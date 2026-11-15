"""The parts of the service that make it a website of its own.

Started on a laptop, the service can hand the built viewer to any phone or laptop on the same
network, and those viewers then have everything the desktop app has: the Detective, a chart
for every finding, zones. Three things live here:

- the viewer's page and the files it asks for beside it (``snapshot.json``, ``ask.json``,
  ``series/<variable>/<cell_id>.json`` and ``years/<variable>.json``), answered from what
  the service holds, so the page always shows the service's own scan;
- what a presenter has on show (``GET`` and ``POST /present``), which viewers can follow;
- a limit on how many questions one device may put to the Detective, because a service open
  to a room spends its owner's key.

The formats are in ``contract/README.md`` under "Series as files beside the viewer" and
"Presenting". Nothing here computes or changes a result.

To let other devices in, start the service on every address, not only this machine's own:

    .venv\\Scripts\\python -m uvicorn vigil.api.app:app --host 0.0.0.0 --port 8000
"""

from __future__ import annotations

import gzip
import json
import os
import re
import sys
import threading
import time
from pathlib import Path

from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import FileResponse, Response
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel, Field

ROOT = Path(__file__).resolve().parents[3]
ASK_FIXTURE = ROOT / "contract" / "fixture" / "ask.json"
SERIES_FILE = re.compile(r"^(r\d+c\d+)\.json$")

#: The addresses a request has when it comes from the machine the service runs on.
LOOPBACK = {"127.0.0.1", "::1"}

#: Questions one other device may ask the Detective in a minute, unless VIGIL_ASK_PER_MINUTE says otherwise.
ASK_PER_MINUTE = 4

#: Questions all other devices together may ask in a minute, unless VIGIL_ASK_TOTAL_PER_MINUTE says otherwise.
#: A room of phones each inside its own limit would otherwise multiply it.
ASK_TOTAL_PER_MINUTE = 20


def is_local(request: Request) -> bool:
    """Whether the request came from the machine the service runs on."""
    return request.client is not None and request.client.host in LOOPBACK


def open_to_network(arguments: list[str] | None = None, environment: dict | None = None) -> bool | None:
    """
    Whether the service was started to listen beyond its own machine, read from how uvicorn was
    asked to start: ``--host`` on the command line, or ``UVICORN_HOST``. None when it cannot be
    told, as when something else is running the app. It is only for telling a presenter why
    nobody can connect, so a wrong guess costs a sentence, not a result.
    """
    arguments = sys.argv if arguments is None else arguments
    environment = os.environ if environment is None else environment
    if not any("uvicorn" in part for part in arguments[:2]):
        return None
    host = environment.get("UVICORN_HOST", "127.0.0.1")
    for index, part in enumerate(arguments):
        if part == "--host" and index + 1 < len(arguments):
            host = arguments[index + 1]
        elif part.startswith("--host="):
            host = part[len("--host="):]
    return host not in LOOPBACK and host != "localhost"


def site_folder() -> Path | None:
    """Where the built viewer is: VIGIL_SITE, or the repository's root when the built copy is there."""
    named = os.environ.get("VIGIL_SITE", "").strip()
    folder = Path(named) if named else ROOT
    return folder if (folder / "index.html").is_file() else None


# ----- The Detective's limit -----


class AskLimiter:
    """
    Counts questions over the last minute: each device's own, and every other device's together.
    The machine the service runs on is never limited and never counted.
    """

    def __init__(self, per_minute: int | None = None, total_per_minute: int | None = None):
        self.per_minute = per_minute if per_minute is not None else int(os.environ.get("VIGIL_ASK_PER_MINUTE", ASK_PER_MINUTE))
        self.total_per_minute = (total_per_minute if total_per_minute is not None
                                 else int(os.environ.get("VIGIL_ASK_TOTAL_PER_MINUTE", ASK_TOTAL_PER_MINUTE)))
        self.asked: dict[str, list[float]] = {}
        self.lock = threading.Lock()

    def allow(self, request: Request, now: float | None = None) -> bool:
        if is_local(request):
            return True
        device = request.client.host if request.client else "unknown"
        now = time.monotonic() if now is None else now
        with self.lock:
            # Forget what has aged out, and the devices that have nothing left, so the record cannot grow without end.
            self.asked = {name: kept for name, times in self.asked.items() if (kept := [at for at in times if now - at < 60])}
            recent = self.asked.get(device, [])
            everyone = sum(len(times) for times in self.asked.values())
            if len(recent) >= self.per_minute or everyone >= self.total_per_minute:
                return False
            self.asked[device] = recent + [now]
            return True


# ----- Presenting -----


class ZoneShown(BaseModel):
    name: str = Field(min_length=1, max_length=80)
    #: [latitude, longitude] points, as in "A zone".
    polygon: list[tuple[float, float]] = Field(min_length=3, max_length=1000)


class ShowRequest(BaseModel):
    presenting: bool = True
    finding_id: str | None = None
    variable: str | None = None
    zone: ZoneShown | None = None


class Presenting:
    """What the presenter has on show. Held in memory: it is gone when the service stops."""

    def __init__(self):
        self.presenting = False
        self.sequence = 0
        self.show: dict | None = None
        self.lock = threading.Lock()

    def set(self, show: dict | None) -> None:
        with self.lock:
            if show == self.show and self.presenting == (show is not None):
                return                                   # the same thing again is not news
            self.presenting = show is not None
            self.show = show
            self.sequence += 1

    def answer(self, run_id: str | None) -> dict:
        with self.lock:
            return {"presenting": self.presenting, "run_id": run_id, "sequence": self.sequence, "show": self.show,
                    "open_to_network": open_to_network()}


# ----- The routes -----


def add_site(app: FastAPI, held, folder: Path | None, years_text=None) -> None:
    """
    Adds the routes above to the service. ``held`` gives the results a request is answered from,
    and ``years_text`` a variable year by year as JSON text, as ``GET /years`` answers it.
    """
    presenting = Presenting()
    compressed: dict[int, bytes] = {}

    def run_id(request: Request) -> str | None:
        run = held(request).run
        return run["id"] if run else None

    @app.get("/present")
    def present(request: Request):
        return presenting.answer(run_id(request))

    @app.post("/present")
    def show(request: Request, body: ShowRequest):
        if not is_local(request):
            raise HTTPException(403, "Only the presenter's own machine can change what is on show.")
        r = held(request)
        if not body.presenting:
            presenting.set(None)
            return presenting.answer(run_id(request))
        if body.finding_id is not None and body.finding_id not in r.store.by_id:
            raise HTTPException(422, f"There is no finding with the id {body.finding_id}.")
        if body.variable is not None and body.variable not in r.grids:
            raise HTTPException(422, f"There is no globe layer for a variable called {body.variable}.")
        zone = None
        if body.zone is not None:
            zone = {"name": body.zone.name, "polygon": [[latitude, longitude] for latitude, longitude in body.zone.polygon]}
        presenting.set({"finding_id": body.finding_id, "variable": body.variable, "zone": zone})
        return presenting.answer(run_id(request))

    @app.get("/snapshot.json", include_in_schema=False)
    def snapshot_file(request: Request):
        # The page asks for this beside itself. It gets the scan the service holds, so the page, the
        # Detective and a zone's figures are always about the same run. A scan is some 20 MB as text
        # and a tenth of that compressed, so it is compressed once and kept.
        r = held(request)
        if "gzip" not in request.headers.get("accept-encoding", ""):
            return Response(json.dumps(r.document, allow_nan=False), media_type="application/json")
        key = id(r.document)
        if key not in compressed:
            compressed.clear()
            compressed[key] = gzip.compress(json.dumps(r.document, allow_nan=False, separators=(",", ":")).encode("utf-8"), compresslevel=6)
        return Response(compressed[key], media_type="application/json",
                        headers={"Content-Encoding": "gzip", "Vary": "Accept-Encoding", "Cache-Control": "no-cache"})

    @app.get("/ask.json", include_in_schema=False)
    def ask_file():
        if not ASK_FIXTURE.is_file():
            raise HTTPException(404, "There are no example questions on this server.")
        return FileResponse(ASK_FIXTURE, media_type="application/json")

    @app.get("/series/{variable}/{name}", include_in_schema=False)
    def series_file(request: Request, variable: str, name: str):
        r = held(request)
        match = SERIES_FILE.match(name)
        if not match:
            raise HTTPException(404, "A series file is named after its cell, as in r49c114.json.")
        cell_id = match[1]
        series = r.series.get((cell_id, variable))
        if series is None and r.scan_data is not None and variable in r.scan_data.data:
            from vigil.analysis.scan import cell_object

            row, column = (int(part) for part in re.findall(r"\d+", cell_id))
            grid = r.scan_data.grid
            if row < grid["rows"] and column < grid["columns"]:
                series = r.series_for(cell_object(row, column, grid), variable)
        if series is None:
            raise HTTPException(404, f"There is no series for {variable} in {cell_id} on this server.")
        return Response(json.dumps({**r.head, "run_id": run_id(request), "series": [series]}, allow_nan=False),
                        media_type="application/json")

    @app.get("/years/{name}", include_in_schema=False)
    def years_file(request: Request, name: str):
        # The viewer looks for years/<variable>.json beside its page before it asks the service anything.
        if years_text is None or not name.endswith(".json"):
            raise HTTPException(404, "A year-by-year file is named after its variable, as in precipitation.json.")
        try:
            return Response(years_text(request, name[:-len(".json")]), media_type="application/json")
        except HTTPException as problem:
            # To a page asking for a file, a file the service cannot make is a file that is not there.
            raise HTTPException(404, problem.detail) from problem

    if folder is not None:
        index = folder / "index.html"

        @app.get("/", include_in_schema=False)
        def page():
            # The page names its script by a hash that changes with every build, so it must not be kept.
            return FileResponse(index, media_type="text/html", headers={"Cache-Control": "no-cache"})

        if (folder / "viewer").is_dir():
            app.mount("/viewer", StaticFiles(directory=folder / "viewer"), name="viewer")
