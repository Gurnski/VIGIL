"""Checks the ranking score on made-up findings.

Run from the ``python/`` folder:  python -m pytest vigil/analysis/tests/test_score.py -v
"""

import json
import subprocess
import sys
from pathlib import Path

import numpy as np
import pytest

from vigil.analysis.score import (
    PART_NAMES,
    WEIGHTS,
    Evidence,
    cascade_robustness,
    change_point_robustness,
    correlation_certainty,
    cross_system_scores,
    MAX_GROUP_SHARE,
    TOP_N,
    detrended_sd,
    graded_robustness,
    rank_findings,
    score_finding,
    standardised_effect,
    step_sd,
    trend_certainty,
    trend_robustness,
)
from vigil.analysis.synthetic import make_series
from vigil.analysis.trend import deseasonalise, trend_test

REPO = Path(__file__).resolve().parents[4]


def trend(variable, cell, slope, half_width, *, significant=True, coverage=1.0, name=None):
    """A made-up trend finding in the contract's shape (only the fields scoring reads)."""
    return {
        "id": name or f"trend.{variable}.{cell}",
        "kind": "trend",
        "significant": significant,
        "cell": {"id": cell},
        "variables": [{"id": variable}],
        "preprocessing": {"coverage": coverage},
        "result": {"slope_per_decade": slope, "ci_low": slope - half_width, "ci_high": slope + half_width},
    }


def cascade(cell, leading, following, correlation=0.6, p_adjusted=0.001, months=300):
    return {
        "id": f"cascade.{leading}.{following}.{cell}",
        "kind": "cascade",
        "significant": True,
        "cell": {"id": cell},
        "variables": [{"id": leading}, {"id": following}],
        "preprocessing": {"coverage": 1.0, "months_present": months},
        "cascade": {"correlation": correlation, "p_adjusted": p_adjusted},
    }


def evidence_for(findings, sd=1.0, robustness=1.0):
    return {f["id"]: Evidence(noise_sd=sd, robustness=robustness, certainty=0.7) for f in findings}


def by_id(ranked):
    return {f["id"]: f for f in ranked}


def test_parts_stay_between_0_and_1_and_total_is_the_weighted_sum():
    findings = [
        trend("temperature", "r01c01", 0.4, 0.05),
        trend("precipitation", "r01c01", -500.0, 1.0, coverage=0.0),
        trend("vegetation", "r02c02", 0.0, 0.1, significant=False),
        trend("soil_moisture", "r03c03", 1e-9, 1e9),
        cascade("r01c01", "precipitation", "vegetation", correlation=-0.9, p_adjusted=0.0),
    ]
    for f in rank_findings(findings, evidence_for(findings)):
        score = f["score"]
        for name in PART_NAMES:
            assert 0.0 <= score[name] <= 1.0, (f["id"], name)
        recomputed = sum(WEIGHTS[name] * score[name] for name in PART_NAMES)
        assert score["total"] == pytest.approx(recomputed, abs=1e-4)
        assert 0.0 <= score["total"] <= 1.0


def test_weights_sum_to_one_and_are_validated():
    assert sum(WEIGHTS.values()) == pytest.approx(1.0)
    f = trend("temperature", "r01c01", 0.4, 0.1)
    with pytest.raises(ValueError):
        score_finding(f, Evidence(noise_sd=1.0), 0.0, {**WEIGHTS, "effect_size": 0.9})
    with pytest.raises(ValueError):
        score_finding(f, Evidence(), 0.0)  # no noise estimate


def test_larger_and_tighter_beats_smaller_and_looser():
    strong = trend("precipitation", "r01c01", 0.8, 0.1)
    weak = trend("precipitation", "r02c02", 0.3, 0.25)
    only_larger = trend("precipitation", "r03c03", 0.8, 0.5)
    only_tighter = trend("precipitation", "r04c04", 0.3, 0.05)
    findings = [weak, only_larger, only_tighter, strong]
    ranked = rank_findings(findings, evidence_for(findings), diversify=False)
    assert ranked[0]["id"] == strong["id"]
    assert ranked[-1]["id"] == weak["id"]


def test_effect_is_standardised_so_units_do_not_matter():
    # The same signal-to-noise ratio in K and in mm per month scores the same.
    kelvin = trend("temperature", "r01c01", 0.3, 0.05)
    rain = trend("precipitation", "r01c01", 30.0, 5.0)
    a = score_finding(kelvin, Evidence(noise_sd=0.6, robustness=1.0), 0.0)
    b = score_finding(rain, Evidence(noise_sd=60.0, robustness=1.0), 0.0)
    assert a == b


def test_effect_mapping_is_monotonic_and_bounded():
    xs = np.linspace(0, 50, 200)
    ys = [standardised_effect(x, 0.5) for x in xs]
    assert ys[0] == 0.0 and ys[-1] < 1.0
    assert all(b > a for a, b in zip(ys, ys[1:]))
    assert standardised_effect(0.5, 0.5) == pytest.approx(0.5)
    assert trend_certainty(1.0, 0.0, 2.0) == pytest.approx(0.5)  # interval just touches zero
    assert trend_certainty(0.0, -1.0, 1.0) == 0.0


def test_cross_system_boost_for_two_or_more_variables_in_a_cell():
    alone = trend("precipitation", "r01c01", 0.5, 0.1)
    together_a = trend("temperature", "r09c09", 0.5, 0.1)
    together_b = trend("vegetation", "r09c09", 0.5, 0.1)
    three = [trend(v, "r05c05", 0.5, 0.1) for v in ("temperature", "soil_moisture", "vegetation")]
    unrelated_nonsig = trend("soil_moisture", "r01c01", 0.0, 1.0, significant=False)
    findings = [alone, together_a, together_b, unrelated_nonsig, *three]
    cross = cross_system_scores(findings)
    assert cross[alone["id"]] == 0.0  # a non-significant neighbour does not count
    assert cross[together_a["id"]] == cross[together_b["id"]] == 0.5
    assert all(cross[f["id"]] == 0.8 for f in three)
    assert cross[unrelated_nonsig["id"]] == 0.0

    ranked = by_id(rank_findings(findings, evidence_for(findings), diversify=False))
    assert ranked[together_a["id"]]["rank"] < ranked[alone["id"]]["rank"]
    assert ranked[three[0]["id"]]["rank"] < ranked[together_a["id"]]["rank"]


def test_a_cascade_counts_both_variables_in_its_cell():
    c = cascade("r01c01", "precipitation", "vegetation")
    t = trend("temperature", "r01c01", 0.5, 0.1)
    cross = cross_system_scores([c, t])
    assert cross[c["id"]] == 0.8 and cross[t["id"]] == 0.8


def test_cascade_certainty_does_not_saturate_and_tells_cascades_apart():
    # Every one of these has a p-value far below 1e-4 over 300 months.
    values = [correlation_certainty(r, 300) for r in (0.3, 0.5, 0.7, 0.9, 0.99)]
    assert all(b > a for a, b in zip(values, values[1:]))
    assert all(0.0 < v < 1.0 for v in values)
    assert values[-1] - values[0] > 0.2  # a real spread, not a constant

    assert correlation_certainty(0.5, 60) < correlation_certainty(0.5, 300)  # shorter record
    assert correlation_certainty(-0.5, 300) == correlation_certainty(0.5, 300)
    assert correlation_certainty(0.0, 300) == 0.0
    assert correlation_certainty(0.5, 3) == 0.0
    assert correlation_certainty(1.0, 300) <= 1.0
    # An interval that reaches zero scores about a half, as for a trend.
    assert correlation_certainty(0.113, 300) == pytest.approx(0.5, abs=0.03)

    weak, strong = cascade("r01c01", "precipitation", "vegetation", 0.25), cascade("r02c02", "precipitation", "vegetation", 0.8)
    ev = Evidence(robustness=1.0)
    assert score_finding(weak, ev, 0.5)["certainty"] < score_finding(strong, ev, 0.5)["certainty"] < 1.0
    # Allowing for autocorrelation (fewer independent months) lowers it.
    assert score_finding(strong, Evidence(robustness=1.0, effective_months=80), 0.5)["certainty"] < score_finding(strong, ev, 0.5)["certainty"]


def test_temperature_does_not_crowd_out_other_variables():
    # Temperature is stronger and tighter than everything else, in 40 cells.
    temps = [trend("temperature", f"r10c{i:02d}", 0.9, 0.02) for i in range(40)]
    rain = [trend("precipitation", f"r20c{i:02d}", 0.4, 0.15) for i in range(10)]
    veg = [trend("vegetation", f"r30c{i:02d}", 0.4, 0.15) for i in range(10)]
    soil = [trend("soil_moisture", f"r40c{i:02d}", 0.4, 0.15) for i in range(10)]
    findings = temps + rain + veg + soil

    plain = rank_findings(findings, evidence_for(findings), diversify=False)
    assert all(f["variables"][0]["id"] == "temperature" for f in plain[:20])  # the problem

    ranked = rank_findings(findings, evidence_for(findings))
    top = [f["variables"][0]["id"] for f in ranked[:20]]
    assert top.count("temperature") <= 5
    assert {"precipitation", "vegetation", "soil_moisture"} <= set(top)
    assert [f["rank"] for f in ranked] == list(range(1, len(findings) + 1))
    assert len(ranked) == len(findings)  # nothing dropped


def test_cap_relaxes_when_only_one_variable_exists():
    findings = [trend("temperature", f"r10c{i:02d}", 0.9, 0.02) for i in range(30)]
    ranked = rank_findings(findings, evidence_for(findings))
    assert len(ranked) == 30
    assert [f["rank"] for f in ranked] == list(range(1, 31))


def test_significant_findings_rank_above_non_significant_ones():
    loud_but_not_significant = trend("precipitation", "r01c01", 5.0, 0.01, significant=False)
    quiet_significant = trend("precipitation", "r02c02", 0.1, 0.05)
    findings = [loud_but_not_significant, quiet_significant]
    ranked = rank_findings(findings, evidence_for(findings))
    assert ranked[0]["id"] == quiet_significant["id"]


def test_ties_are_stable_whatever_order_the_findings_arrive_in():
    findings = [trend("precipitation", f"r{i:02d}c01", 0.5, 0.1) for i in range(12)]
    expected = [f["id"] for f in rank_findings(findings, evidence_for(findings))]
    assert expected == sorted(expected)  # equal totals fall back to id
    rng = np.random.default_rng(1)
    for _ in range(5):
        shuffled = [findings[i] for i in rng.permutation(len(findings))]
        assert [f["id"] for f in rank_findings(shuffled, evidence_for(shuffled))] == expected


def test_input_findings_are_not_changed():
    f = trend("temperature", "r01c01", 0.4, 0.1)
    rank_findings([f], evidence_for([f]))
    assert "score" not in f and "rank" not in f


def test_trend_robustness_is_graded_with_no_cliff_at_alpha():
    assert graded_robustness(True, 0.001, 0.05) == 1.0  # cross-check passes and agrees
    assert graded_robustness(False, 0.001, 0.05) == 0.0  # passes but disagrees (opposite sign)
    assert graded_robustness(False, 0.05, 0.05) == pytest.approx(1.0)
    assert graded_robustness(False, 0.051, 0.05) > 0.99  # just missing costs almost nothing
    assert graded_robustness(False, 0.2, 0.05) == pytest.approx(0.537, abs=0.001)
    assert graded_robustness(False, 1.0, 0.05) == 0.0
    ps = np.linspace(0.05, 1.0, 50)
    values = [graded_robustness(False, p, 0.05) for p in ps]
    assert all(b < a for a, b in zip(values, values[1:]))
    with pytest.raises(ValueError):
        graded_robustness(False, float("nan"), 0.05)

    # From real TrendResults: a clear trend is fully robust, flat noise is not.
    rng = np.random.default_rng(5)
    clear = trend_test(deseasonalise(make_series(300, trend_per_decade=0.8, noise_sd=0.5, rng=rng)))
    flat = trend_test(deseasonalise(make_series(300, noise_sd=0.5, rng=rng)))
    assert trend_robustness(clear) == 1.0
    assert 0.0 <= trend_robustness(flat) < 1.0


def test_robustness_helpers():
    assert change_point_robustness(100, 100) == 1.0
    assert change_point_robustness(100, 106) == pytest.approx(0.5)
    assert change_point_robustness(100, 130) == 0.0
    assert change_point_robustness(100, None) == 0.0
    assert cascade_robustness([0.1, 0.5, 0.6, 0.55, 0.2], lag=2) == pytest.approx((0.5 / 0.6 + 0.55 / 0.6) / 2)
    assert cascade_robustness([0.1, -0.5, 0.6], lag=2) == 0.0  # only neighbour has the opposite sign


def test_change_point_and_contradiction_scores():
    cp = {
        "id": "change_point.vegetation.r01c01", "kind": "change_point", "significant": True,
        "cell": {"id": "r01c01"}, "variables": [{"id": "vegetation"}], "preprocessing": {"coverage": 0.98},
        "change_point": {"shift": -2.0, "date_low": "2015-01-01", "date_high": "2016-01-01"},
    }
    contra = {
        "id": "contradiction.vegetation.r02c02", "kind": "contradiction", "significant": True,
        "cell": {"id": "r02c02"}, "variables": [{"id": "vegetation"}], "preprocessing": {"coverage": 1.0},
        "contradiction": {"slope_per_decade": 0.4, "paired_slope_per_decade": -0.4},
    }
    ev = {cp["id"]: Evidence(noise_sd=1.0, robustness=0.9), contra["id"]: Evidence(noise_sd=1.0, robustness=1.0, certainty=0.8)}
    got = by_id(rank_findings([cp, contra], ev))
    assert got[cp["id"]]["score"]["effect_size"] == pytest.approx(2 / 3, abs=1e-4)
    assert got[cp["id"]]["score"]["certainty"] == pytest.approx(0.5, abs=1e-4)  # 12 months wide
    assert got[contra["id"]]["score"]["certainty"] == 0.8
    with pytest.raises(ValueError):
        rank_findings([contra], {contra["id"]: Evidence(noise_sd=1.0)})


def test_missing_or_broken_inputs_fail_loudly():
    f = trend("temperature", "r01c01", 0.4, 0.1)
    with pytest.raises(ValueError):
        rank_findings([f], {})  # no evidence at all
    with pytest.raises(ValueError):
        rank_findings([f], {f["id"]: Evidence(noise_sd=float("nan"), robustness=1.0)})
    broken = trend("temperature", "r02c02", float("nan"), 0.1)
    with pytest.raises(ValueError):
        rank_findings([broken], evidence_for([broken]))


def test_contradiction_effect_matches_the_trends_it_is_made_from():
    contra = {
        "id": "contradiction.vegetation.r02c02", "kind": "contradiction", "significant": True,
        "cell": {"id": "r02c02"}, "variables": [{"id": "vegetation"}], "preprocessing": {"coverage": 1.0},
        "contradiction": {"slope_per_decade": 0.4, "paired_slope_per_decade": -0.4},
    }
    one_side = trend("vegetation", "r02c02", 0.4, 0.1)
    ev = Evidence(noise_sd=1.0, robustness=1.0, certainty=0.8)
    assert score_finding(contra, ev, 0.0)["effect_size"] == score_finding(one_side, ev, 0.0)["effect_size"]


def test_step_sd_is_not_inflated_by_the_step():
    rng = np.random.default_rng(3)
    raw = make_series(300, noise_sd=0.5, step_month=150, step_size=3.0, rng=rng)
    assert step_sd(raw, 150) == pytest.approx(0.5, rel=0.15)
    assert detrended_sd(raw) > 1.5 * step_sd(raw, 150)  # why change points need step_sd


def test_end_to_end_from_series_through_the_trend_test_to_the_score():
    """Real TrendResults, not hand-made numbers: a strong clean trend must outrank a weak noisy one."""
    rng = np.random.default_rng(11)
    findings, evidence = [], {}
    for cell, slope, noise in (("r01c01", 0.6, 0.5), ("r02c02", 0.15, 1.0)):
        anomalies = deseasonalise(make_series(300, trend_per_decade=slope, noise_sd=noise, ar1=0.3, missing_fraction=0.05, rng=rng))
        result = trend_test(anomalies)
        findings.append({
            "id": f"trend.precipitation.{cell}", "kind": "trend", "significant": result.p_value < 0.05,
            "cell": {"id": cell}, "variables": [{"id": "precipitation"}],
            "preprocessing": {"coverage": result.coverage},
            "result": result.result_block(result.p_value, "mm per month per decade"),
        })
        evidence[findings[-1]["id"]] = Evidence(noise_sd=detrended_sd(anomalies), robustness=trend_robustness(result))
    ranked = rank_findings(findings, evidence)
    assert ranked[0]["id"] == "trend.precipitation.r01c01"
    strong, weak = ranked[0]["score"], ranked[1]["score"]
    assert strong["effect_size"] > weak["effect_size"] and strong["certainty"] > weak["certainty"]
    assert strong["effect_size"] == pytest.approx(1.2 / 1.7, abs=0.08)  # 0.6 / 0.5 sigma per decade


def test_detrended_sd_ignores_the_trend_and_matches_the_noise():
    rng = np.random.default_rng(7)
    raw = make_series(300, trend_per_decade=3.0, noise_sd=0.5, seasonal_amplitude=4.0, rng=rng)
    assert detrended_sd(deseasonalise(raw)) == pytest.approx(0.5, rel=0.15)


def test_rescored_fixture_passes_the_contract_checker(tmp_path):
    """Re-score the contract's fixture with this module and run check.py on it."""
    snapshot = json.loads((REPO / "contract" / "fixture" / "snapshot.json").read_text())
    series = {(s["cell_id"], s["variable"]): s for s in snapshot["series"]}
    findings, evidence = snapshot["findings"], {}
    for f in findings:
        s = series[(f["cell"]["id"], f["variables"][0]["id"])]
        anomaly = [np.nan if v is None else v for v in s["anomaly"]]
        evidence[f["id"]] = Evidence(noise_sd=detrended_sd(anomaly), robustness=0.8, certainty=0.7)
    settings = snapshot["run"]["settings"]
    # The contract's copies of the weights and the cap must match this module's.
    assert settings["score_weights"] == WEIGHTS
    assert settings["rank_cap"] == {"top_n": TOP_N, "max_share": MAX_GROUP_SHARE}
    snapshot["findings"] = rank_findings(findings, evidence, settings["score_weights"], **settings["rank_cap"])
    path = tmp_path / "rescored.json"
    path.write_text(json.dumps(snapshot, allow_nan=False))
    done = subprocess.run([sys.executable, str(REPO / "contract" / "tools" / "check.py"), str(path)], capture_output=True, text=True)
    assert done.returncode == 0, done.stdout + done.stderr
