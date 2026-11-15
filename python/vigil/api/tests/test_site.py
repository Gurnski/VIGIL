"""The service as a website: the viewer's page and files, what a presenter has on show, and the Detective's limit."""

from __future__ import annotations

import gzip
import json
from types import SimpleNamespace

import pytest
from fastapi.testclient import TestClient

from vigil.api.app import FIXTURE, Results, create_app
from vigil.api.document import cells_and_variables
from vigil.api.export import export, series_documents
from vigil.api.site import AskLimiter, open_to_network

HERE = ("127.0.0.1", 50000)           # a request from the machine the service runs on
ELSEWHERE = ("192.168.1.77", 50000)   # a phone in the room


@pytest.fixture(scope="module")
def document() -> dict:
    return json.loads(FIXTURE.read_text(encoding="utf-8"))


@pytest.fixture()
def site(tmp_path):
    (tmp_path / "viewer").mkdir()
    (tmp_path / "index.html").write_text("<!doctype html><title>VIGIL</title>", encoding="utf-8")
    (tmp_path / "viewer" / "index-abc.js").write_text("console.log('viewer')", encoding="utf-8")
    return tmp_path


def client_from(document, site, address):
    return TestClient(create_app(Results(document), site=site), client=address)


# ----- The page and its files -----


def test_the_service_hands_out_the_viewer(document, site):
    with client_from(document, site, ELSEWHERE) as client:
        page = client.get("/")
        assert page.status_code == 200 and "VIGIL" in page.text and page.headers["cache-control"] == "no-cache"
        assert client.get("/viewer/index-abc.js").status_code == 200
        assert client.get("/viewer/missing.js").status_code == 404


def test_the_page_gets_the_scan_the_service_holds(document, site):
    with client_from(document, site, ELSEWHERE) as client:
        reply = client.get("/snapshot.json")
        assert reply.headers["content-encoding"] == "gzip"
        assert reply.json() == document
        plain = client.get("/snapshot.json", headers={"Accept-Encoding": "identity"})
        assert "content-encoding" not in plain.headers and plain.json() == document
        assert client.get("/ask.json").json()["examples"]


def test_a_series_file_is_the_series_with_its_run(document, site):
    stored = document["series"][0]
    with client_from(document, site, ELSEWHERE) as client:
        reply = client.get(f"/series/{stored['variable']}/{stored['cell_id']}.json")
        assert reply.status_code == 200
        body = reply.json()
        assert body["series"] == [stored] and body["run_id"] == document["run"]["id"] and body["synthetic"] is True
        # The fixture has no gridded data, so a cell it stores no series for has no file.
        assert client.get(f"/series/{stored['variable']}/r0c0.json").status_code == 404
        assert client.get(f"/series/{stored['variable']}/not-a-cell.json").status_code == 404


def test_the_api_still_answers_without_a_built_viewer(document, tmp_path):
    with TestClient(create_app(Results(document), site=tmp_path)) as client:
        assert client.get("/health").status_code == 200 and client.get("/snapshot.json").status_code == 200


# ----- Presenting -----


def test_nobody_is_presenting_until_the_presenter_says_so(document, site):
    with client_from(document, site, ELSEWHERE) as client:
        assert client.get("/present").json() == {"presenting": False, "run_id": document["run"]["id"], "sequence": 0, "show": None,
                                                 "open_to_network": None}


def test_only_the_presenters_machine_can_change_what_is_on_show(document, site):
    finding = document["findings"][0]["id"]
    with client_from(document, site, ELSEWHERE) as client:
        refused = client.post("/present", json={"finding_id": finding})
        assert refused.status_code == 403
        assert client.get("/present").json()["presenting"] is False


def test_what_is_on_show_reaches_everyone_and_counts_each_change(document, site):
    finding = document["findings"][0]
    variable = document["grids"][0]["variable"]
    app = create_app(Results(document), site=site)
    with TestClient(app, client=HERE) as presenter, TestClient(app, client=ELSEWHERE) as audience:
        shown = presenter.post("/present", json={"finding_id": finding["id"], "variable": variable}).json()
        assert shown["presenting"] is True and shown["sequence"] == 1
        seen = audience.get("/present").json()
        assert seen["show"] == {"finding_id": finding["id"], "variable": variable, "zone": None}
        # The same thing again is not news: a viewer must not jump back to it.
        assert presenter.post("/present", json={"finding_id": finding["id"], "variable": variable}).json()["sequence"] == 1
        zone = {"name": "Sahel", "polygon": [[17.5, -15.0], [17.5, 35.0], [12.5, 35.0], [12.5, -15.0]]}
        assert presenter.post("/present", json={"zone": zone}).json()["sequence"] == 2
        assert audience.get("/present").json()["show"]["zone"] == zone
        stopped = presenter.post("/present", json={"presenting": False}).json()
        assert stopped == {"presenting": False, "run_id": document["run"]["id"], "sequence": 3, "show": None, "open_to_network": None}


def test_the_service_knows_whether_other_devices_can_reach_it():
    uvicorn = ["C:/python/Lib/site-packages/uvicorn/__main__.py", "vigil.api.app:app"]
    assert open_to_network(uvicorn + ["--port", "8000"], {}) is False            # uvicorn listens on this machine alone by default
    assert open_to_network(uvicorn + ["--host", "0.0.0.0"], {}) is True
    assert open_to_network(uvicorn + ["--host=192.168.1.20"], {}) is True
    assert open_to_network(uvicorn + ["--host", "127.0.0.1"], {}) is False
    assert open_to_network(uvicorn, {"UVICORN_HOST": "0.0.0.0"}) is True
    assert open_to_network(["pytest"], {}) is None                               # not started by uvicorn: it cannot tell


def test_a_presenter_cannot_show_what_the_scan_does_not_have(document, site):
    with client_from(document, site, HERE) as client:
        assert client.post("/present", json={"finding_id": "trend.nothing.r0c0"}).status_code == 422
        assert client.post("/present", json={"variable": "moonlight"}).status_code == 422
        assert client.post("/present", json={"zone": {"name": "Two points", "polygon": [[0, 0], [1, 1]]}}).status_code == 422


# ----- The Detective's limit -----


def request_from(host):
    return SimpleNamespace(client=SimpleNamespace(host=host))


def test_another_device_is_limited_and_the_presenters_is_not():
    limiter = AskLimiter(per_minute=3)
    phone, laptop = request_from("192.168.1.77"), request_from("192.168.1.78")
    assert [limiter.allow(phone, now=second) for second in (0, 1, 2, 3)] == [True, True, True, False]
    assert limiter.allow(laptop, now=3)                      # one device's questions do not count against another
    assert limiter.allow(phone, now=61)                      # the first has aged out of the minute
    assert all(limiter.allow(request_from("127.0.0.1"), now=0) for _ in range(20))


def test_a_room_of_devices_shares_one_limit_too():
    limiter = AskLimiter(per_minute=3, total_per_minute=5)
    asked = [limiter.allow(request_from(f"192.168.1.{device}"), now=0) for device in range(10, 18)]
    assert asked == [True] * 5 + [False] * 3                 # five devices got one each, and the room's minute was spent
    assert limiter.allow(request_from("127.0.0.1"), now=0)   # the presenter is never counted or refused
    assert limiter.allow(request_from("192.168.1.17"), now=61)


def test_a_refused_question_is_told_why(document, site, monkeypatch):
    monkeypatch.setenv("ANTHROPIC_API_KEY", "not-a-real-key")
    monkeypatch.setenv("VIGIL_ASK_PER_MINUTE", "0")
    with client_from(document, site, ELSEWHERE) as client:
        reply = client.post("/ask", json={"question": "Where is it warming fastest?"})
        assert reply.status_code == 429 and "Too many questions" in reply.json()["detail"]


# ----- Reading the stored scan -----


def test_the_service_reads_a_scan_that_was_stored_compressed(document, tmp_path, monkeypatch):
    """The site keeps snapshot.json compressed under its plain name. A service pointed at that copy must still start."""
    packed = tmp_path / "snapshot.json"
    packed.write_bytes(gzip.compress(json.dumps(document).encode("utf-8")))
    monkeypatch.setenv("VIGIL_SNAPSHOT", str(packed))
    monkeypatch.delenv("VIGIL_SCAN_DATA", raising=False)
    assert Results.from_environment().document == document
    plain = tmp_path / "plain.json"
    plain.write_text(json.dumps(document), encoding="utf-8")
    monkeypatch.setenv("VIGIL_SNAPSHOT", str(plain))
    assert Results.from_environment().document == document


def test_the_service_reads_gridded_data_from_an_address(tmp_path, monkeypatch):
    """On Lambda there is no file beside the service, so the gridded data comes from a bucket, as the scan does."""
    import vigil.api.app as service
    from vigil.api.document import ScanData, full_scan
    from vigil.api.tests.test_app import made_up_data

    data = made_up_data()
    stored = tmp_path / "scan-data.npz"
    data.save(stored)
    snapshot = tmp_path / "snapshot.json"
    snapshot.write_text(json.dumps(full_scan(data)), encoding="utf-8")
    asked = []

    def fetch(source: str) -> bytes:
        asked.append(source)
        return stored.read_bytes()

    monkeypatch.setattr(service, "_fetch", fetch)
    monkeypatch.setenv("VIGIL_SNAPSHOT", str(snapshot))
    monkeypatch.setattr(service, "_read", lambda source: snapshot.read_bytes())
    monkeypatch.setenv("VIGIL_SCAN_DATA", "s3://a-bucket/scan-data.npz")
    held = Results.from_environment()
    assert asked == ["s3://a-bucket/scan-data.npz"]
    assert isinstance(held.scan_data, ScanData)
    assert held.scan_data.data["temperature"].shape == data.data["temperature"].shape

# ----- The files for a website with no service -----


def test_the_export_writes_a_series_for_every_one_the_service_can_supply(document, tmp_path):
    results = Results(document)
    wanted = {(variable, cell["id"]) for finding in results.findings for cell, variable in cells_and_variables(finding)}
    made = {(variable, cell_id): body for variable, cell_id, body in series_documents(results)}
    stored = {(series["variable"], series["cell_id"]) for series in document["series"]}
    assert set(made) == wanted & stored                      # no gridded data here, so only the stored ones
    counts = export(results, tmp_path)
    assert counts["series"] == len(made)
    variable, cell_id = next(iter(made))
    on_disk = json.loads((tmp_path / "series" / variable / f"{cell_id}.json").read_text(encoding="utf-8"))
    assert on_disk == made[(variable, cell_id)] and on_disk["run_id"] == document["run"]["id"]
    assert json.loads(gzip.decompress((tmp_path / "snapshot.json.gz").read_bytes())) == document
    assert json.loads((tmp_path / "snapshot.json").read_text(encoding="utf-8")) == document
