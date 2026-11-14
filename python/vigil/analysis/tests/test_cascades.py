"""Checks cascade finding: the statistics on many made-up pairs, then the findings on a small made-up grid.

Run from the ``python/`` folder:  python -m pytest vigil/analysis/tests/test_cascades.py -v
"""

import copy
import json
from pathlib import Path

import jsonschema
import numpy as np
import pytest
from scipy.stats import t as student_t

from vigil.analysis.cascades import (
    add_cascades,
    adjust_candidates,
    anomalies,
    detrend,
    lagged_link_tests,
    limitations,
)
from vigil.analysis.contradictions import add_contradictions
from vigil.analysis.fdr import benjamini_hochberg
from vigil.analysis.scan import scan_trends
from vigil.analysis.score import cascade_robustness, correlation_certainty, rank_findings
from vigil.analysis.synthetic import make_series
from vigil.analysis.trend import deseasonalise

REPO = Path(__file__).resolve().parents[4]
ROWS, COLUMNS, MONTHS = 4, 5, 240
GRID = {"cell_degrees": 5.0, "rows": ROWS, "columns": COLUMNS, "lat_min": -10.0, "lon_min": -20.0}
VARIABLES = ("temperature", "precipitation", "soil_moisture", "vegetation")
BASELINE = {"temperature": 285.0, "precipitation": 80.0, "soil_moisture": 25.0, "vegetation": 10.0}
LONG = 300          # months in the simulations of the statistics


# ------------------------------------------------------------------ many made-up pairs

def block(n, rng, months=LONG, **kwargs):
    """``n`` made-up series from ``make_series``, as rows."""
    return np.stack([make_series(months, rng=rng, **kwargs) for _ in range(n)])


def follow(leader, own, lag, strength):
    """A series that follows ``leader`` by ``lag`` months with correlation ``strength``. Rows are series."""
    shifted = own.copy()
    shifted[:, lag:] = leader[:, : leader.shape[1] - lag]
    return strength * shifted + np.sqrt(1 - strength**2) * own


def test_unrelated_autocorrelated_pairs_are_called_linked_at_no_more_than_the_stated_rate():
    """3,000 independent pairs with memory. At the 5% level about 5% or fewer may pass, not the 20% the plain test gives."""
    rng = np.random.default_rng(101)
    a = anomalies(block(3000, rng, ar1=0.6, seasonal_amplitude=2.0, baseline=5.0), 1)
    b = anomalies(block(3000, rng, ar1=0.3, seasonal_amplitude=1.0), 1)
    tests = lagged_link_tests(a, b, max_lag=6)
    rate = np.mean(tests.p_value < 0.05)
    assert 0.015 < rate < 0.06
    # with the rule that the lead must be clear, fewer still
    assert np.mean((tests.p_value < 0.05) & tests.clear_lead) < rate
    # The plain test of the best lag (every month counted as independent, the choice of lag not paid for)
    # would call a large share of these unrelated pairs linked.
    r, n = tests.correlation, tests.months_paired
    plain = 2 * student_t.sf(np.abs(r) * np.sqrt((n - 2) / (1 - r**2)), n - 2)
    assert np.mean(plain < 0.05) > 0.30
    # Paying for the 13 lags alone, without the autocorrelation, is still too confident.
    assert np.mean(1 - (1 - plain) ** 13 < 0.05) > 0.08


def test_very_persistent_unrelated_pairs_are_not_called_linked_either():
    rng = np.random.default_rng(102)
    a, b = block(1500, rng, ar1=0.9), block(1500, rng, ar1=0.9)
    tests = lagged_link_tests(a, b, max_lag=6)
    assert np.mean(tests.p_value < 0.05) < 0.05
    assert np.median(tests.effective_months) < 80          # 300 months of AR(0.9) are worth far fewer


def test_unrelated_series_that_both_trend_are_not_called_linked():
    """The trap in brief note A05: a shared trend correlates at every lag. Detrending removes it."""
    rng = np.random.default_rng(103)
    a = block(1500, rng, ar1=0.4, trend_per_decade=1.5)
    b = block(1500, rng, ar1=0.4, trend_per_decade=-1.5)
    tests = lagged_link_tests(a, b, max_lag=6)
    assert np.mean(tests.p_value < 0.05) < 0.06
    assert np.median(np.abs(tests.correlations[:, 6])) < 0.1
    # Without detrending the same pairs are strongly correlated in the same month.
    raw = np.array([np.corrcoef(x, y)[0, 1] for x, y in zip(a[:200], b[:200])])
    assert np.median(raw) < -0.5


def test_a_planted_link_is_found_at_its_lag_and_in_its_direction():
    rng = np.random.default_rng(104)
    leader = block(500, rng, ar1=0.4)
    follower = follow(leader, block(500, rng, ar1=0.4), lag=3, strength=0.45)
    forward = lagged_link_tests(leader, follower, max_lag=6)
    assert np.mean((forward.lag == 3) & forward.clear_lead & (forward.p_value < 0.05)) > 0.95
    assert np.median(forward.correlation) == pytest.approx(0.45, abs=0.05)
    # Given the other way round, the lag is negative: the second series leads.
    backward = lagged_link_tests(follower, leader, max_lag=6)
    assert np.array_equal(backward.lag, -forward.lag)
    assert np.allclose(backward.correlation, forward.correlation)
    assert np.allclose(backward.p_value, forward.p_value)
    assert np.allclose(backward.correlations, forward.correlations[:, ::-1])


def test_a_negative_link_keeps_its_sign_and_its_interval():
    rng = np.random.default_rng(105)
    leader = block(200, rng, ar1=0.3)
    follower = follow(leader, block(200, rng, ar1=0.3), lag=2, strength=0.5) * -1
    tests = lagged_link_tests(leader, follower, max_lag=6)
    assert np.mean((tests.lag == 2) & (tests.correlation < 0) & tests.clear_lead) > 0.95
    assert np.all(tests.ci_low <= tests.correlation) and np.all(tests.correlation <= tests.ci_high)


def test_a_same_month_link_is_significant_but_has_no_lead():
    rng = np.random.default_rng(106)
    leader = block(500, rng, ar1=0.5)
    together = follow(leader, block(500, rng, ar1=0.5), lag=0, strength=0.6)
    tests = lagged_link_tests(leader, together, max_lag=6)
    assert np.mean(tests.p_value < 0.05) > 0.99
    assert np.mean(tests.lag == 0) > 0.97
    assert np.mean(tests.clear_lead) < 0.02


def test_missing_months_do_not_break_the_test_or_raise_the_false_positive_rate():
    rng = np.random.default_rng(107)
    a = block(1500, rng, ar1=0.5, missing_fraction=0.06)
    b = block(1500, rng, ar1=0.5, missing_fraction=0.06)
    tests = lagged_link_tests(a, b, max_lag=6)
    assert np.isfinite(tests.correlations).all() and np.isfinite(tests.p_value).all()
    assert np.mean(tests.p_value < 0.05) < 0.06
    assert tests.months_paired.max() < LONG


def test_many_same_month_links_do_not_let_false_leads_into_the_list():
    """Why the candidates are charged for the whole family, not corrected first and filtered afterwards."""
    rng = np.random.default_rng(108)
    same, lagged, null = 300, 25, 175
    listed_plain = false_plain = listed_charged = false_charged = found = 0
    for _ in range(12):
        leader = block(same + lagged + null, rng, ar1=0.5)
        other = block(same + lagged + null, rng, ar1=0.5)
        other[:same] = follow(leader[:same], other[:same], lag=0, strength=0.6)
        other[same : same + lagged] = follow(leader[same : same + lagged], other[same : same + lagged], lag=2, strength=0.6)
        real = np.zeros(same + lagged + null, dtype=bool)
        real[same : same + lagged] = True
        tests = lagged_link_tests(leader, other, max_lag=6)

        plain = (benjamini_hochberg(tests.p_value, 0.05)[0] < 0.05) & tests.clear_lead
        listed_plain += plain.sum()
        false_plain += (plain & ~real).sum()

        candidates = np.nonzero(tests.clear_lead)[0]
        charged = np.zeros(real.size, dtype=bool)
        charged[candidates[adjust_candidates(tests.p_value[candidates], real.size) < 0.05]] = True
        listed_charged += charged.sum()
        false_charged += (charged & ~real).sum()
        found += (charged & real).sum()
    assert found == 12 * lagged                           # nothing real is lost
    assert false_charged / listed_charged < 0.05          # the stated rate holds among what is listed
    assert false_plain > 2 * max(false_charged, 1)        # correcting first and filtering afterwards lets more through


def test_adjust_candidates_is_benjamini_hochberg_when_every_test_is_a_candidate():
    p = np.random.default_rng(109).random(40) ** 3
    assert np.allclose(adjust_candidates(p, 40), benjamini_hochberg(p)[0])
    charged = adjust_candidates(p, 400)
    assert np.all(charged >= adjust_candidates(p, 40)) and np.all(charged >= p) and np.all(charged <= 1)
    assert adjust_candidates([], 10).size == 0
    with pytest.raises(ValueError):
        adjust_candidates(p, 39)


def test_anomalies_match_deseasonalise_row_by_row():
    rng = np.random.default_rng(110)
    raw = block(6, rng, months=100, ar1=0.3, seasonal_amplitude=4.0, baseline=20.0, missing_fraction=0.1)
    raw[2, 3::12] = np.nan                               # a calendar month with no value at all stays missing
    fast = anomalies(raw, start_month=4)
    for row, series in zip(fast, raw):
        assert np.allclose(row, deseasonalise(series, start_month=4), equal_nan=True, atol=1e-12)


def test_detrend_removes_a_line_and_keeps_the_gaps():
    t = np.arange(120.0)
    rng = np.random.default_rng(111)
    series = np.stack([3.0 + 0.05 * t, -1.0 - 0.2 * t + rng.standard_normal(120)])
    series[1, [5, 40, 41]] = np.nan
    out = detrend(series)
    assert np.allclose(out[0], 0, atol=1e-9)
    assert np.isnan(out[1, [5, 40, 41]]).all() and np.isfinite(np.delete(out[1], [5, 40, 41])).all()
    present = np.isfinite(out[1])
    assert abs(np.polyfit(t[present], out[1][present], 1)[0]) < 1e-9
    assert abs(out[1][present].mean()) < 1e-9


# ------------------------------------------------------------------ a small made-up grid

LINK = (1, 1)        # soil moisture follows precipitation by 2 months; precipitation has a trend
REVERSED = (2, 3)    # temperature follows vegetation by 3 months, negatively; both have trends
TRENDS_ONLY = (0, 4) # temperature and soil moisture both trend, with no month-to-month link
TOGETHER = (3, 0)    # soil moisture moves with precipitation in the same month; precipitation has a trend
NO_TREND = (2, 0)    # soil moisture follows precipitation by 2 months, and nothing trends
CHAIN = (1, 3)       # precipitation, then soil moisture a month later, then vegetation two months after that; two trends


def world(seed=1, change=None):
    """Four variables on the small grid: independent noise everywhere, plus the cells named above.

    About 17 pairs of unrelated series are tested in each world at the 5% level, so some seeds hold a
    chance cascade (seeds 0, 2 and 7 of the first eight do). The default seed is one that does not, so
    the tests below can name exactly the planted links. The false-positive rate is measured above.
    """
    rng = np.random.default_rng(seed)
    noise = {v: np.stack([[make_series(MONTHS, ar1=0.3, rng=rng) for _ in range(COLUMNS)] for _ in range(ROWS)])
             for v in VARIABLES}
    trend = {v: np.zeros((ROWS, COLUMNS)) for v in VARIABLES}

    def link(follower, leader, cell, lag, strength):
        noise[follower][cell] = follow(noise[leader][cell][None], noise[follower][cell][None], lag, abs(strength))[0]
        if strength < 0:
            noise[follower][cell] *= -1

    link("soil_moisture", "precipitation", LINK, 2, 0.7)
    trend["precipitation"][LINK] = 2.0
    link("temperature", "vegetation", REVERSED, 3, -0.7)
    trend["temperature"][REVERSED], trend["vegetation"][REVERSED] = 2.0, -2.0
    trend["temperature"][TRENDS_ONLY], trend["soil_moisture"][TRENDS_ONLY] = 3.0, -3.0
    link("soil_moisture", "precipitation", TOGETHER, 0, 0.7)
    trend["precipitation"][TOGETHER] = -2.0
    link("soil_moisture", "precipitation", NO_TREND, 2, 0.7)
    link("soil_moisture", "precipitation", CHAIN, 1, 0.8)
    link("vegetation", "soil_moisture", CHAIN, 2, 0.8)
    trend["precipitation"][CHAIN], trend["vegetation"][CHAIN] = -2.0, -2.0

    t = np.arange(MONTHS)
    season = 3.0 * np.sin(2 * np.pi * (t % 12) / 12)
    data = {v: BASELINE[v] + season + trend[v][:, :, None] * t / 120.0 + noise[v] for v in VARIABLES}
    if change:
        change(data)
    return data


def scanned(data, **kwargs):
    return scan_trends(data, start_month=1, grid=GRID, rank_cap=kwargs.pop("rank_cap", None), **kwargs)


def cascades_for(data, result, **kwargs):
    return add_cascades(data, result.findings, result.evidence, start_month=1, grid=GRID, **kwargs)


def of_kind(findings, kind):
    return [f for f in findings if f["kind"] == kind]


def cell_id(cell):
    return f"r{cell[0]:02d}c{cell[1]:03d}"


@pytest.fixture(scope="module")
def small_world():
    data = world()
    result = scanned(data)
    return data, result, cascades_for(data, result)


def test_the_planted_cells_have_the_trends_the_cascade_rule_needs(small_world):
    _, result, _ = small_world
    trends = {f["id"] for f in result.findings}
    assert {f"trend.precipitation.{cell_id(LINK)}", f"trend.temperature.{cell_id(REVERSED)}",
            f"trend.vegetation.{cell_id(REVERSED)}", f"trend.temperature.{cell_id(TRENDS_ONLY)}",
            f"trend.soil_moisture.{cell_id(TRENDS_ONLY)}", f"trend.precipitation.{cell_id(TOGETHER)}",
            f"trend.precipitation.{cell_id(CHAIN)}"} <= trends
    assert not any(f["cell"]["id"] == cell_id(NO_TREND) for f in result.findings)


def test_the_planted_links_are_the_cascades_found(small_world):
    _, _, combined = small_world
    found = {f["id"] for f in of_kind(combined.findings, "cascade")}
    assert found == {
        f"cascade.precipitation-soil_moisture.{cell_id(LINK)}",
        f"cascade.vegetation-temperature.{cell_id(REVERSED)}",
        f"cascade.precipitation-soil_moisture.{cell_id(CHAIN)}",
        f"cascade.soil_moisture-vegetation.{cell_id(CHAIN)}",
        f"cascade.precipitation-vegetation.{cell_id(CHAIN)}",      # the two links of the chain, end to end
    }
    assert combined.cascades == 5


def test_a_link_is_reported_at_its_lag_with_the_leader_first(small_world):
    _, _, combined = small_world
    by_id = {f["id"]: f for f in combined.findings}
    c = by_id[f"cascade.precipitation-soil_moisture.{cell_id(LINK)}"]
    block_ = c["cascade"]
    assert c["significant"] is True
    assert [v["id"] for v in c["variables"]] == ["precipitation", "soil_moisture"]
    assert [v["units"] for v in c["variables"]] == ["mm per month", "kg per m2"]
    assert (block_["leading_variable"], block_["following_variable"]) == ("precipitation", "soil_moisture")
    assert block_["lag_months"] == 2
    assert block_["correlation"] == pytest.approx(0.7, abs=0.08)
    assert len(block_["correlations_by_lag"]) == 7
    assert block_["correlations_by_lag"][2] == block_["correlation"]
    assert max(block_["correlations_by_lag"], key=abs) == block_["correlation"]
    assert 0 <= block_["p_value"] <= block_["p_adjusted"] < 0.05
    assert c["preprocessing"] == {"months_present": MONTHS, "months_expected": MONTHS, "coverage": 1.0}
    assert c["cell"] == {"id": "r01c001", "row": 1, "column": 1, "lat_min": -5.0, "lat_max": 0.0,
                         "lon_min": -15.0, "lon_max": -10.0}


def test_the_data_decide_who_leads_and_a_negative_link_keeps_its_sign(small_world):
    """Temperature comes before vegetation in ``data``, but here vegetation moves first."""
    _, _, combined = small_world
    by_id = {f["id"]: f for f in combined.findings}
    c = by_id[f"cascade.vegetation-temperature.{cell_id(REVERSED)}"]
    assert [v["id"] for v in c["variables"]] == ["vegetation", "temperature"]
    assert c["cascade"]["lag_months"] == 3
    assert c["cascade"]["correlation"] == pytest.approx(-0.7, abs=0.08)
    parameters = c["method"]["parameters"]
    assert parameters["family"] == "temperature-vegetation"
    # The other direction was looked at, and is far weaker.
    assert 1 <= parameters["strongest_reverse_lag_months"] <= 6
    assert abs(parameters["strongest_reverse_correlation"]) < 0.35


def test_the_method_block_says_what_was_done(small_world):
    _, _, combined = small_world
    c = of_kind(combined.findings, "cascade")[0]
    parameters = c["method"]["parameters"]
    assert c["method"]["test"] == "lagged_pearson_correlation"
    assert parameters["max_lag_months"] == 6 and parameters["lags_tested"] == 13
    assert parameters["detrended"] is True and parameters["series"] == "anomaly"
    assert parameters["lag_selection_correction"] == "sidak"
    assert parameters["multiple_testing"] == "benjamini_hochberg" and parameters["threshold"] == 0.05
    assert parameters["cells_tested"] == "cells with a significant trend in either variable"
    assert parameters["p_value_at_lag"] <= c["cascade"]["p_value"]
    assert 4 <= parameters["effective_months"] <= parameters["months_paired"] <= MONTHS
    assert parameters["correlation_ci_low"] <= c["cascade"]["correlation"] <= parameters["correlation_ci_high"]
    assert all(isinstance(v, (str, int, float, bool)) for v in parameters.values())      # flat, as the contract asks


def test_two_trends_without_a_monthly_link_are_not_a_cascade(small_world):
    _, _, combined = small_world
    assert not any(f["cell"]["id"] == cell_id(TRENDS_ONLY) for f in of_kind(combined.findings, "cascade"))
    counts = {row["pair"]: row for row in combined.counts}
    assert counts["temperature-soil_moisture"]["tests_run"] >= 1          # it was tested, and did not pass


def test_a_same_month_link_is_counted_and_not_listed(small_world):
    _, _, combined = small_world
    assert not any(f["cell"]["id"] == cell_id(TOGETHER) for f in of_kind(combined.findings, "cascade"))
    counts = {row["pair"]: row for row in combined.counts}
    assert counts["precipitation-soil_moisture"]["strongest_at_lag_0"] >= 1


def test_a_cell_without_a_trend_is_only_tested_when_asked(small_world):
    data, result, combined = small_world
    wanted = f"cascade.precipitation-soil_moisture.{cell_id(NO_TREND)}"
    assert wanted not in {f["id"] for f in combined.findings}
    everywhere = cascades_for(data, result, cells="all")
    by_id = {f["id"]: f for f in everywhere.findings}
    assert by_id[wanted]["cascade"]["lag_months"] == 2
    assert by_id[wanted]["links"] == []                                    # no trend in the cell to link to
    assert by_id[wanted]["method"]["parameters"]["cells_tested"] == "every cell where both variables were scanned"
    row = {r["pair"]: r for r in everywhere.counts}["precipitation-soil_moisture"]
    assert row["tests_run"] == ROWS * COLUMNS
    assert by_id[wanted]["method"]["parameters"]["family_tests"] == ROWS * COLUMNS


def test_counts_add_up(small_world):
    _, _, combined = small_world
    assert [row["pair"] for row in combined.counts] == [
        "temperature-precipitation", "temperature-soil_moisture", "temperature-vegetation",
        "precipitation-soil_moisture", "precipitation-vegetation", "soil_moisture-vegetation"]
    for row in combined.counts:
        assert row["tests_run"] == row["strongest_at_lag_0"] + row["lead_not_clear"] + row["candidates"]
        assert row["significant"] <= row["candidates"]
    assert sum(row["significant"] for row in combined.counts) == combined.cascades


# ------------------------------------------------------------------ links

def test_links_are_written_on_both_ends_with_the_lag_and_the_correlation(small_world):
    _, _, combined = small_world
    by_id = {f["id"]: f for f in combined.findings}
    c = by_id[f"cascade.vegetation-temperature.{cell_id(REVERSED)}"]
    measured = {"lag_months": 3, "correlation": c["cascade"]["correlation"]}
    assert c["links"] == [
        {"finding_id": f"trend.vegetation.{cell_id(REVERSED)}", "kind": "trend", **measured},
        {"finding_id": f"trend.temperature.{cell_id(REVERSED)}", "kind": "trend", **measured},
    ]
    for trend_id in (f"trend.vegetation.{cell_id(REVERSED)}", f"trend.temperature.{cell_id(REVERSED)}"):
        assert by_id[trend_id]["links"] == [{"finding_id": c["id"], "kind": "cascade", **measured}]
    # Only one of the two variables has a trend here, so there is one link.
    alone = by_id[f"cascade.precipitation-soil_moisture.{cell_id(LINK)}"]
    assert [link["finding_id"] for link in alone["links"]] == [f"trend.precipitation.{cell_id(LINK)}"]


def test_cascades_in_one_cell_that_share_a_variable_link_to_each_other(small_world):
    _, _, combined = small_world
    by_id = {f["id"]: f for f in combined.findings}
    first = by_id[f"cascade.precipitation-soil_moisture.{cell_id(CHAIN)}"]
    second = by_id[f"cascade.soil_moisture-vegetation.{cell_id(CHAIN)}"]
    assert (first["cascade"]["lag_months"], second["cascade"]["lag_months"]) == (1, 2)
    assert {"finding_id": second["id"], "kind": "cascade"} in first["links"]
    assert {"finding_id": first["id"], "kind": "cascade"} in second["links"]


def test_every_link_resolves_and_is_answered(small_world):
    _, _, combined = small_world
    by_id = {f["id"]: f for f in combined.findings}
    for f in combined.findings:
        assert isinstance(f["links"], list)
        for link in f["links"]:
            target = by_id[link["finding_id"]]
            assert link["kind"] == target["kind"]
            assert any(back["finding_id"] == f["id"] for back in target["links"])


def test_links_from_contradictions_are_kept():
    def oppose(data):
        t = np.arange(MONTHS)
        data["precipitation"][LINK[0], LINK[1] + 1] -= 2.0 * t / 120.0     # the cell to the east dries
    data = world(change=oppose)
    result = scanned(data)
    with_pairs = add_contradictions(result.findings, result.evidence, grid=GRID, threshold=0.05)
    pair = "contradiction.precipitation.r01c001-r01c002"
    assert pair in {f["id"] for f in with_pairs.findings}
    combined = add_cascades(data, with_pairs.findings, with_pairs.evidence, start_month=1, grid=GRID)
    trend = {f["id"]: f for f in combined.findings}[f"trend.precipitation.{cell_id(LINK)}"]
    assert [link["finding_id"] for link in trend["links"]] == [pair, f"cascade.precipitation-soil_moisture.{cell_id(LINK)}"]
    assert len(of_kind(combined.findings, "contradiction")) == with_pairs.contradictions
    before = {f["id"]: f for f in with_pairs.findings}
    assert all(f["links"] == before[f["id"]]["links"] for f in of_kind(combined.findings, "contradiction"))


# ------------------------------------------------------------------ ranking and evidence

def test_the_list_is_ranked_together_and_scored_with_the_usual_parts(small_world):
    _, result, combined = small_world
    assert len(combined.findings) == len(result.findings) + combined.cascades
    assert [f["rank"] for f in combined.findings] == list(range(1, len(combined.findings) + 1))
    for f in combined.findings:
        assert set(f["score"]) == {"total", "effect_size", "certainty", "coverage", "robustness", "cross_system"}
    again = rank_findings(combined.findings, combined.evidence, diversify=False)
    assert [f["id"] for f in again] == [f["id"] for f in combined.findings]


def test_a_cascade_is_scored_from_its_correlation_and_its_effective_months(small_world):
    _, _, combined = small_world
    c = {f["id"]: f for f in combined.findings}[f"cascade.precipitation-soil_moisture.{cell_id(LINK)}"]
    block_, evidence = c["cascade"], combined.evidence[c["id"]]
    assert evidence.effective_months == pytest.approx(c["method"]["parameters"]["effective_months"], abs=0.06)
    assert evidence.effective_months <= MONTHS - 2
    assert c["score"]["effect_size"] == pytest.approx(abs(block_["correlation"]), abs=1e-4)
    assert c["score"]["certainty"] == pytest.approx(
        correlation_certainty(block_["correlation"], evidence.effective_months), abs=1e-4)
    assert c["score"]["robustness"] == pytest.approx(
        cascade_robustness(block_["correlations_by_lag"], block_["lag_months"]), abs=1e-4)
    assert evidence.robustness == cascade_robustness(block_["correlations_by_lag"], block_["lag_months"])


def test_a_cascade_gives_cross_system_credit_to_the_trend_beside_it(small_world):
    _, result, combined = small_world
    trend_id = f"trend.precipitation.{cell_id(LINK)}"
    before = {f["id"]: f for f in result.findings}[trend_id]["score"]
    after = {f["id"]: f for f in combined.findings}[trend_id]["score"]
    assert before["cross_system"] == 0.0 and after["cross_system"] == 0.5      # one variable in the cell, then two
    assert {k: v for k, v in after.items() if k not in ("total", "cross_system")} == \
           {k: v for k, v in before.items() if k not in ("total", "cross_system")}


def test_the_cap_treats_all_cascades_as_one_group(small_world):
    data, result, _ = small_world
    cap = {"top_n": 8, "max_share": 0.25}
    combined = cascades_for(data, result, rank_cap=cap)
    top = combined.findings[:8]
    assert sum(f["kind"] == "cascade" for f in top) <= 2
    assert [f["rank"] for f in combined.findings] == list(range(1, len(combined.findings) + 1))


def test_inputs_are_not_changed(small_world):
    data, result, _ = small_world
    findings, evidence = copy.deepcopy(result.findings), dict(result.evidence)
    arrays = {v: a.copy() for v, a in data.items()}
    cascades_for(data, result)
    assert result.findings == findings and result.evidence == evidence
    assert all("links" not in f for f in result.findings)
    assert all(np.array_equal(data[v], arrays[v]) for v in data)


# ------------------------------------------------------------------ which cells and pairs

def test_a_masked_cell_is_not_tested():
    data = world()
    result = scanned(data)
    mask = np.full((ROWS, COLUMNS), None, dtype=object)
    mask[LINK] = "ocean"
    combined = cascades_for(data, result, mask={"soil_moisture": mask})
    assert f"cascade.precipitation-soil_moisture.{cell_id(LINK)}" not in {f["id"] for f in combined.findings}
    assert combined.cascades == 4


def test_scattered_gaps_are_allowed_and_counted():
    def gaps(data):
        rng = np.random.default_rng(5)
        data["soil_moisture"][LINK][rng.choice(MONTHS, 10, replace=False)] = np.nan
        data["precipitation"][LINK][rng.choice(MONTHS, 8, replace=False)] = np.nan
    data = world(change=gaps)
    combined = cascades_for(data, scanned(data))
    c = {f["id"]: f for f in combined.findings}[f"cascade.precipitation-soil_moisture.{cell_id(LINK)}"]
    both = int((np.isfinite(data["soil_moisture"][LINK]) & np.isfinite(data["precipitation"][LINK])).sum())
    assert c["preprocessing"]["months_present"] == both < MONTHS
    assert c["preprocessing"]["coverage"] == pytest.approx(both / MONTHS)
    assert c["cascade"]["lag_months"] == 2
    assert c["method"]["parameters"]["months_paired"] < both


def test_too_few_months_in_common_skips_the_pair():
    def thin(data):
        data["soil_moisture"][LINK][:20] = np.nan             # each series passes the scan's 90% alone
        data["precipitation"][LINK][-20:] = np.nan            # but they share only 200 of 240 months
    data = world(change=thin)
    result = scanned(data)
    assert f"trend.precipitation.{cell_id(LINK)}" in {f["id"] for f in result.findings}
    combined = cascades_for(data, result)
    assert f"cascade.precipitation-soil_moisture.{cell_id(LINK)}" not in {f["id"] for f in combined.findings}
    assert {r["pair"]: r for r in combined.counts}["precipitation-soil_moisture"]["skipped_coverage"] == 1


def test_a_flat_series_is_skipped():
    def desert(data):
        data["precipitation"][REVERSED] = 0.0                 # no rain in any month; temperature still trends here
    data = world(change=desert)
    combined = cascades_for(data, scanned(data))
    counts = {r["pair"]: r for r in combined.counts}
    assert counts["temperature-precipitation"]["skipped_flat"] == 1
    assert counts["precipitation-vegetation"]["skipped_flat"] == 1
    assert f"cascade.vegetation-temperature.{cell_id(REVERSED)}" in {f["id"] for f in combined.findings}


def test_earthquakes_take_no_part():
    data = world()
    data["earthquakes"] = np.random.default_rng(6).poisson(2.0, size=(ROWS, COLUMNS, MONTHS)).astype(float)
    combined = cascades_for(data, scanned(data))
    assert len(combined.counts) == 6 and not any("earthquakes" in row["pair"] for row in combined.counts)
    assert not any("earthquakes" in f["id"] for f in of_kind(combined.findings, "cascade"))


def test_pairs_can_be_named(small_world):
    data, result, _ = small_world
    only = cascades_for(data, result, pairs=[("soil_moisture", "precipitation")])
    assert [row["pair"] for row in only.counts] == ["soil_moisture-precipitation"]
    # Naming the pair the other way round does not change who leads.
    assert {f["id"] for f in of_kind(only.findings, "cascade")} == {
        f"cascade.precipitation-soil_moisture.{cell_id(LINK)}", f"cascade.precipitation-soil_moisture.{cell_id(CHAIN)}"}


def test_nothing_to_test_gives_the_findings_back_in_the_same_order():
    rng = np.random.default_rng(7)
    data = {v: BASELINE[v] + np.stack([[make_series(MONTHS, ar1=0.2, rng=rng) for _ in range(COLUMNS)]
                                       for _ in range(ROWS)]) for v in ("temperature", "vegetation")}
    data["temperature"][0, 0] += 3.0 * np.arange(MONTHS) / 120.0
    result = scanned(data)
    combined = cascades_for(data, result)
    assert combined.cascades == 0
    assert [f["id"] for f in combined.findings] == [f["id"] for f in result.findings]
    assert all(f["links"] == [] for f in combined.findings)


def test_bad_input_is_refused(small_world):
    data, result, combined = small_world
    with pytest.raises(ValueError, match="already hold cascades"):
        add_cascades(data, combined.findings, combined.evidence, start_month=1, grid=GRID)
    with pytest.raises(ValueError, match="cells must be"):
        cascades_for(data, result, cells="some")
    with pytest.raises(ValueError, match="max_lag_months"):
        cascades_for(data, result, max_lag_months=0)
    with pytest.raises(ValueError, match="too few"):
        cascades_for(data, result, max_lag_months=60)
    with pytest.raises(ValueError, match="pair"):
        cascades_for(data, result, pairs=[("temperature", "ozone")])
    with pytest.raises(ValueError, match="twice"):
        cascades_for(data, result, pairs=[("temperature", "vegetation"), ("vegetation", "temperature")])


def test_a_shorter_maximum_lag_tests_fewer_lags(small_world):
    data, result, _ = small_world
    combined = cascades_for(data, result, max_lag_months=2)
    by_id = {f["id"]: f for f in combined.findings}
    c = by_id[f"cascade.precipitation-soil_moisture.{cell_id(LINK)}"]
    assert len(c["cascade"]["correlations_by_lag"]) == 3 and c["method"]["parameters"]["lags_tested"] == 5
    assert f"cascade.vegetation-temperature.{cell_id(REVERSED)}" not in by_id      # its lag is 3 months


# ------------------------------------------------------------------ contract

def test_the_output_has_the_shape_of_the_contract_fixture(small_world):
    _, _, combined = small_world
    fixture = json.loads((REPO / "contract" / "fixture" / "snapshot.json").read_text(encoding="utf-8"))
    expected = next(f for f in fixture["findings"] if f["kind"] == "cascade")
    c = {f["id"]: f for f in combined.findings}[f"cascade.vegetation-temperature.{cell_id(REVERSED)}"]
    assert set(c["cascade"]) == set(expected["cascade"])
    assert set(c["method"]) == set(expected["method"])
    assert set(expected["method"]["parameters"]) <= set(c["method"]["parameters"])
    assert set(c["links"][0]) == set(expected["links"][0])
    # What the pipeline adds is the only thing missing from the top level.
    assert set(expected) - set(c) == {"summary", "period", "standing"}
    assert set(expected["preprocessing"]) - set(c["preprocessing"]) == {"regridding", "aggregation", "anomaly", "missing_data_rule"}
    assert set(c["variables"][0]) == {"id", "units"}


def test_the_cascade_block_and_links_pass_the_contract_schema(small_world):
    _, _, combined = small_world
    schema = json.loads((REPO / "contract" / "schema.json").read_text(encoding="utf-8"))
    for name in ("cascade", "link"):
        validator = jsonschema.Draft202012Validator({"$ref": f"#/$defs/{name}", "$defs": schema["$defs"]})
        for f in of_kind(combined.findings, "cascade"):
            for item in ([f["cascade"]] if name == "cascade" else f["links"]):
                assert not list(validator.iter_errors(item))


def test_what_the_contract_checker_checks(small_world):
    _, _, combined = small_world
    for f in of_kind(combined.findings, "cascade"):
        block_ = f["cascade"]
        assert [v["id"] for v in f["variables"]] == [block_["leading_variable"], block_["following_variable"]]
        assert f["significant"] == (block_["p_adjusted"] < 0.05)
        assert abs(block_["correlations_by_lag"][block_["lag_months"]] - block_["correlation"]) < 1e-6
        assert block_["lag_months"] >= 1 and -1 <= block_["correlation"] <= 1
        assert abs(f["preprocessing"]["coverage"] - f["preprocessing"]["months_present"] / f["preprocessing"]["months_expected"]) < 1e-3
        assert f["id"] == f"cascade.{block_['leading_variable']}-{block_['following_variable']}.{f['cell']['id']}"


def test_no_nan_reaches_the_output(small_world):
    _, _, combined = small_world
    json.dumps(combined.findings, allow_nan=False)
    json.dumps(combined.counts, allow_nan=False)


def test_limitations_are_plain_sentences_that_never_claim_a_cause():
    sentences = limitations(6)
    assert len(sentences) >= 4 and all(isinstance(s, str) and s for s in sentences)
    assert "does not show that one caused" in sentences[0]
    assert "6 months" in " ".join(sentences)
