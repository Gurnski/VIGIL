"""The service's calls, against the contract's fixture and against made-up gridded data."""

from __future__ import annotations

import importlib.util
import json
from pathlib import Path

import numpy as np
import pytest
from fastapi.testclient import TestClient

from vigil.api.app import FIXTURE, Results, create_app
from vigil.api.document import MAX_REGION_CELLS, ScanData, analyse_region, full_scan, write

CONTRACT = FIXTURE.parents[1]


def load_checker():
    spec = importlib.util.spec_from_file_location("contract_check", CONTRACT / "tools" / "check.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


@pytest.fixture(scope="module")
def fixture_document() -> dict:
    return json.loads(FIXTURE.read_text(encoding="utf-8"))


@pytest.fixture()
def client(fixture_document):
    with TestClient(create_app(Results(fixture_document))) as test_client:
        yield test_client


# ----- Against the stored fixture -----


def test_snapshot_is_the_stored_document(client, fixture_document):
    reply = client.get("/snapshot")
    assert reply.status_code == 200
    assert reply.json() == fixture_document


def test_run_has_the_run_and_the_variables(client, fixture_document):
    body = client.get("/run").json()
    assert set(body) == {"contract_version", "synthetic", "run", "variables"}
    assert body["run"]["id"] == fixture_document["run"]["id"]
    assert body["synthetic"] is True


def test_findings_come_in_rank_order(client, fixture_document):
    ranks = [finding["rank"] for finding in client.get("/findings").json()["findings"]]
    assert ranks == sorted(ranks)
    assert len(ranks) == len(fixture_document["findings"])


def test_findings_filters(client, fixture_document):
    all_findings = fixture_document["findings"]
    trends = client.get("/findings", params={"kind": "trend"}).json()["findings"]
    assert trends and all(finding["kind"] == "trend" for finding in trends)

    not_significant = client.get("/findings", params={"significant": "false"}).json()["findings"]
    assert len(not_significant) == sum(not finding["significant"] for finding in all_findings)

    vegetation = client.get("/findings", params={"variable": "vegetation"}).json()["findings"]
    assert vegetation and all(any(v["id"] == "vegetation" for v in finding["variables"]) for finding in vegetation)

    top = client.get("/findings", params={"limit": 3}).json()["findings"]
    assert [finding["rank"] for finding in top] == [1, 2, 3]


def test_findings_in_a_box_are_those_whose_cell_overlaps_it(client, fixture_document):
    cell = fixture_document["findings"][0]["cell"]
    box = {"lat_min": cell["lat_min"] + 0.1, "lat_max": cell["lat_max"] - 0.1,
           "lon_min": cell["lon_min"] + 0.1, "lon_max": cell["lon_max"] - 0.1}
    found = client.get("/findings", params=box).json()["findings"]
    assert found and all(finding["cell"]["id"] == cell["id"] for finding in found)


def test_one_finding_comes_with_its_series(client, fixture_document):
    contradiction = next(f for f in fixture_document["findings"] if f["kind"] == "contradiction")
    body = client.get(f"/findings/{contradiction['id']}").json()
    assert [finding["id"] for finding in body["findings"]] == [contradiction["id"]]
    cells = {series["cell_id"] for series in body["series"]}
    assert cells == {contradiction["cell"]["id"], contradiction["contradiction"]["paired_cell"]["id"]}


def test_unknown_things_are_404_with_a_detail(client):
    for path in ("/findings/trend.temperature.r00c000", "/series?cell_id=r00c000&variable=temperature", "/grids?variable=wind"):
        reply = client.get(path)
        assert reply.status_code == 404, path
        assert reply.json()["detail"]


def test_series_and_grids(client, fixture_document):
    stored = fixture_document["series"][0]
    body = client.get("/series", params={"cell_id": stored["cell_id"], "variable": stored["variable"]}).json()
    assert body["series"] == [stored]
    assert len(client.get("/grids").json()["grids"]) == len(fixture_document["grids"])
    one = client.get("/grids", params={"variable": "temperature"}).json()["grids"]
    assert [grid["variable"] for grid in one] == ["temperature"]


def test_analyse_needs_the_gridded_data(client):
    reply = client.post("/analyse", json={"lat_min": 0, "lat_max": 10, "lon_min": 0, "lon_max": 10})
    assert reply.status_code == 503


def test_ask_without_a_key_is_503(client, monkeypatch):
    monkeypatch.delenv("ANTHROPIC_API_KEY", raising=False)
    assert client.post("/ask", json={"question": "Where is it warming fastest?"}).status_code == 503


def test_ask_passes_the_full_list_and_returns_the_answer(client, fixture_document, monkeypatch):
    seen = {}

    def fake_ask(question, findings, run, history, finding_id):
        seen.update(question=question, findings=findings, run=run, history=history, finding_id=finding_id)
        return {"contract_version": "0.1", "run_id": run["id"], "status": "answered", "answer": "An answer.",
                "citations": [{"finding_id": findings[0]["id"]}], "limitations": []}

    monkeypatch.setenv("ANTHROPIC_API_KEY", "test-key")
    monkeypatch.setattr("vigil.detective.ask.ask", fake_ask)
    reply = client.post("/ask", json={"question": "Why?", "history": [{"role": "user", "text": "Earlier"}],
                                      "finding_id": fixture_document["findings"][2]["id"]})
    assert reply.status_code == 200 and reply.json()["answer"] == "An answer."
    assert len(seen["findings"]) == len(fixture_document["findings"])
    assert seen["history"] == [{"role": "user", "text": "Earlier"}]
    assert seen["finding_id"] == fixture_document["findings"][2]["id"]
    assert client.post("/ask", json={"question": ""}).status_code == 422


def test_a_browser_on_the_site_may_call_the_service(client):
    reply = client.options("/ask", headers={"Origin": "https://keepvigil.earth", "Access-Control-Request-Method": "POST",
                                             "Access-Control-Request-Headers": "content-type"})
    assert reply.headers.get("access-control-allow-origin") == "https://keepvigil.earth"


# ----- Against made-up gridded data: the whole path from arrays to a checked document -----


def made_up_data() -> ScanData:
    """A small grid of noise, with a warming block and a drying block planted in it."""
    rng = np.random.default_rng(7)
    rows, columns, months = 8, 12, 240
    month = np.arange(months)
    season = np.sin(2 * np.pi * month / 12)
    temperature = 280 + 5 * season + rng.normal(0, 0.6, (rows, columns, months))
    precipitation = 80 + 30 * season + rng.normal(0, 8, (rows, columns, months))
    temperature[2:5, 3:7] += 0.012 * month
    precipitation[4:6, 6:9] -= 0.09 * month
    temperature[0, 0, 5:9] = np.nan                         # a few missing months
    mask = np.full((rows, columns), None, dtype=object)
    mask[7, :] = "ice"
    mask[:, 11] = "ocean"
    grid = {"cell_degrees": 2.5, "rows": rows, "columns": columns, "lat_min": 10.0, "lon_min": -20.0}
    return ScanData(data={"temperature": temperature, "precipitation": precipitation}, grid=grid,
                    period_start="2004-01-01", mask=mask, regridding="Made up for the tests", synthetic=True)


@pytest.fixture(scope="module")
def made_up() -> ScanData:
    return made_up_data()


@pytest.fixture(scope="module")
def scanned(made_up) -> dict:
    return full_scan(made_up)


def check(document: dict, tmp_path: Path) -> list[str]:
    path = tmp_path / "document.json"
    write(document, path)
    checker = load_checker()
    document = checker.load(path)
    checker.validate_schema(document)
    checker.check_results(document)
    return list(checker.errors)


def test_scan_data_survives_a_round_trip_to_disk(made_up, tmp_path):
    made_up.save(tmp_path / "scan-data.npz")
    loaded = ScanData.load(tmp_path / "scan-data.npz")
    assert loaded.grid == made_up.grid and loaded.dates == made_up.dates and loaded.synthetic is True
    assert np.array_equal(loaded.data["temperature"], made_up.data["temperature"], equal_nan=True)
    assert (loaded.mask == made_up.mask).all()


def test_the_full_scan_writes_a_document_the_checker_accepts(scanned, tmp_path):
    assert scanned["findings"], "the planted trends should have been found"
    assert check(scanned, tmp_path) == []
    first = scanned["findings"][0]
    assert first["summary"] and first["variables"][0]["mission"] and first["standing"]["limitations"]


def test_a_region_document_passes_the_checker_and_keeps_full_grid_ids(made_up, tmp_path):
    grid = made_up.grid
    document = analyse_region(made_up, lat_min=grid["lat_min"] + 5, lat_max=grid["lat_min"] + 12.5,
                              lon_min=grid["lon_min"] + 7.5, lon_max=grid["lon_min"] + 17.5)
    assert check(document, tmp_path) == []
    region = document["run"]["settings"]["region"]
    assert region == {"row": 2, "column": 3, "rows": 3, "columns": 4}
    assert document["run"]["settings"]["grid"] == grid
    assert document["run"]["counts"]["cells_total"] == 12
    assert any(not finding["significant"] for finding in document["findings"]), "tests that did not pass are returned too"
    for finding in document["findings"]:
        assert 2 <= finding["cell"]["row"] < 5 and 3 <= finding["cell"]["column"] < 7


def test_the_service_builds_series_and_analyses_when_it_has_the_data(made_up, scanned):
    with TestClient(create_app(Results(scanned, made_up))) as client:
        body = client.get("/series", params={"cell_id": "r01c001", "variable": "precipitation"}).json()
        assert len(body["series"][0]["raw"]) == made_up.months

        grid = made_up.grid
        reply = client.post("/analyse", json={"lat_min": grid["lat_min"] + 5, "lat_max": grid["lat_min"] + 12.5,
                                              "lon_min": grid["lon_min"] + 7.5, "lon_max": grid["lon_min"] + 17.5})
        assert reply.status_code == 200
        assert reply.json()["run"]["settings"]["region"]["rows"] == 3

        across_date_line = client.post("/analyse", json={"lat_min": 10, "lat_max": 20, "lon_min": 170, "lon_max": -170})
        assert across_date_line.status_code == 422 and "date line" in across_date_line.json()["detail"]


def test_a_region_too_large_is_refused(made_up):
    huge = ScanData(data={"temperature": np.zeros((30, 30, 24))},
                    grid={"cell_degrees": 2.5, "rows": 30, "columns": 30, "lat_min": -30.0, "lon_min": -30.0},
                    period_start="2004-01-01")
    assert 30 * 30 > MAX_REGION_CELLS
    with pytest.raises(ValueError, match="smaller area"):
        analyse_region(huge, lat_min=-30, lat_max=45, lon_min=-30, lon_max=45)


def test_a_snapshot_that_does_not_match_the_data_is_refused(made_up, fixture_document):
    with pytest.raises(ValueError, match="do not match"):
        Results(fixture_document, made_up)
