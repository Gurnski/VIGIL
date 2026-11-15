"""The VIGIL service: the calls in ``contract/README.md``, over HTTP.

    cd python
    .venv\\Scripts\\python -m uvicorn vigil.api.app:app --port 8000

It hands out results and never changes them. Every results call answers with a results
document holding the parts that call is for. A failure is a non-2xx status with FastAPI's
usual body, ``{"detail": "..."}``.

What it serves is set by environment variables, read once when the service starts:

``VIGIL_SNAPSHOT``
    The stored scan: a results document written by ``python -m vigil.api.document``. A file
    path, an ``s3://bucket/key`` address or an ``https://`` one, as text or gzip-compressed.
    Without it the service serves the contract's fixture, which is made up and says so
    (``"synthetic": true``).
``VIGIL_SCAN_DATA``
    The gridded data the scan was made from, as the ``.npz`` file ``ScanData`` describes: a
    file path, an ``s3://bucket/key`` address or an ``https://`` one. Optional. With it,
    ``GET /series`` works for any cell and ``POST /analyse`` and ``POST /zone`` run. Without
    it, series come only from the stored document and those two calls answer 503.
``ANTHROPIC_API_KEY``
    The Detective's key. It lives only here, on the server. Without it ``POST /ask``
    answers 503 and the apps fall back to their marked sample answers.
``VIGIL_CORS_ORIGINS``
    The web addresses allowed to call the service from a browser, separated by commas.
``VIGIL_SITE``
    The folder holding the built viewer (``index.html`` and ``viewer/``), which the service
    then serves at ``/``. Left out, it is the repository's root when the built copy is there.
``VIGIL_ASK_PER_MINUTE``, ``VIGIL_ASK_TOTAL_PER_MINUTE``
    How many questions one other device, and all other devices together, may put to the
    Detective in a minute. 4 and 20 unless set.

``vigil/api/site.py`` has the page, the files it asks for and what a presenter has on show.
"""

from __future__ import annotations

import gzip
import io
import json
import logging
import os
import re
import urllib.request
from contextlib import asynccontextmanager
from pathlib import Path
from typing import Literal

from fastapi import FastAPI, HTTPException, Query, Request
from fastapi.middleware.cors import CORSMiddleware
from fastapi.middleware.gzip import GZipMiddleware
from fastapi.responses import Response
from pydantic import BaseModel, Field

from vigil.api.site import AskLimiter, add_site, site_folder
from vigil.detective.tools import FindingStore

log = logging.getLogger(__name__)

FIXTURE = Path(__file__).resolve().parents[3] / "contract" / "fixture" / "snapshot.json"
DEFAULT_ORIGINS = "https://keepvigil.earth,https://www.keepvigil.earth,http://localhost:5173,http://127.0.0.1:5173"
CELL_ID = re.compile(r"^r(\d+)c(\d+)$")

Kind = Literal["trend", "change_point", "cascade", "contradiction"]


# ----- What the service holds -----


class Results:
    """The stored scan, and the gridded data behind it when the service has them."""

    def __init__(self, document: dict, scan_data=None):
        if not document.get("contract_version"):
            raise ValueError("the snapshot is not a results document: contract_version is missing")
        self.document = document
        self.scan_data = scan_data
        self.run = document.get("run")
        # In rank order, as the contract says the list is. Never re-ranked here.
        self.findings = sorted(document.get("findings", []), key=lambda finding: finding["rank"])
        self.store = FindingStore(self.findings, self.run)
        self.series = {(s["cell_id"], s["variable"]): s for s in document.get("series", [])}
        self.grids = {grid["variable"]: grid for grid in document.get("grids", [])}
        if scan_data is not None and self.run is not None:
            settings = self.run["settings"]
            if settings["grid"] != scan_data.grid or settings["period"]["start"] != scan_data.dates[0] \
                    or settings["period"]["end"] != scan_data.dates[-1]:
                raise ValueError("the snapshot and the scan data do not match: their grids or periods differ. "
                                 "The snapshot must be the one made from this data.")

    @property
    def head(self) -> dict:
        """What every results document starts with."""
        return {"contract_version": self.document["contract_version"], "synthetic": bool(self.document.get("synthetic", False))}

    def series_for(self, cell: dict, variable: str) -> dict | None:
        """A stored series, or one built from the arrays, or None when the service has neither."""
        stored = self.series.get((cell["id"], variable))
        if stored is not None or self.scan_data is None or variable not in self.scan_data.data:
            return stored
        from vigil.api.document import make_series

        return make_series(self.scan_data.data, cell, variable, self.scan_data.dates, self.scan_data.start_month)

    @classmethod
    def from_environment(cls) -> "Results":
        source = os.environ.get("VIGIL_SNAPSHOT", "").strip()
        document = json.loads(_read(source) if source else FIXTURE.read_bytes())
        if not source:
            log.warning("VIGIL_SNAPSHOT is not set: serving the contract's synthetic fixture.")
        scan_data = None
        data_source = os.environ.get("VIGIL_SCAN_DATA", "").strip()
        if data_source:
            from vigil.api.document import ScanData       # numpy and the analysis load only when there is data

            # A bucket or a web address is fetched whole: numpy reads an .npz from memory as it does from a file.
            remote = data_source.startswith(("s3://", "https://", "http://"))
            scan_data = ScanData.load(io.BytesIO(_fetch(data_source)) if remote else data_source)
        return cls(document, scan_data)


def _read(source: str) -> bytes:
    """The stored scan's bytes, from a file, a bucket or a web address, unpacked when they are compressed."""
    return _unpacked(_fetch(source))


def _fetch(source: str) -> bytes:
    if source.startswith("s3://"):
        import boto3                                       # present on AWS Lambda; not a requirement elsewhere

        bucket, _, key = source[len("s3://"):].partition("/")
        return boto3.client("s3").get_object(Bucket=bucket, Key=key)["Body"].read()
    if source.startswith(("https://", "http://")):
        with urllib.request.urlopen(source, timeout=30) as reply:
            return reply.read()
    return Path(source).read_bytes()


def _unpacked(data: bytes) -> bytes:
    # The public site keeps snapshot.json compressed under its plain name, so that a browser gets it at a
    # tenth of the size. Read from the bucket, or through a client that does not unpack it, it arrives still
    # compressed. The first two bytes say so, whatever the name does.
    return gzip.decompress(data) if data[:2] == b"\x1f\x8b" else data


# ----- Requests -----


class Turn(BaseModel):
    role: Literal["user", "assistant"]
    text: str


class AskRequest(BaseModel):
    question: str = Field(min_length=1)
    history: list[Turn] = []
    finding_id: str | None = None


class AnalyseRequest(BaseModel):
    lat_min: float = Field(ge=-90, le=90)
    lat_max: float = Field(ge=-90, le=90)
    lon_min: float = Field(ge=-180, le=180)
    lon_max: float = Field(ge=-180, le=180)
    #: First and last month to analyse, as YYYY-MM-01, both included. Left out, the whole period.
    start: str | None = None
    end: str | None = None


class ZoneRequest(BaseModel):
    #: The outline as [latitude, longitude] points, closed from the last back to the first.
    #: Longitudes are continuous: an outline that crosses the date line runs past 180.
    polygon: list[tuple[float, float]] = Field(min_length=3)


def _json(payload: dict) -> Response:
    # Written by hand, not through FastAPI's encoder: it is quicker on a large document, and
    # allow_nan=False makes a stray NaN fail here instead of reaching an app that cannot read it.
    return Response(json.dumps(payload, allow_nan=False), media_type="application/json")


# ----- The app -----


def create_app(results: Results | None = None, site: Path | None = None) -> FastAPI:
    """
    The service. ``results`` is for tests; left out, it is read from the environment at start-up.
    ``site`` is the folder of the built viewer; left out, ``site_folder()`` finds it.
    """

    @asynccontextmanager
    async def lifespan(app: FastAPI):
        app.state.results = results if results is not None else Results.from_environment()
        yield

    app = FastAPI(title="VIGIL", version="0.1", lifespan=lifespan)
    origins = [origin.strip() for origin in os.environ.get("VIGIL_CORS_ORIGINS", DEFAULT_ORIGINS).split(",") if origin.strip()]
    app.add_middleware(CORSMiddleware, allow_origins=origins, allow_methods=["GET", "POST"], allow_headers=["Content-Type"])
    # A findings list or a globe layer is megabytes of repetitive text: a phone on a shared network needs it compressed.
    app.add_middleware(GZipMiddleware, minimum_size=2000)
    limiter = AskLimiter()
    year_fields: dict[str, str] = {}

    def held(request: Request) -> Results:
        return request.app.state.results

    @app.get("/health")
    def health(request: Request):
        r = held(request)
        return {
            "status": "ok",
            "run_id": r.run["id"] if r.run else None,
            "synthetic": r.head["synthetic"],
            "findings": len(r.findings),
            "scan_data": r.scan_data is not None,
            "detective": bool(os.environ.get("ANTHROPIC_API_KEY")),
        }

    @app.get("/snapshot")
    def snapshot(request: Request):
        return _json(held(request).document)

    @app.get("/run")
    def run(request: Request):
        r = held(request)
        return _json({**r.head, "run": r.run, "variables": r.document.get("variables", [])})

    @app.get("/findings")
    def findings(
        request: Request,
        kind: Kind | None = None,
        variable: str | None = None,
        significant: bool | None = None,
        limit: int | None = Query(None, ge=1),
        lat_min: float | None = Query(None, ge=-90, le=90),
        lat_max: float | None = Query(None, ge=-90, le=90),
        lon_min: float | None = Query(None, ge=-180, le=180),
        lon_max: float | None = Query(None, ge=-180, le=180),
    ):
        r = held(request)
        # The Detective's own search does the filtering, so the list a person sees and the list
        # the Detective searches follow one rule: a finding is in the box when its cell overlaps it.
        matches: list[dict] = []
        for wanted in ([True, False] if significant is None else [significant]):
            found, _ = r.store.search(kind=kind, variable=variable, significant=wanted, lat_min=lat_min, lat_max=lat_max,
                                      lon_min=lon_min, lon_max=lon_max, sort_by="rank", limit=len(r.findings) + 1)
            matches += found
        matches.sort(key=lambda finding: finding["rank"])
        return _json({**r.head, "findings": matches[:limit]})

    @app.get("/findings/{finding_id}")
    def finding(request: Request, finding_id: str):
        r = held(request)
        found = r.store.by_id.get(finding_id)
        if found is None:
            raise HTTPException(404, f"There is no finding with the id {finding_id}.")
        from vigil.api.document import cells_and_variables

        series = [r.series_for(cell, variable) for cell, variable in cells_and_variables(found)]
        # A series the service cannot supply is left out: the apps cope with a finding that has none.
        return _json({**r.head, "findings": [found], "series": [s for s in series if s is not None]})

    @app.get("/series")
    def series(request: Request, cell_id: str, variable: str):
        r = held(request)
        stored = r.series.get((cell_id, variable))
        if stored is not None:
            return _json({**r.head, "series": [stored]})
        match = CELL_ID.match(cell_id)
        if not match:
            raise HTTPException(422, "cell_id must look like r60c112: r, the row, c, the column.")
        if r.scan_data is None:
            raise HTTPException(404, f"The stored results hold no series for {variable} in {cell_id}, and the service "
                                     "has no gridded data to build one from.")
        if variable not in r.scan_data.data:
            raise HTTPException(404, f"There is no variable called {variable} in this scan.")
        row, column = int(match[1]), int(match[2])
        grid = r.scan_data.grid
        if row >= grid["rows"] or column >= grid["columns"]:
            raise HTTPException(404, f"{cell_id} is off the scan grid, which has {grid['rows']} rows and {grid['columns']} columns.")
        from vigil.analysis.scan import cell_object

        return _json({**r.head, "series": [r.series_for(cell_object(row, column, grid), variable)]})

    @app.get("/grids")
    def grids(request: Request, variable: str | None = None):
        r = held(request)
        if variable is None:
            return _json({**r.head, "grids": list(r.grids.values())})
        if variable not in r.grids:
            raise HTTPException(404, f"There is no globe layer for a variable called {variable}.")
        return _json({**r.head, "grids": [r.grids[variable]]})

    @app.post("/analyse")
    def analyse(request: Request, body: AnalyseRequest):
        r = held(request)
        if r.scan_data is None:
            raise HTTPException(503, "This service has the stored results but not the gridded data, so it cannot run a new analysis.")
        from vigil.api.document import RegionError, analyse_region

        try:
            return _json(analyse_region(r.scan_data, body.lat_min, body.lat_max, body.lon_min, body.lon_max, body.start, body.end))
        except RegionError as problem:
            raise HTTPException(422, str(problem)) from problem

    @app.post("/zone")
    def zone(request: Request, body: ZoneRequest):
        r = held(request)
        if r.scan_data is None:
            raise HTTPException(503, "This service has the stored results but not the gridded data, so it cannot "
                                     "work out the series for a zone.")
        from vigil.api.zone import ZoneError, analyse_zone

        try:
            return _json(analyse_zone(r.scan_data, r.grids, r.run, body.polygon))
        except ZoneError as problem:
            raise HTTPException(422, str(problem)) from problem

    def years_text(request: Request, variable: str) -> str:
        """A variable year by year, as JSON text. Shared by ``GET /years`` and the file the viewer asks for."""
        r = held(request)
        if r.scan_data is None:
            raise HTTPException(503, "This service has the stored results but not the gridded data, so it cannot "
                                     "show a variable year by year.")
        if variable not in r.grids:
            raise HTTPException(404, f"There is no globe layer for a variable called {variable}.")
        if variable not in year_fields:
            from vigil.api.years import YearsError, annual_anomalies

            try:
                made = annual_anomalies(r.scan_data, r.grids[variable], variable, r.run["id"] if r.run else None)
            except YearsError as problem:
                raise HTTPException(422, str(problem)) from problem
            # Kept as text: it is the same for everyone who asks, and takes a second or two to make.
            year_fields[variable] = json.dumps(made, allow_nan=False, separators=(",", ":"))
        return year_fields[variable]

    @app.get("/years")
    def years(request: Request, variable: str):
        return Response(years_text(request, variable), media_type="application/json")

    @app.post("/ask")
    def ask(request: Request, body: AskRequest):
        r = held(request)
        if not os.environ.get("ANTHROPIC_API_KEY"):
            raise HTTPException(503, "The Detective is not set up on this server.")
        if not limiter.allow(request):
            raise HTTPException(429, "Too many questions in the last minute, from this device or from the room. Wait a little and ask again.")
        import anthropic

        from vigil.api.trace import TracingClient
        from vigil.detective.ask import ask as ask_detective

        try:
            # The Detective calls the model through this client, which notes each tool call on its way past.
            tracer = TracingClient(anthropic.Anthropic())
            # The full list and the run record, not a trimmed one: the Detective searches what it is given.
            answer = ask_detective(body.question, r.findings, r.run, [turn.model_dump() for turn in body.history],
                                   body.finding_id, client=tracer)
        except Exception as problem:                       # whatever went wrong, the app gets a clean error
            log.exception("The Detective failed")
            raise HTTPException(502, "The Detective could not answer just now.") from problem
        if tracer.steps:
            # What it looked up, in order, for the apps to show beside the answer. See "steps" in the contract.
            answer = {**answer, "steps": tracer.steps}
        return _json(answer)

    add_site(app, held, site if site is not None else site_folder(), years_text)
    return app


app = create_app()
