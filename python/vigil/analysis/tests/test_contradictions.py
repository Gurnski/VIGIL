"""Checks contradiction finding on a small made-up grid.

Run from the ``python/`` folder:  python -m pytest vigil/analysis/tests/test_contradictions.py -v
"""

import copy
import json
from pathlib import Path

import numpy as np
import pytest

from vigil.analysis.contradictions import add_contradictions, cell_centre_km_apart
from vigil.analysis.scan import cell_object, scan_trends
from vigil.analysis.score import rank_findings
from vigil.analysis.synthetic import make_series

REPO = Path(__file__).resolve().parents[4]
ROWS, COLUMNS, MONTHS = 6, 8, 240
GRID = {"cell_degrees": 5.0, "rows": ROWS, "columns": COLUMNS, "lat_min": -15.0, "lon_min": -20.0}   # does not wrap
WRAPPING = {"cell_degrees": 45.0, "rows": 4, "columns": 8, "lat_min": -90.0, "lon_min": -180.0}        # 8 x 45 = 360


def field(trends, shape=(ROWS, COLUMNS), seed=0):
    """Noisy flat series everywhere, with a trend per decade in the cells named in ``trends``."""
    rng = np.random.default_rng(seed)
    grid = np.empty(shape + (MONTHS,))
    for r in range(shape[0]):
        for c in range(shape[1]):
            grid[r, c] = make_series(MONTHS, trend_per_decade=trends.get((r, c), 0.0), noise_sd=1.0,
                                     ar1=0.2, seasonal_amplitude=3.0, baseline=10.0, rng=rng)
    return grid


def scanned(data, grid=GRID, **kwargs):
    shape = next(iter(data.values())).shape
    return scan_trends(data, start_month=1, grid={**grid, "rows": shape[0], "columns": shape[1]},
                       rank_cap=kwargs.pop("rank_cap", None), **kwargs)


def combine(result, grid=GRID, **kwargs):
    return add_contradictions(result.findings, result.evidence, grid=grid, threshold=0.05, **kwargs)


def of_kind(findings, kind):
    return [f for f in findings if f["kind"] == kind]


@pytest.fixture(scope="module")
def east_west_pair():
    """Temperature: warming at (2, 3), cooling in the cell to its east (2, 4). The rest is flat."""
    result = scanned({"temperature": field({(2, 3): 2.0, (2, 4): -2.0})})
    return result, combine(result)


# ------------------------------------------------------------------ the pair

def test_neighbouring_opposite_trends_make_one_contradiction(east_west_pair):
    result, combined = east_west_pair
    assert combined.contradictions == 1
    (c,) = of_kind(combined.findings, "contradiction")
    assert c["id"] == "contradiction.temperature.r02c003-r02c004"
    assert c["cell"]["id"] == "r02c003" and c["contradiction"]["paired_cell"]["id"] == "r02c004"
    assert c["significant"] is True
    block = c["contradiction"]
    assert block["variable"] == "temperature"
    assert block["slope_per_decade"] > 0 > block["paired_slope_per_decade"]
    assert block["units"] == "K per decade"
    assert block["distance_km"] == pytest.approx(cell_centre_km_apart(c["cell"], block["paired_cell"]), abs=0.1)
    assert 400 < block["distance_km"] < 560             # 5 degrees of longitude at about 2.5 degrees north
    assert block["confounders"] and all(isinstance(t, str) for t in block["confounders"])
    assert c["method"] == {"test": "opposite_significant_trends",
                           "parameters": {"neighbourhood": "cells sharing an edge", "threshold": 0.05}}


def test_the_slopes_are_those_of_the_trend_findings(east_west_pair):
    result, combined = east_west_pair
    (c,) = of_kind(combined.findings, "contradiction")
    trends = {f["cell"]["id"]: f for f in of_kind(result.findings, "trend")}
    assert c["contradiction"]["slope_per_decade"] == trends["r02c003"]["result"]["slope_per_decade"]
    assert c["contradiction"]["paired_slope_per_decade"] == trends["r02c004"]["result"]["slope_per_decade"]


def test_links_are_written_on_both_ends(east_west_pair):
    _, combined = east_west_pair
    by_id = {f["id"]: f for f in combined.findings}
    c = by_id["contradiction.temperature.r02c003-r02c004"]
    assert c["links"] == [
        {"finding_id": "trend.temperature.r02c003", "kind": "trend", "cell_id": "r02c004"},
        {"finding_id": "trend.temperature.r02c004", "kind": "trend", "cell_id": "r02c003"},
    ]
    assert by_id["trend.temperature.r02c003"]["links"] == [
        {"finding_id": c["id"], "kind": "contradiction", "cell_id": "r02c004"}]
    assert by_id["trend.temperature.r02c004"]["links"] == [
        {"finding_id": c["id"], "kind": "contradiction", "cell_id": "r02c003"}]
    # Every trend carries a links list, so the pipeline must not overwrite it with [].
    assert all(isinstance(f["links"], list) for f in of_kind(combined.findings, "trend"))


def test_inputs_are_not_changed(east_west_pair):
    result, _ = east_west_pair
    assert all("links" not in f for f in result.findings)


def test_the_list_is_ranked_together_and_scored_with_the_usual_parts(east_west_pair):
    _, combined = east_west_pair
    assert [f["rank"] for f in combined.findings] == list(range(1, len(combined.findings) + 1))
    for f in combined.findings:
        assert set(f["score"]) == {"total", "effect_size", "certainty", "coverage", "robustness", "cross_system"}
    again = rank_findings(combined.findings, combined.evidence, diversify=False)
    assert [f["id"] for f in again] == [f["id"] for f in combined.findings]
    # Adding the contradiction does not change a trend's own score.
    result, _ = east_west_pair
    before = {f["id"]: f["score"] for f in result.findings}
    assert all(f["score"] == before[f["id"]] for f in of_kind(combined.findings, "trend"))


def test_the_contradiction_is_as_uncertain_as_its_weaker_trend(east_west_pair):
    result, combined = east_west_pair
    (c,) = of_kind(combined.findings, "contradiction")
    trends = {f["id"]: f for f in of_kind(combined.findings, "trend")}
    pair = [trends["trend.temperature.r02c003"], trends["trend.temperature.r02c004"]]
    assert c["score"]["certainty"] == pytest.approx(min(t["score"]["certainty"] for t in pair), abs=1e-3)
    assert c["score"]["robustness"] == pytest.approx(min(t["score"]["robustness"] for t in pair), abs=1e-3)
    assert c["preprocessing"]["coverage"] == min(t["preprocessing"]["coverage"] for t in pair)
    ev = combined.evidence[c["id"]]
    assert ev.noise_sd == max(result.evidence[t["id"]].noise_sd for t in pair)


# ------------------------------------------------------------------ which pairs count

def test_neighbours_with_the_same_sign_are_not_a_contradiction():
    result = scanned({"temperature": field({(2, 3): 2.0, (2, 4): 2.0})})
    assert combine(result).contradictions == 0


def test_cells_that_are_not_neighbours_are_not_a_contradiction():
    result = scanned({"temperature": field({(2, 1): 2.0, (2, 4): -2.0})})
    assert combine(result).contradictions == 0


def test_a_flat_neighbour_is_not_a_contradiction():
    result = scanned({"temperature": field({(2, 3): 2.0})})
    assert len(of_kind(result.findings, "trend")) == 1
    assert combine(result).contradictions == 0


def test_opposite_trends_in_different_variables_are_not_a_contradiction():
    data = {"temperature": field({(2, 3): 2.0}), "precipitation": field({(2, 4): -2.0}, seed=1)}
    result = scanned(data)
    assert len(of_kind(result.findings, "trend")) == 2
    assert combine(result).contradictions == 0


def test_a_corner_neighbour_counts_only_with_diagonals():
    result = scanned({"temperature": field({(2, 3): 2.0, (3, 4): -2.0})})
    assert combine(result).contradictions == 0
    diagonal = combine(result, include_diagonals=True)
    assert diagonal.contradictions == 1
    (c,) = of_kind(diagonal.findings, "contradiction")
    assert c["method"]["parameters"]["neighbourhood"] == "cells sharing an edge or a corner"
    assert c["id"] == "contradiction.temperature.r02c003-r03c004"


def test_a_south_north_pair_is_found_once_with_the_lower_id_first():
    result = scanned({"temperature": field({(3, 2): -2.0, (2, 2): 2.0})})
    combined = combine(result)
    assert combined.contradictions == 1
    assert of_kind(combined.findings, "contradiction")[0]["id"] == "contradiction.temperature.r02c002-r03c002"


def test_columns_wrap_round_the_dateline_when_the_grid_is_a_full_circle():
    shape = (4, 8)
    data = {"temperature": field({(1, 0): 2.0, (1, 7): -2.0}, shape=shape)}
    result = scanned(data, grid=WRAPPING)
    combined = combine(result, grid=WRAPPING)
    assert combined.contradictions == 1
    assert of_kind(combined.findings, "contradiction")[0]["id"] == "contradiction.temperature.r01c000-r01c007"
    # The same cells on a grid that does not go all the way round are not neighbours.
    narrow = {**WRAPPING, "cell_degrees": 40.0}
    assert combine(result, grid=narrow).contradictions == 0


def test_earthquakes_are_not_compared():
    rng = np.random.default_rng(3)
    rising = rng.poisson(0.5 + 3.5 * np.arange(MONTHS) / MONTHS)
    falling = rng.poisson(4.0 - 3.5 * np.arange(MONTHS) / MONTHS)
    counts = rng.poisson(1.0, size=(ROWS, COLUMNS, MONTHS)).astype(float)
    counts[2, 3], counts[2, 4] = rising, falling
    result = scanned({"earthquakes": counts})
    assert {f["cell"]["id"] for f in result.findings} >= {"r02c003", "r02c004"}
    assert combine(result).contradictions == 0
    assert combine(result, exclude_variables=()).contradictions == 1


def test_one_cell_can_be_in_two_contradictions():
    result = scanned({"temperature": field({(2, 3): 2.0, (2, 2): -2.0, (2, 4): -2.0})})
    combined = combine(result)
    assert combined.contradictions == 2
    by_id = {f["id"]: f for f in combined.findings}
    assert [l["finding_id"] for l in by_id["trend.temperature.r02c003"]["links"]] == [
        "contradiction.temperature.r02c002-r02c003", "contradiction.temperature.r02c003-r02c004"]


def test_the_cap_still_applies_to_the_combined_list():
    result = scanned({"temperature": field({(2, 3): 2.0, (2, 4): -2.0})}, rank_cap={"top_n": 4, "max_share": 0.25})
    combined = combine(result, rank_cap={"top_n": 4, "max_share": 0.25})
    assert [f["rank"] for f in combined.findings] == list(range(1, len(combined.findings) + 1))
    assert combined.contradictions == 1


def test_nothing_to_compare_gives_the_trends_back_unchanged():
    result = scanned({"temperature": field({})})
    combined = combine(result)
    assert combined.contradictions == 0
    assert [f["id"] for f in combined.findings] == [f["id"] for f in result.findings]


# ------------------------------------------------------------------ contract

def test_the_output_has_the_shape_of_the_contract_fixture(east_west_pair):
    _, combined = east_west_pair
    fixture = json.loads((REPO / "contract" / "fixture" / "snapshot.json").read_text(encoding="utf-8"))
    expected = next(f for f in fixture["findings"] if f["kind"] == "contradiction")
    (c,) = of_kind(combined.findings, "contradiction")
    assert set(c["contradiction"]) == set(expected["contradiction"])
    assert set(c["method"]) == set(expected["method"])
    assert set(c["method"]["parameters"]) == set(expected["method"]["parameters"])
    assert set(c["links"][0]) == set(expected["links"][0])
    # What the pipeline adds is the only thing missing from the top level.
    assert set(expected) - set(c) == {"summary", "period", "standing"}
    assert set(expected["preprocessing"]) - set(c["preprocessing"]) == {"regridding", "aggregation", "anomaly", "missing_data_rule"}


def test_no_nan_reaches_the_output(east_west_pair):
    _, combined = east_west_pair
    json.dumps(combined.findings, allow_nan=False)


def test_distance_between_cell_centres():
    a, b = cell_object(0, 0, GRID), cell_object(0, 1, GRID)
    assert cell_centre_km_apart(a, a) == 0
    assert cell_centre_km_apart(a, b) == pytest.approx(5 * 111.19 * np.cos(np.radians(-12.5)), rel=0.01)
