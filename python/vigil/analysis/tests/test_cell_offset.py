"""Checks that scanning a region gives every cell the id it has on the full grid.

Run from the ``python/`` folder:  python -m pytest vigil/analysis/tests/test_cell_offset.py -v

``POST /analyse`` re-runs the scan on a region and a period. Cell ids in the contract are positions on
the full grid, so a cell must be ``r05c006`` whether it is scanned with the rest of the world or alone.
These tests plant one finding of each kind and scan the same data twice, whole and by region.
"""

import numpy as np
import pytest

from vigil.analysis.cascades import add_cascades
from vigil.analysis.change_points import add_change_points
from vigil.analysis.contradictions import add_contradictions
from vigil.analysis.scan import cell_object, check_cell_offset, scan_trends
from vigil.analysis.synthetic import make_series

ROWS, COLUMNS, MONTHS = 8, 10, 240
FULL = {"cell_degrees": 5.0, "rows": ROWS, "columns": COLUMNS, "lat_min": -20.0, "lon_min": -25.0}
VARIABLES = ("temperature", "precipitation", "soil_moisture", "vegetation")

# The region: rows 4 to 7 and columns 5 to 9 of the full grid.
OFFSET = (4, 5)
REGION_ROWS, REGION_COLUMNS = 4, 5
REGION = {"cell_degrees": 5.0, "rows": REGION_ROWS, "columns": REGION_COLUMNS,
          "lat_min": FULL["lat_min"] + OFFSET[0] * 5.0, "lon_min": FULL["lon_min"] + OFFSET[1] * 5.0}

TREND = (5, 6)            # temperature warms here ...
OPPOSITE = (5, 7)         # ... and cools next door: a contradiction
CASCADE = (6, 8)          # soil moisture follows precipitation by 2 months
STEP = (7, 9)             # vegetation steps up
OUTSIDE = (0, 0)          # a trend in a cell the region does not include


def planted():
    rng = np.random.default_rng(11)
    data = {v: np.stack([[make_series(MONTHS, noise_sd=1.0, ar1=0.2, seasonal_amplitude=2.0, baseline=10.0, rng=rng)
                          for _ in range(COLUMNS)] for _ in range(ROWS)]) for v in VARIABLES}
    t = np.arange(MONTHS)
    data["temperature"][TREND] += 2.5 * t / 120
    data["temperature"][OPPOSITE] -= 2.5 * t / 120
    data["temperature"][OUTSIDE] += 2.5 * t / 120
    lead = make_series(MONTHS + 2, noise_sd=1.0, ar1=0.5, rng=rng)
    data["precipitation"][CASCADE] = lead[2:]
    data["soil_moisture"][CASCADE] = lead[:-2] + 0.2 * rng.standard_normal(MONTHS)
    data["vegetation"][STEP] += np.where(t >= 120, 4.0, 0.0)
    return data


def chain(data, grid, offset):
    settings = dict(start_month=1, grid=grid, min_coverage=0.9, threshold=0.05, ci_level=0.95, cell_offset=offset)
    scan = scan_trends(data, **settings)
    step2 = add_contradictions(scan.findings, scan.evidence, grid=grid, threshold=0.05, cell_offset=offset)
    step3 = add_cascades(data, step2.findings, step2.evidence, max_lag_months=6, cells="all", **settings)
    step4 = add_change_points(data, step3.findings, step3.evidence, period_start="2001-01-01", **settings)
    return scan, step4


def region_of(data):
    return {v: a[OFFSET[0]:, OFFSET[1]:, :].copy() for v, a in data.items()}


def at(findings, kind, cell):
    return [f for f in findings if f["kind"] == kind and f["cell"]["id"] == cell_object(*cell, FULL)["id"]]


@pytest.fixture(scope="module")
def both():
    data = planted()
    whole = chain(data, FULL, (0, 0))
    part = chain(region_of(data), REGION, OFFSET)
    return data, whole, part


# ------------------------------------------------------------------ the same cells get the same ids

@pytest.mark.parametrize("kind, cell", [("trend", TREND), ("trend", OPPOSITE), ("contradiction", TREND),
                                        ("cascade", CASCADE), ("change_point", STEP)])
def test_each_kind_of_finding_has_the_same_cell_in_a_region_as_on_the_whole_grid(both, kind, cell):
    _, (_, whole), (_, part) = both
    found_whole, found_part = at(whole.findings, kind, cell), at(part.findings, kind, cell)
    assert found_whole, f"the full scan did not find the planted {kind} at {cell}"
    assert found_part, f"the region scan did not find the planted {kind} at {cell}"
    assert {f["id"] for f in found_whole} == {f["id"] for f in found_part}
    assert [f["cell"] for f in found_whole] == [f["cell"] for f in found_part] == [cell_object(*cell, FULL)] * len(found_part)


def test_ids_are_the_full_grids_and_bounds_follow_the_cell(both):
    _, _, (_, part) = both
    for f in part.findings:
        cells = [f["cell"]] + ([f["contradiction"]["paired_cell"]] if f["kind"] == "contradiction" else [])
        for cell in cells:
            assert cell == cell_object(cell["row"], cell["column"], FULL)
            assert cell["id"] == f"r{cell['row']:02d}c{cell['column']:03d}"
            assert OFFSET[0] <= cell["row"] < ROWS and OFFSET[1] <= cell["column"] < COLUMNS


def test_a_cell_outside_the_region_is_not_in_a_region_scan(both):
    _, (_, whole), (_, part) = both
    assert at(whole.findings, "trend", OUTSIDE)
    assert not at(part.findings, "trend", OUTSIDE)


def test_the_contradiction_and_the_links_use_the_full_grid_ids(both):
    _, _, (_, part) = both
    by_id = {f["id"]: f for f in part.findings}
    c = by_id["contradiction.temperature.r05c006-r05c007"]
    assert {link["finding_id"] for link in c["links"]} == {"trend.temperature.r05c006", "trend.temperature.r05c007"}
    assert all(any(back["finding_id"] == c["id"] for back in by_id[link["finding_id"]]["links"]) for link in c["links"])


def test_a_trend_in_a_cascade_cell_is_linked_by_its_full_grid_id(both):
    _, _, (_, part) = both
    for f in part.findings:
        for link in f["links"]:
            assert link["finding_id"] in {g["id"] for g in part.findings}


# ------------------------------------------------------------------ the region's own numbers

def test_the_layers_and_counts_describe_the_region(both):
    _, _, (scan, _) = both
    layer = scan.grids[0]
    assert (layer["rows"], layer["columns"]) == (REGION_ROWS, REGION_COLUMNS)
    assert (layer["lat_min"], layer["lat_max"]) == (0.0, 20.0)
    assert (layer["lon_min"], layer["lon_max"]) == (0.0, 25.0)
    assert scan.counts["cells_total"] == REGION_ROWS * REGION_COLUMNS
    assert scan.counts["tests_total"] == REGION_ROWS * REGION_COLUMNS * len(VARIABLES)


def test_slopes_are_the_same_whether_or_not_the_rest_of_the_world_is_scanned(both):
    _, (whole_scan, _), (part_scan, _) = both
    key = {f["id"]: f for f in whole_scan.findings}
    for f in part_scan.findings:
        if f["id"] in key:
            assert f["result"]["slope_per_decade"] == key[f["id"]]["result"]["slope_per_decade"]


# ------------------------------------------------------------------ mistakes are errors, not wrong ids

def test_forgetting_the_offset_in_a_later_step_is_an_error():
    data = region_of(planted())
    scan = scan_trends(data, start_month=1, grid=REGION, cell_offset=OFFSET)
    with pytest.raises(ValueError, match="outside the data"):
        add_cascades(data, scan.findings, scan.evidence, start_month=1, grid=REGION, max_lag_months=6)


@pytest.mark.parametrize("bad", [(-1, 0), (0, -2), (1.5, 0), (True, 0), (1,), "ab", None])
def test_a_bad_offset_is_refused(bad):
    with pytest.raises(ValueError, match="cell_offset"):
        check_cell_offset(bad)


def test_the_default_offset_changes_nothing():
    assert cell_object(60, 112, {"cell_degrees": 2.5, "lat_min": -90.0, "lon_min": -180.0}) == cell_object(
        60, 112, {"cell_degrees": 2.5, "lat_min": -90.0, "lon_min": -180.0}, (0, 0))
    assert cell_object(60, 112, {"cell_degrees": 2.5, "lat_min": -90.0, "lon_min": -180.0})["id"] == "r60c112"


def test_a_region_that_spans_the_world_still_wraps_for_contradictions():
    grid = {"cell_degrees": 45.0, "rows": 4, "columns": 8, "lat_min": -90.0, "lon_min": -180.0}
    region = {**grid, "rows": 2, "lat_min": 0.0}                  # the northern half: rows 2 and 3
    rng = np.random.default_rng(3)
    data = {"temperature": np.stack([[make_series(MONTHS, noise_sd=1.0, ar1=0.2, rng=rng) for _ in range(8)] for _ in range(2)])}
    t = np.arange(MONTHS)
    data["temperature"][0, 0] += 2.5 * t / 120
    data["temperature"][0, 7] -= 2.5 * t / 120
    scan = scan_trends(data, start_month=1, grid=region, cell_offset=(2, 0))
    combined = add_contradictions(scan.findings, scan.evidence, grid=region, threshold=0.05, cell_offset=(2, 0))
    assert [f["id"] for f in combined.findings if f["kind"] == "contradiction"] == ["contradiction.temperature.r02c000-r02c007"]
