"""Checks change point finding: the statistics on many made-up series, then the findings on a small made-up grid.

Run from the ``python/`` folder:  python -m pytest vigil/analysis/tests/test_change_points.py -v
"""

import copy
import json
from pathlib import Path

import jsonschema
import numpy as np
import pytest

from vigil.analysis.cascades import add_cascades, adjust_candidates, anomalies
from vigil.analysis.change_points import (
    add_change_points,
    breakpoints,
    limitations,
    month_date,
    robustness,
    step_tests,
)
from vigil.analysis.contradictions import add_contradictions
from vigil.analysis.scan import scan_trends
from vigil.analysis.score import STEP_HALF_SIGMA, months_between, rank_findings, standardised_effect, step_sd
from vigil.analysis.synthetic import make_series

REPO = Path(__file__).resolve().parents[4]
ROWS, COLUMNS, MONTHS = 4, 5, 240
GRID = {"cell_degrees": 5.0, "rows": ROWS, "columns": COLUMNS, "lat_min": -10.0, "lon_min": -20.0}
START = "2001-01-01"
VARIABLES = ("temperature", "precipitation", "soil_moisture", "vegetation")
BASELINE = {"temperature": 285.0, "precipitation": 80.0, "soil_moisture": 25.0, "vegetation": 10.0}
LONG = 300          # months in the simulations of the statistics


# ------------------------------------------------------------------ many made-up series

def block(n, rng, months=LONG, **kwargs):
    """``n`` made-up series from ``make_series``, as rows."""
    return np.stack([make_series(months, rng=rng, **kwargs) for _ in range(n)])


def reported(tests, gain=10.0):
    """What would be listed if each series were tested alone: it passes Pettitt and a step clearly beats a line."""
    return (tests.p_value < 0.05) & (tests.bic_gain > gain)


def test_unchanged_autocorrelated_series_pass_at_no_more_than_the_stated_rate():
    """The false-positive gate: 3,000 series with memory and no step. About 5% or fewer may pass at the 5% level."""
    rng = np.random.default_rng(201)
    tests = step_tests(anomalies(block(3000, rng, ar1=0.5, seasonal_amplitude=2.0, baseline=5.0), 1))
    rate = np.mean(tests.p_value < 0.05)
    assert 0.02 < rate < 0.06
    # The rule that a step must clearly beat a line removes nearly all of those.
    assert np.mean(reported(tests)) < 0.005
    # Pettitt's own p-value, with every month counted as independent, calls a step in a large share of them.
    n = tests.months_present
    plain = 2 * np.exp(-6 * tests.pettitt_statistic**2 / (n**3 + n**2))
    assert np.mean(plain < 0.05) > 0.35


@pytest.mark.parametrize("ar1", [0.0, 0.3, 0.9])
def test_the_rate_holds_from_no_memory_to_a_great_deal(ar1):
    rng = np.random.default_rng(202)
    tests = step_tests(block(1500, rng, ar1=ar1))
    assert np.mean(tests.p_value < 0.05) < 0.065
    assert np.mean(reported(tests)) < 0.005


def test_pure_trends_pass_the_test_but_are_not_reported_as_steps():
    """A steady trend looks like a step to Pettitt. The comparison with a straight line is what keeps it out."""
    rng = np.random.default_rng(203)
    for per_decade in (0.4, 0.8):                       # a rise of 1 and of 2 sd over the 25 years
        tests = step_tests(block(1500, rng, ar1=0.4, trend_per_decade=per_decade))
        assert np.mean(tests.p_value < 0.05) > 0.6
        assert np.mean(reported(tests)) < 0.01
        assert np.mean(reported(tests, gain=0.0)) > 5 * np.mean(reported(tests))     # a bare "step fits better" is not enough


def test_a_planted_step_is_found_dated_and_sized():
    rng = np.random.default_rng(204)
    tests = step_tests(block(600, rng, ar1=0.4, step_month=110, step_size=2.0))
    found = reported(tests)
    assert found.mean() > 0.9
    assert np.mean(np.abs(tests.split - 110)[found] <= 3) > 0.8
    assert np.median((tests.level_after - tests.level_before)[found]) == pytest.approx(2.0, abs=0.1)
    assert np.all(tests.low <= tests.split) and np.all(tests.split <= tests.high)
    inside = (tests.low <= 110) & (110 <= tests.high)
    assert inside[found].mean() > 0.93


def test_a_step_down_has_a_lower_level_after():
    rng = np.random.default_rng(205)
    tests = step_tests(block(100, rng, ar1=0.3, step_month=200, step_size=-2.5))
    assert np.all((tests.level_after < tests.level_before)[reported(tests)])
    assert reported(tests).mean() > 0.9


def test_the_date_range_holds_the_true_date_about_as_often_as_it_says():
    """Coverage of the 95% range over 1,200 planted steps of mixed size, place and memory."""
    rng = np.random.default_rng(206)
    inside = found = 0
    for ar1, size, at in ((0.3, 1.5, 150), (0.3, 3.0, 60), (0.5, 2.0, 200), (0.6, 2.5, 100)):
        tests = step_tests(block(300, rng, ar1=ar1, step_month=at, step_size=size))
        keep = reported(tests)
        inside += ((tests.low <= at) & (at <= tests.high))[keep].sum()
        found += keep.sum()
    assert found > 900
    assert 0.93 < inside / found < 0.995


def test_the_range_widens_when_the_step_is_small_or_the_noise_is_large():
    rng = np.random.default_rng(207)

    def median_width(**kwargs):
        tests = step_tests(block(300, rng, ar1=0.3, step_month=150, **kwargs))
        return float(np.median(tests.high - tests.low))

    large, small = median_width(step_size=3.0), median_width(step_size=1.5)
    noisy = median_width(step_size=3.0, noise_sd=2.0)
    assert small > 2 * large
    assert noisy > 2 * large
    assert noisy == pytest.approx(small, rel=0.35)       # what matters is the step in units of the noise


def test_more_memory_in_the_noise_widens_the_range():
    rng = np.random.default_rng(208)
    widths = [float(np.median((t.high - t.low))) for t in
              (step_tests(block(300, rng, ar1=ar1, step_month=150, step_size=2.0)) for ar1 in (0.0, 0.6))]
    assert widths[1] > 2 * widths[0]


def test_a_step_cannot_sit_at_the_edge_of_the_record():
    rng = np.random.default_rng(209)
    tests = step_tests(block(200, rng, ar1=0.3, step_month=8, step_size=3.0), min_segment=24)
    assert tests.split.min() >= 24 and tests.split.max() <= LONG - 24
    assert tests.low.min() >= 24 and tests.high.max() <= LONG - 24
    shorter = step_tests(block(200, rng, ar1=0.3, step_month=8, step_size=3.0), min_segment=6)
    assert np.median(shorter.split) == 8


def test_missing_months_do_not_break_the_test_or_move_the_date():
    rng = np.random.default_rng(210)
    series = block(400, rng, ar1=0.4, step_month=100, step_size=-2.5, missing_fraction=0.07)
    tests = step_tests(series)
    for name in ("p_value", "level_before", "level_after", "bic_gain", "autocorrelation", "inflation"):
        assert np.isfinite(getattr(tests, name)).all()
    assert reported(tests).mean() > 0.9
    assert np.median(np.abs(tests.split - 100)) <= 1
    assert np.all(tests.months_present < LONG)
    # the date is always a month that has a value
    assert np.isfinite(series[np.arange(400), tests.split]).all()
    quiet = step_tests(block(1500, rng, ar1=0.5, missing_fraction=0.07))
    assert np.mean(quiet.p_value < 0.05) < 0.06


def test_a_series_that_does_not_vary_or_has_no_room_for_a_step_is_never_significant():
    rng = np.random.default_rng(211)
    series = block(3, rng, ar1=0.3, months=120)
    series[0] = 4.0                                      # flat
    series[1, 30:] = np.nan                              # 30 months of data: no month has 24 on both sides
    tests = step_tests(series)
    assert list(tests.testable) == [False, False, True]
    assert list(tests.p_value[:2]) == [1.0, 1.0]
    assert np.all(tests.bic_gain[:2] == -np.inf)


def test_many_trending_series_do_not_let_unchanged_ones_into_the_list():
    """A family like temperature: mostly trends, a few real steps. The candidates are charged for the whole family."""
    rng = np.random.default_rng(212)
    trends, steps, unchanged = 300, 20, 400
    listed = false = found = 0
    for _ in range(4):
        series = np.concatenate([
            np.stack([make_series(LONG, ar1=0.5, trend_per_decade=rng.uniform(0.4, 1.6), rng=rng) for _ in range(trends)]),
            np.stack([make_series(LONG, ar1=0.5, step_month=int(rng.integers(60, 240)),
                                  step_size=rng.choice([-1, 1]) * rng.uniform(2.0, 3.0), rng=rng) for _ in range(steps)]),
            block(unchanged, rng, ar1=0.5),
        ])
        real = np.zeros(series.shape[0], dtype=bool)
        real[trends : trends + steps] = True
        tests = step_tests(series)
        candidates = np.nonzero(tests.bic_gain > 10.0)[0]
        chosen = candidates[adjust_candidates(tests.p_value[candidates], series.shape[0]) < 0.05]
        listed += chosen.size
        false += (~real[chosen]).sum()
        found += real[chosen].sum()
    assert found >= 0.85 * 4 * steps
    assert false / listed < 0.05


def test_ruptures_finds_the_planted_step_at_both_penalties():
    rng = np.random.default_rng(213)
    series = make_series(LONG, ar1=0.3, step_month=130, step_size=3.0, rng=rng)
    tests = step_tests(series[None])
    split = int(tests.split[0])
    assert abs(split - 130) <= 2
    assert breakpoints(series, penalty=4 * np.log(LONG)) == [split]      # the same cost, so the same month
    score, nearest = robustness(series, split, float(tests.residual_variance[0]), float(tests.inflation[0]))
    assert nearest[1] == split                                           # the strict penalty: one breakpoint, the same month
    assert score >= 0.9


def test_robustness_is_zero_when_ruptures_finds_nothing_near_the_date():
    rng = np.random.default_rng(214)
    series = make_series(LONG, ar1=0.3, rng=rng)                         # no step at all
    tests = step_tests(series[None])
    score, nearest = robustness(series, int(tests.split[0]), float(tests.residual_variance[0]), float(tests.inflation[0]))
    assert score == 0.0 and nearest[1] is None
    assert breakpoints(series, penalty=1e9) == []
    assert breakpoints(np.full(30, np.nan), penalty=1.0) == []


def test_a_series_with_two_steps_scores_lower_robustness_than_one_clean_step():
    """One step is fitted, so a second one is a way for the single date to be wrong. The check picks that up."""
    rng = np.random.default_rng(216)

    def mean_score(series):
        tests = step_tests(series)
        keep = np.nonzero(reported(tests))[0]
        assert keep.size >= 30
        return np.mean([robustness(series[i], int(tests.split[i]), float(tests.residual_variance[i]),
                                   float(tests.inflation[i]))[0] for i in keep])

    one = block(150, rng, ar1=0.4, step_month=100, step_size=2.0)
    two = block(400, rng, ar1=0.4, step_month=100, step_size=2.0)
    two[:, 200:] -= 2.0                                                    # back down again at month 200
    assert mean_score(one) > 0.9
    assert mean_score(two) < mean_score(one) - 0.15


def test_ruptures_skips_missing_months_and_reports_real_month_numbers():
    rng = np.random.default_rng(215)
    series = make_series(LONG, ar1=0.2, step_month=90, step_size=4.0, rng=rng)
    series[80:90] = np.nan
    (found,) = breakpoints(series, penalty=4 * np.log(LONG))
    assert found == 90


def test_month_date_counts_months_from_the_start():
    from datetime import date
    assert month_date(date(2001, 1, 1), 0) == "2001-01-01"
    assert month_date(date(2001, 1, 1), 100) == "2009-05-01"
    assert month_date(date(2000, 11, 1), 2) == "2001-01-01"
    assert month_date(date(2001, 1, 1), 299) == "2025-12-01"


# ------------------------------------------------------------------ a small made-up grid

STEP = (1, 1)        # temperature steps up at month 100
TREND = (0, 3)       # temperature rises steadily: a trend, not a step
PAIR = (2, 3)        # soil moisture steps down at month 110 and vegetation at month 113
APART = (3, 0)       # precipitation steps at month 60 and vegetation at month 180: too far apart to link
PLANTED = {
    ("temperature", STEP): dict(step_month=100, step_size=2.5),
    ("temperature", TREND): dict(trend_per_decade=2.0),
    ("soil_moisture", PAIR): dict(step_month=110, step_size=-2.5),
    ("vegetation", PAIR): dict(step_month=113, step_size=-2.5),
    ("precipitation", APART): dict(step_month=60, step_size=2.5),
    ("vegetation", APART): dict(step_month=180, step_size=2.5),
}


def world(seed=1, change=None, variables=VARIABLES):
    """Four variables on the small grid: noise with a seasonal cycle everywhere, plus the cells named above."""
    rng = np.random.default_rng(seed)
    data = {}
    for variable in variables:
        data[variable] = np.stack([
            [make_series(MONTHS, ar1=0.3, seasonal_amplitude=3.0, baseline=BASELINE[variable], rng=rng,
                         **PLANTED.get((variable, (r, c)), {})) for c in range(COLUMNS)]
            for r in range(ROWS)])
    if change:
        change(data)
    return data


def scanned(data, **kwargs):
    return scan_trends(data, start_month=1, grid=GRID, rank_cap=kwargs.pop("rank_cap", None), **kwargs)


def steps_for(data, result, **kwargs):
    kwargs.setdefault("period_start", START)
    return add_change_points(data, result.findings, result.evidence, start_month=1, grid=GRID, **kwargs)


def of_kind(findings, kind):
    return [f for f in findings if f["kind"] == kind]


def cell_id(cell):
    return f"r{cell[0]:02d}c{cell[1]:03d}"


def finding_id(variable, cell):
    return f"change_point.{variable}.{cell_id(cell)}"


@pytest.fixture(scope="module")
def small_world():
    data = world()
    result = scanned(data)
    return data, result, steps_for(data, result)


def test_the_planted_steps_are_the_change_points_found(small_world):
    _, _, combined = small_world
    found = {f["id"] for f in of_kind(combined.findings, "change_point")}
    assert found == {finding_id("temperature", STEP), finding_id("soil_moisture", PAIR), finding_id("vegetation", PAIR),
                     finding_id("precipitation", APART), finding_id("vegetation", APART)}
    assert combined.change_points == 5


def test_a_change_point_has_its_date_its_range_and_its_levels(small_world):
    data, _, combined = small_world
    c = {f["id"]: f for f in combined.findings}[finding_id("temperature", STEP)]
    block_ = c["change_point"]
    assert c["significant"] is True
    assert c["variables"] == [{"id": "temperature", "units": "K"}]
    assert block_["units"] == "K"
    assert block_["date_low"] <= block_["date"] <= block_["date_high"]
    assert block_["date_low"] <= "2009-05-01" <= block_["date_high"]            # month 100 of a record from 2001-01
    assert months_between("2009-05-01", block_["date"]) in range(-3, 4)
    assert block_["shift"] == pytest.approx(2.5, abs=0.4)
    assert block_["shift"] == block_["level_after"] - block_["level_before"]
    assert block_["level_before"] < 0 < block_["level_after"]                   # levels of the anomalies, which average zero
    # The levels are the means of the anomalies either side of the date.
    anomaly = anomalies(data["temperature"][STEP][None], 1)[0]
    at = months_between(START, block_["date"])
    assert block_["level_before"] == pytest.approx(anomaly[:at].mean())
    assert block_["level_after"] == pytest.approx(anomaly[at:].mean())
    assert c["preprocessing"] == {"months_present": MONTHS, "months_expected": MONTHS, "coverage": 1.0}
    assert c["cell"]["id"] == "r01c001"


def test_the_method_block_says_what_was_done(small_world):
    _, _, combined = small_world
    c = {f["id"]: f for f in combined.findings}[finding_id("temperature", STEP)]
    parameters = c["method"]["parameters"]
    assert c["method"]["test"] == "pettitt_single_step"
    assert 0 <= parameters["p_value"] <= parameters["p_adjusted"] < 0.05
    assert parameters["min_segment_months"] == 24 and parameters["min_bic_gain"] == 10.0
    assert parameters["bic_gain"] > 10
    assert parameters["family"] == "temperature" and parameters["family_tests"] == ROWS * COLUMNS
    assert parameters["multiple_testing"] == "benjamini_hochberg" and parameters["threshold"] == 0.05
    assert parameters["date_range_level"] == 0.95
    assert 0 <= parameters["residual_autocorrelation"] < 1
    assert 4 < parameters["effective_months"] <= MONTHS
    assert parameters["noise_sd"] > 0
    assert parameters["robustness_date_second_penalty"] == c["change_point"]["date"]
    assert parameters["robustness_date_first_penalty"] != "none"
    assert all(isinstance(v, (str, int, float, bool)) for v in parameters.values())      # flat, as the contract asks


def test_a_steady_trend_is_a_trend_finding_and_not_a_change_point(small_world):
    _, result, combined = small_world
    assert f"trend.temperature.{cell_id(TREND)}" in {f["id"] for f in result.findings}
    assert finding_id("temperature", TREND) not in {f["id"] for f in combined.findings}


def test_flat_cells_are_tested_and_not_significant(small_world):
    _, _, combined = small_world
    planted = {(variable, cell_id(cell)) for variable, cell in PLANTED}
    for f in of_kind(combined.findings, "change_point"):
        assert (f["variables"][0]["id"], f["cell"]["id"]) in planted
    for row in combined.counts:
        assert row["tests_run"] == ROWS * COLUMNS
        assert row["significant"] <= row["candidates"] <= row["tests_run"]
    assert sum(row["significant"] for row in combined.counts) == combined.change_points


def test_counts_name_the_commonest_date(small_world):
    _, _, combined = small_world
    counts = {row["variable"]: row for row in combined.counts}
    assert [row["variable"] for row in combined.counts] == list(VARIABLES)
    assert counts["vegetation"]["significant"] == 2 and counts["vegetation"]["share_on_commonest_date"] == 0.5
    assert counts["temperature"]["share_on_commonest_date"] == 1.0
    one = {f["id"]: f for f in combined.findings}[finding_id("temperature", STEP)]
    assert counts["temperature"]["commonest_date"] == one["change_point"]["date"]


# ------------------------------------------------------------------ links

def test_a_change_point_and_the_trend_in_the_same_series_link_to_each_other(small_world):
    """A step also passes the trend test, so the cell holds both findings. They point at each other."""
    _, _, combined = small_world
    by_id = {f["id"]: f for f in combined.findings}
    step, trend = by_id[finding_id("temperature", STEP)], by_id[f"trend.temperature.{cell_id(STEP)}"]
    assert step["links"] == [{"finding_id": trend["id"], "kind": "trend"}]
    assert trend["links"] == [{"finding_id": step["id"], "kind": "change_point"}]


def test_two_change_points_close_in_time_in_one_cell_link_with_the_gap(small_world):
    _, _, combined = small_world
    by_id = {f["id"]: f for f in combined.findings}
    soil, plants = by_id[finding_id("soil_moisture", PAIR)], by_id[finding_id("vegetation", PAIR)]
    gap = abs(months_between(soil["change_point"]["date"], plants["change_point"]["date"]))
    assert gap <= 8                                                           # planted 3 months apart
    assert {"finding_id": plants["id"], "kind": "change_point", "lag_months": gap} in soil["links"]
    assert {"finding_id": soil["id"], "kind": "change_point", "lag_months": gap} in plants["links"]


def test_change_points_years_apart_do_not_link_unless_asked(small_world):
    data, result, combined = small_world
    by_id = {f["id"]: f for f in combined.findings}
    rain, plants = by_id[finding_id("precipitation", APART)], by_id[finding_id("vegetation", APART)]
    assert not any(link["finding_id"] == plants["id"] for link in rain["links"])
    wide = {f["id"]: f for f in steps_for(data, result, link_within_months=240).findings}
    assert any(link["finding_id"] == plants["id"] and link["lag_months"] > 100 for link in wide[rain["id"]]["links"])


def test_every_link_resolves_and_is_answered(small_world):
    _, _, combined = small_world
    by_id = {f["id"]: f for f in combined.findings}
    for f in combined.findings:
        assert isinstance(f["links"], list)
        for link in f["links"]:
            target = by_id[link["finding_id"]]
            assert link["kind"] == target["kind"]
            assert any(back["finding_id"] == f["id"] for back in target["links"])


def test_the_whole_chain_runs_and_keeps_earlier_links():
    """scan, contradictions, cascades, change points: each step adds to the list the one before returned."""
    def oppose(data):
        data["temperature"][TREND[0], TREND[1] + 1] -= 2.0 * np.arange(MONTHS) / 120.0     # the cell to the east cools
    data = world(change=oppose)
    result = scanned(data)
    pairs = add_contradictions(result.findings, result.evidence, grid=GRID, threshold=0.05)
    links = add_cascades(data, pairs.findings, pairs.evidence, start_month=1, grid=GRID)
    combined = add_change_points(data, links.findings, links.evidence, start_month=1, period_start=START, grid=GRID)
    assert pairs.contradictions >= 1 and combined.change_points >= 5
    before = {f["id"]: f for f in links.findings}
    for f in combined.findings:
        if f["kind"] != "change_point":
            assert f["links"][: len(before[f["id"]]["links"])] == before[f["id"]]["links"]
    assert len(combined.findings) == len(links.findings) + combined.change_points
    json.dumps(combined.findings, allow_nan=False)


# ------------------------------------------------------------------ ranking and evidence

def test_the_list_is_ranked_together_and_scored_with_the_usual_parts(small_world):
    _, result, combined = small_world
    assert len(combined.findings) == len(result.findings) + combined.change_points
    assert [f["rank"] for f in combined.findings] == list(range(1, len(combined.findings) + 1))
    for f in combined.findings:
        assert set(f["score"]) == {"total", "effect_size", "certainty", "coverage", "robustness", "cross_system"}
    again = rank_findings(combined.findings, combined.evidence, diversify=False)
    assert [f["id"] for f in again] == [f["id"] for f in combined.findings]


def test_a_change_point_is_scored_from_its_shift_its_noise_and_its_date_range(small_world):
    data, _, combined = small_world
    c = {f["id"]: f for f in combined.findings}[finding_id("soil_moisture", PAIR)]
    block_, evidence = c["change_point"], combined.evidence[c["id"]]
    anomaly = anomalies(data["soil_moisture"][PAIR][None], 1)[0]
    assert evidence.noise_sd == step_sd(anomaly, months_between(START, block_["date"]))
    assert evidence.noise_sd == c["method"]["parameters"]["noise_sd"]
    assert c["score"]["effect_size"] == pytest.approx(
        standardised_effect(block_["shift"] / evidence.noise_sd, STEP_HALF_SIGMA), abs=1e-4)
    width = months_between(block_["date_low"], block_["date_high"])
    assert c["score"]["certainty"] == pytest.approx(1 / (1 + width / 12), abs=1e-4)
    assert c["score"]["robustness"] == pytest.approx(evidence.robustness, abs=1e-4) == c["method"]["parameters"]["robustness"]
    assert evidence.robustness > 0.8                                           # a 2.5 sd step survives both penalties


def test_change_points_in_two_variables_give_each_other_cross_system_credit(small_world):
    _, _, combined = small_world
    by_id = {f["id"]: f for f in combined.findings}
    assert by_id[finding_id("soil_moisture", PAIR)]["score"]["cross_system"] == 0.5     # two variables changed in the cell
    assert by_id[finding_id("vegetation", PAIR)]["score"]["cross_system"] == 0.5


def test_the_cap_counts_a_change_point_in_its_variables_group(small_world):
    data, result, _ = small_world
    combined = steps_for(data, result, rank_cap={"top_n": 8, "max_share": 0.25})
    top = combined.findings[:8]
    for variable in VARIABLES:
        assert sum(f["variables"][0]["id"] == variable for f in top) <= 2
    assert [f["rank"] for f in combined.findings] == list(range(1, len(combined.findings) + 1))


def test_inputs_are_not_changed(small_world):
    data, result, _ = small_world
    findings, evidence = copy.deepcopy(result.findings), dict(result.evidence)
    arrays = {v: a.copy() for v, a in data.items()}
    steps_for(data, result)
    assert result.findings == findings and result.evidence == evidence
    assert all("links" not in f for f in result.findings)
    assert all(np.array_equal(data[v], arrays[v]) for v in data)


# ------------------------------------------------------------------ which cells and variables

def test_cells_the_scan_skipped_stay_skipped():
    def thin(data):
        data["temperature"][STEP][::4] = np.nan                # 75% of months: below the 90% rule
    data = world(change=thin)
    mask = np.full((ROWS, COLUMNS), None, dtype=object)
    mask[PAIR] = "ocean"
    result = scanned(data, mask=mask)
    combined = steps_for(data, result, mask=mask)
    found = {f["id"] for f in of_kind(combined.findings, "change_point")}
    assert found == {finding_id("precipitation", APART), finding_id("vegetation", APART)}
    counts = {row["variable"]: row for row in combined.counts}
    assert counts["temperature"]["tests_run"] == ROWS * COLUMNS - 2          # one masked, one too thin
    assert counts["vegetation"]["tests_run"] == ROWS * COLUMNS - 1


def test_scattered_gaps_are_allowed_and_counted():
    def gaps(data):
        data["temperature"][STEP][np.random.default_rng(5).choice(MONTHS, 12, replace=False)] = np.nan
    data = world(change=gaps)
    combined = steps_for(data, scanned(data))
    c = {f["id"]: f for f in combined.findings}[finding_id("temperature", STEP)]
    assert c["preprocessing"]["months_present"] == MONTHS - 12
    assert c["preprocessing"]["coverage"] == pytest.approx((MONTHS - 12) / MONTHS)
    assert c["change_point"]["date_low"] <= "2009-05-01" <= c["change_point"]["date_high"]


def test_a_flat_series_is_tested_and_never_a_change_point():
    def desert(data):
        data["precipitation"][STEP] = 0.0
    data = world(change=desert)
    combined = steps_for(data, scanned(data))
    counts = {row["variable"]: row for row in combined.counts}
    assert counts["precipitation"]["flat"] == 1 and counts["precipitation"]["tests_run"] == ROWS * COLUMNS
    assert finding_id("precipitation", STEP) not in {f["id"] for f in combined.findings}


def test_earthquakes_take_no_part_unless_let_in():
    data = world()
    counts = np.random.default_rng(6).poisson(8.0, size=(ROWS, COLUMNS, MONTHS)).astype(float)
    counts[0, 0, 120:] += 12                                  # an aftershock sequence that never ends
    data["earthquakes"] = counts
    result = scanned(data)
    combined = steps_for(data, result)
    assert [row["variable"] for row in combined.counts] == list(VARIABLES)
    assert not any("earthquakes" in f["id"] for f in of_kind(combined.findings, "change_point"))
    let_in = steps_for(data, result, exclude_variables=())
    assert "change_point.earthquakes.r00c000" in {f["id"] for f in let_in.findings}


def test_a_stricter_or_looser_rule_for_step_against_trend():
    data = world()
    result = scanned(data)
    assert steps_for(data, result, min_bic_gain=1e6).change_points == 0
    assert steps_for(data, result, min_bic_gain=-1e6).change_points >= 6       # now the steady trend is listed too


def test_nothing_found_gives_the_findings_back_in_the_same_order():
    data = world(variables=("temperature",))
    data["temperature"][STEP] = data["temperature"][0, 0]     # no step left anywhere
    result = scanned(data)
    assert result.findings
    combined = steps_for(data, result)
    assert combined.change_points == 0
    assert [f["id"] for f in combined.findings] == [f["id"] for f in result.findings]
    assert all(f["links"] == [] for f in combined.findings)
    assert combined.counts[0]["commonest_date"] is None


def test_bad_input_is_refused(small_world):
    data, result, combined = small_world
    with pytest.raises(ValueError, match="already hold change points"):
        add_change_points(data, combined.findings, combined.evidence, start_month=1, period_start=START, grid=GRID)
    with pytest.raises(ValueError, match="start_month"):
        steps_for(data, result, period_start="2001-02-01")
    with pytest.raises(ValueError, match="period_start"):
        steps_for(data, result, period_start="January 2001")
    with pytest.raises(ValueError, match="too few"):
        steps_for(data, result, min_segment_months=100)
    with pytest.raises(ValueError, match="min_segment_months"):
        steps_for(data, result, min_segment_months=1)
    with pytest.raises(ValueError, match="link_within_months"):
        steps_for(data, result, link_within_months=-1)


def test_dates_follow_the_period_start():
    data = world(variables=("temperature",))
    result = scan_trends(data, start_month=7, grid=GRID)
    combined = add_change_points(data, result.findings, result.evidence, start_month=7, period_start="1998-07-01", grid=GRID)
    c = {f["id"]: f for f in combined.findings}[finding_id("temperature", STEP)]
    assert c["change_point"]["date_low"] <= "2006-11-01" <= c["change_point"]["date_high"]      # 100 months after 1998-07


# ------------------------------------------------------------------ contract

def test_the_output_has_the_shape_of_the_contract_fixture(small_world):
    _, _, combined = small_world
    fixture = json.loads((REPO / "contract" / "fixture" / "snapshot.json").read_text(encoding="utf-8"))
    expected = next(f for f in fixture["findings"] if f["kind"] == "change_point")
    c = {f["id"]: f for f in combined.findings}[finding_id("vegetation", PAIR)]
    assert set(c["change_point"]) == set(expected["change_point"])
    assert set(c["method"]) == set(expected["method"])
    pair_link = next(link for link in c["links"] if link["kind"] == "change_point")
    assert set(pair_link) == set(expected["links"][0])
    # What the pipeline adds is the only thing missing from the top level.
    assert set(expected) - set(c) == {"summary", "period", "standing"}
    assert set(expected["preprocessing"]) - set(c["preprocessing"]) == {"regridding", "aggregation", "anomaly", "missing_data_rule"}
    assert set(c["variables"][0]) == {"id", "units"}


def test_the_change_point_block_and_links_pass_the_contract_schema(small_world):
    _, _, combined = small_world
    schema = json.loads((REPO / "contract" / "schema.json").read_text(encoding="utf-8"))
    for name in ("change_point", "link"):
        validator = jsonschema.Draft202012Validator({"$ref": f"#/$defs/{name}", "$defs": schema["$defs"]})
        for f in of_kind(combined.findings, "change_point"):
            for item in ([f["change_point"]] if name == "change_point" else f["links"]):
                assert not list(validator.iter_errors(item))


def test_what_the_contract_checker_checks(small_world):
    _, _, combined = small_world
    for f in of_kind(combined.findings, "change_point"):
        c = f["change_point"]
        assert c["date_low"] <= c["date"] <= c["date_high"]
        assert START <= c["date"] <= "2020-12-01"
        assert abs(c["shift"] - (c["level_after"] - c["level_before"])) < 1e-6 + 1e-4 * abs(c["shift"])
        assert abs(f["preprocessing"]["coverage"] - f["preprocessing"]["months_present"] / f["preprocessing"]["months_expected"]) < 1e-3
        assert f["id"] == f"change_point.{f['variables'][0]['id']}.{f['cell']['id']}"


def test_no_nan_reaches_the_output(small_world):
    _, _, combined = small_world
    json.dumps(combined.findings, allow_nan=False)
    json.dumps(combined.counts, allow_nan=False)


def test_limitations_are_plain_sentences_that_claim_no_cause():
    sentences = limitations()
    assert len(sentences) >= 4 and all(isinstance(s, str) and s for s in sentences)
    assert "not proof of when" in sentences[0]
