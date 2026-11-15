"""A variable year by year: the annual mean of each scanned cell's anomalies."""

from __future__ import annotations

import json

import numpy as np
import pytest
from fastapi.testclient import TestClient

from vigil.analysis.trend import deseasonalise
from vigil.api.app import FIXTURE, Results, create_app
from vigil.api.document import ScanData, full_scan
from vigil.api.years import MIN_MONTHS, annual_anomalies


def made_up() -> ScanData:
    """Ten years on a small grid: one cell warms steadily, one loses most of a year, one row is ice."""
    rng = np.random.default_rng(3)
    rows, columns, months = 3, 4, 120
    month = np.arange(months)
    temperature = 280 + 6 * np.sin(2 * np.pi * month / 12) + rng.normal(0, 0.3, (rows, columns, months))
    temperature[1, 2] += 0.02 * month
    temperature[0, 0, 36:41] = np.nan                       # five months of the fourth year: seven are left
    mask = np.full((rows, columns), None, dtype=object)
    mask[2, :] = "ice"
    grid = {"cell_degrees": 2.5, "rows": rows, "columns": columns, "lat_min": 40.0, "lon_min": 10.0}
    return ScanData(data={"temperature": temperature}, grid=grid, period_start="2010-01-01",
                    mask={"temperature": mask}, regridding="Made up for the tests", synthetic=True)


@pytest.fixture(scope="module")
def data() -> ScanData:
    return made_up()


@pytest.fixture(scope="module")
def document(data) -> dict:
    return full_scan(data)


def layer(document: dict) -> dict:
    return next(grid for grid in document["grids"] if grid["variable"] == "temperature")


def test_a_years_value_is_the_mean_of_its_months_anomalies(data, document):
    answer = annual_anomalies(data, layer(document), "temperature", document["run"]["id"])
    assert answer["years"] == list(range(2010, 2020)) and answer["min_months"] == MIN_MONTHS
    assert (answer["rows"], answer["columns"]) == (3, 4) and answer["lat_max"] == 47.5 and answer["lon_max"] == 20.0
    anomaly = deseasonalise(data.data["temperature"][1, 2], data.start_month)
    for index in range(10):
        assert answer["values"][index][1][2] == pytest.approx(anomaly[index * 12:(index + 1) * 12].mean(), rel=1e-3)
    # The warming cell goes from below its own normal to above it.
    assert answer["values"][0][1][2] < -0.8 and answer["values"][9][1][2] > 0.8


def test_a_thin_year_and_a_skipped_cell_have_no_value(data, document):
    answer = annual_anomalies(data, layer(document), "temperature", document["run"]["id"])
    assert answer["values"][3][0][0] is None                 # seven months that year
    assert answer["values"][2][0][0] is not None
    assert all(answer["values"][year][2][column] is None for year in range(10) for column in range(4))   # ice


def test_the_scale_ends_inside_the_largest_values(data, document):
    answer = annual_anomalies(data, layer(document), "temperature", document["run"]["id"])
    sizes = [abs(value) for year in answer["values"] for line in year for value in line if value is not None]
    assert 0 < answer["scale_max"] <= max(sizes)


def test_the_service_answers_and_says_which_run(data, document):
    with TestClient(create_app(Results(document, data))) as client:
        reply = client.get("/years", params={"variable": "temperature"})
        assert reply.status_code == 200
        body = reply.json()
        assert body["run_id"] == document["run"]["id"] and body["variable"] == "temperature" and body["units"] == "K"
        assert client.get("/years", params={"variable": "temperature"}).json() == body      # the kept copy
        assert client.get("/years", params={"variable": "moonlight"}).status_code == 404


def test_the_export_writes_the_same_answer_as_a_file(data, document, tmp_path):
    from vigil.api.export import export

    counts = export(Results(document, data), tmp_path)
    assert counts["years"] == 1
    on_disk = json.loads((tmp_path / "years" / "temperature.json").read_text(encoding="utf-8"))
    assert on_disk == annual_anomalies(data, layer(document), "temperature", document["run"]["id"])


def test_the_service_hands_out_the_same_answer_as_a_file(data, document):
    with TestClient(create_app(Results(document, data))) as client:
        as_file = client.get("/years/temperature.json")
        assert as_file.status_code == 200
        assert as_file.json() == client.get("/years", params={"variable": "temperature"}).json()
        assert client.get("/years/moonlight.json").status_code == 404
        assert client.get("/years/temperature").status_code == 404


def test_it_needs_the_gridded_data():
    with TestClient(create_app(Results(json.loads(FIXTURE.read_text(encoding="utf-8"))))) as client:
        variable = client.get("/grids").json()["grids"][0]["variable"]
        reply = client.get("/years", params={"variable": variable})
        assert reply.status_code == 503 and "gridded data" in reply.json()["detail"]
        # As a file, what cannot be made is simply not there.
        assert client.get(f"/years/{variable}.json").status_code == 404
