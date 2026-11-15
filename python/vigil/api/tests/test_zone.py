"""A drawn zone: which cells it takes in, the series it makes of them, and the test on that series."""

from __future__ import annotations

import json

import numpy as np
import pytest
from fastapi.testclient import TestClient

from vigil.api.app import FIXTURE, Results, create_app
from vigil.api.document import MIN_COVERAGE, ScanData, full_scan
from vigil.api.zone import RARE_SNOW, RARE_SNOW_NOTE, ZoneError, analyse_zone, caution_for, zone_cells

WORLD = {"cell_degrees": 2.5, "rows": 72, "columns": 144, "lat_min": -90.0, "lon_min": -180.0}


def box(lat_min, lat_max, lon_min, lon_max):
    return [[lat_max, lon_min], [lat_max, lon_max], [lat_min, lon_max], [lat_min, lon_min]]


def around(cell: dict) -> list:
    """An outline just inside one cell, so it holds that cell's centre and no other."""
    return box(cell["lat_min"] + 0.2, cell["lat_max"] - 0.2, cell["lon_min"] + 0.2, cell["lon_max"] - 0.2)


# ----- Geometry -----


def test_a_box_takes_the_cells_whose_centres_it_holds():
    cells = zone_cells(box(0, 5, 10, 17.5), WORLD)
    rows, columns = np.nonzero(cells)
    assert sorted(set(rows)) == [36, 37] and sorted(set(columns)) == [76, 77, 78]
    # An edge through the middle of a cell leaves the cells whose centres are beyond it.
    assert zone_cells(box(0, 5, 10, 16), WORLD).sum() == 4


def test_an_outline_of_any_shape_follows_the_even_odd_rule():
    triangle = [[0, 0], [10, 0], [0, 10]]
    cells = zone_cells(triangle, WORLD)
    rows, columns = np.nonzero(cells)
    for row, column in zip(rows, columns):
        latitude, longitude = -90 + (row + 0.5) * 2.5, -180 + (column + 0.5) * 2.5
        assert latitude > 0 and longitude > 0 and latitude + longitude < 10
    assert cells.sum() == 6


def test_an_outline_across_the_date_line_runs_past_180():
    cells = zone_cells(box(0, 5, 175, 185), WORLD)
    rows, columns = np.nonzero(cells)
    assert sorted(set(columns)) == [0, 1, 142, 143] and sorted(set(rows)) == [36, 37]
    # The same area, written from the other side.
    assert (zone_cells(box(0, 5, -185, -175), WORLD) == cells).all()


def test_outlines_that_cannot_be_zones_are_refused():
    with pytest.raises(ZoneError, match="pole"):
        zone_cells([[80, lon] for lon in range(0, 361, 30)], WORLD)
    with pytest.raises(ZoneError, match="three"):
        zone_cells([[0, 0], [1, 1]], WORLD)
    with pytest.raises(ZoneError, match="Latitudes"):
        zone_cells([[0, 0], [95, 0], [0, 5]], WORLD)


# ----- The series and the test -----


def made_up() -> ScanData:
    """Noise with a warming block, on a grid far enough north that cell areas differ row to row."""
    rng = np.random.default_rng(11)
    rows, columns, months = 8, 12, 240
    month = np.arange(months)
    season = np.sin(2 * np.pi * month / 12)
    temperature = 280 + 5 * season + rng.normal(0, 0.6, (rows, columns, months))
    precipitation = 80 + 30 * season + rng.normal(0, 8, (rows, columns, months))
    temperature[2:5, 3:7] += 0.012 * month
    # Earthquakes: busy in one corner and rising there, a handful elsewhere.
    earthquakes = rng.poisson(0.02, (rows, columns, months)).astype(float)
    earthquakes[1, 1] = rng.poisson(1.0 + 0.02 * month)
    mask = np.full((rows, columns), None, dtype=object)
    mask[7, :] = "ice"
    mask[:, 11] = "ocean"
    grid = {"cell_degrees": 2.5, "rows": rows, "columns": columns, "lat_min": 50.0, "lon_min": -20.0}
    return ScanData(data={"temperature": temperature, "precipitation": precipitation, "earthquakes": earthquakes},
                    grid=grid, period_start="2004-01-01",
                    mask={"temperature": mask, "precipitation": mask}, regridding="Made up for the tests", synthetic=True)


@pytest.fixture(scope="module")
def data() -> ScanData:
    return made_up()


@pytest.fixture(scope="module")
def scanned(data) -> dict:
    return full_scan(data)


def layers(document: dict) -> dict:
    return {layer["variable"]: layer for layer in document["grids"]}


def by_variable(answer: dict) -> dict:
    return {summary["variable"]: summary for summary in answer["summaries"]}


@pytest.mark.parametrize("variable", ["temperature", "earthquakes"])
def test_a_zone_of_one_cell_gives_that_cells_own_result(data, scanned, variable):
    """The check that the zone is tested as a cell is: same weighting, months, anomalies and test."""
    finding = next(f for f in scanned["findings"] if f["kind"] == "trend" and f["variables"][0]["id"] == variable)
    answer = analyse_zone(data, layers(scanned), scanned["run"], around(finding["cell"]))
    assert answer["zone"]["cells"] == [finding["cell"]["id"]]
    summary = by_variable(answer)[variable]
    assert summary["tested"] and summary["cells_used"] == 1
    for key in ("slope_per_decade", "ci_low", "ci_high", "p_value", "units", "ci_level"):
        assert summary["result"][key] == pytest.approx(finding["result"][key]), key
    assert summary["method"]["test"] == finding["method"]["test"]
    assert summary["preprocessing"]["months_present"] == finding["preprocessing"]["months_present"]
    # One test in its family: nothing to correct for.
    assert summary["result"]["p_adjusted"] == pytest.approx(summary["result"]["p_value"])
    stored = next(s for s in scanned["series"] if s["cell_id"] == finding["cell"]["id"] and s["variable"] == variable)
    assert summary["series"]["raw"] == pytest.approx(stored["raw"], nan_ok=True)
    assert summary["series"]["anomaly"] == pytest.approx(stored["anomaly"], nan_ok=True)


def test_measured_variables_are_averaged_by_cell_area_over_the_scanned_cells(data, scanned):
    grid = data.grid
    # Rows 5, 6 and 7 of column 2. Row 7 is ice, so only rows 5 and 6 were scanned.
    outline = box(grid["lat_min"] + 12.5, grid["lat_min"] + 20, grid["lon_min"] + 5, grid["lon_min"] + 7.5)
    answer = analyse_zone(data, layers(scanned), scanned["run"], outline)
    assert answer["zone"]["cell_count"] == 3
    summary = by_variable(answer)["temperature"]
    assert summary["cells_used"] == 2 and summary["cells_skipped"] == {"ice": 1}

    south = np.radians(grid["lat_min"] + np.array([5, 6]) * 2.5)
    weight = np.sin(south + np.radians(2.5)) - np.sin(south)
    expected = (data.data["temperature"][5, 2] * weight[0] + data.data["temperature"][6, 2] * weight[1]) / weight.sum()
    assert summary["series"]["raw"] == pytest.approx(expected.tolist())
    assert weight[0] > weight[1], "the cell nearer the equator is the larger, so it must weigh more"
    assert "weighted by cell area" in summary["preprocessing"]["aggregation"]


def test_a_month_missing_from_too_much_of_the_zone_is_left_missing(data, scanned):
    grid = data.grid
    thinned = ScanData(data={name: array.copy() for name, array in data.data.items()}, grid=grid,
                       period_start=data.period_start, mask=data.mask, synthetic=True)
    thinned.data["temperature"][5, 2, 100] = np.nan            # half the zone's area gone in month 100
    outline = box(grid["lat_min"] + 12.5, grid["lat_min"] + 17.5, grid["lon_min"] + 5, grid["lon_min"] + 7.5)
    series = by_variable(analyse_zone(thinned, layers(scanned), scanned["run"], outline))["temperature"]["series"]
    assert series["raw"][100] is None and series["raw"][99] is not None
    assert MIN_COVERAGE > 0.5


def test_counts_are_added_up_over_every_cell_of_the_zone(data, scanned):
    grid = data.grid
    outline = box(grid["lat_min"], grid["lat_min"] + 7.5, grid["lon_min"], grid["lon_min"] + 7.5)
    answer = analyse_zone(data, layers(scanned), scanned["run"], outline)
    summary = by_variable(answer)["earthquakes"]
    assert summary["cells_used"] == 9 and summary["cells_skipped"] == {}
    assert summary["series"]["raw"] == pytest.approx(data.data["earthquakes"][0:3, 0:3].sum(axis=(0, 1)).tolist())
    assert summary["tested"] and summary["result"]["direction"] == "increasing"
    assert "added up" in summary["preprocessing"]["aggregation"]


def test_a_zone_with_nothing_to_test_says_why(data, scanned):
    grid = data.grid
    # The ocean column only: no measured variable was scanned there, and its earthquakes are too few.
    outline = box(grid["lat_min"] + 2.5, grid["lat_min"] + 7.5, grid["lon_min"] + 27.5, grid["lon_min"] + 30)
    summaries = by_variable(analyse_zone(data, layers(scanned), scanned["run"], outline))
    assert summaries["temperature"]["tested"] is False and summaries["temperature"]["skip_reason"] == "no_cells"
    assert summaries["temperature"]["series"] is None and summaries["temperature"]["result"] is None
    assert summaries["earthquakes"]["tested"] is False and summaries["earthquakes"]["skip_reason"] == "coverage"
    assert summaries["earthquakes"]["series"] is not None and summaries["earthquakes"]["result"] is None
    with pytest.raises(ZoneError, match="no centre"):
        analyse_zone(data, layers(scanned), scanned["run"], box(0, 5, 0, 5))


def test_the_answer_says_what_its_p_value_is_and_carries_no_cell(data, scanned):
    finding = next(f for f in scanned["findings"] if f["kind"] == "trend" and f["variables"][0]["id"] == "temperature")
    answer = analyse_zone(data, layers(scanned), scanned["run"], around(finding["cell"]))
    json.dumps(answer, allow_nan=False)
    assert answer["run_id"] == scanned["run"]["id"] and answer["synthetic"] is True
    assert answer["zone"]["area_km2"] > 0
    for summary in answer["summaries"]:
        assert not {"id", "cell", "rank", "score", "links"} & set(summary), "a zone result is not a finding"
        if summary["tested"]:
            assert any("drawn by hand" in line for line in summary["limitations"])
            assert any("no correction" in line for line in summary["limitations"])


# ----- The call -----


def test_the_service_answers_for_a_zone_when_it_has_the_data(data, scanned):
    grid = data.grid
    outline = box(grid["lat_min"] + 5, grid["lat_min"] + 12.5, grid["lon_min"] + 7.5, grid["lon_min"] + 17.5)
    with TestClient(create_app(Results(scanned, data))) as client:
        reply = client.post("/zone", json={"polygon": outline})
        assert reply.status_code == 200
        body = reply.json()
        assert body["zone"]["cell_count"] == 12 and body["run_id"] == scanned["run"]["id"]
        warming = by_variable(body)["temperature"]
        assert warming["significant"] and warming["result"]["direction"] == "increasing"

        refused = client.post("/zone", json={"polygon": [[80, lon] for lon in range(0, 361, 30)]})
        assert refused.status_code == 422 and "pole" in refused.json()["detail"]
        assert client.post("/zone", json={"polygon": [[0, 0], [1, 1]]}).status_code == 422


def test_a_zone_needs_the_gridded_data():
    with TestClient(create_app(Results(json.loads(FIXTURE.read_text(encoding="utf-8"))))) as client:
        reply = client.post("/zone", json={"polygon": box(0, 5, 0, 5)})
        assert reply.status_code == 503 and "gridded data" in reply.json()["detail"]


# ----- Cautions -----


def test_a_snow_trend_where_snow_is_rare_carries_a_caution():
    months = np.arange(240)
    rare = 0.05 + 0.0002 * months
    assert caution_for("snow_cover", rare) == (RARE_SNOW, RARE_SNOW_NOTE)
    assert caution_for("snow_cover", 40 + 0.01 * months) is None
    # The rule is about snow. A small number in another variable is not a reason for doubt.
    assert caution_for("precipitation", rare) is None
    assert caution_for("snow_cover", np.full(240, np.nan)) is None


def test_the_caution_is_on_the_zones_answer_and_leads_its_limitations():
    rng = np.random.default_rng(5)
    rows, columns, months = 2, 2, 240
    month = np.arange(months)
    snow = np.clip(0.05 + 0.0004 * month + rng.normal(0, 0.01, (rows, columns, months)), 0, None)
    temperature = 280 + rng.normal(0, 0.5, (rows, columns, months)) + 0.01 * month
    mask = np.full((rows, columns), None, dtype=object)
    grid = {"cell_degrees": 2.5, "rows": rows, "columns": columns, "lat_min": -10.0, "lon_min": -60.0}
    data = ScanData(data={"temperature": temperature, "snow_cover": snow}, grid=grid, period_start="2004-01-01",
                    mask={"temperature": mask, "snow_cover": mask}, regridding="Made up for the tests", synthetic=True)
    document = full_scan(data)
    answer = by_variable(analyse_zone(data, layers(document), document["run"], box(-10, -5, -60, -55)))
    snow_answer = answer["snow_cover"]
    assert snow_answer["result"] is not None and snow_answer["caution"] == RARE_SNOW
    assert snow_answer["limitations"][0] == RARE_SNOW_NOTE
    assert answer["temperature"]["caution"] is None
