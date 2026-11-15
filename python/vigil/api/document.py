"""From gridded data to a results document in the contract's format.

This is the glue between the analysis and anything that serves or stores results. It makes
the four analysis calls in order, adds the descriptive text that comes from the data sources
and the run (the analysis leaves it out on purpose), builds the series for the evidence
view, and writes the run record. It computes no statistics of its own.

    from vigil.api.document import ScanData, full_scan, analyse_region

    scan_data = ScanData.load("scan-data.npz")      # what the pipeline hands over, see ScanData
    document = full_scan(scan_data)                 # the stored scan: GET /snapshot
    region = analyse_region(scan_data, lat_min=..., lat_max=..., lon_min=..., lon_max=...)

``python -m vigil.api.document scan-data.npz snapshot.json`` runs the full scan and writes
the file. Check it with ``python contract/tools/check.py snapshot.json`` before anyone uses it.

The steps and the wording follow Will's note on using the analysis. Field meanings are in
``contract/README.md``.
"""

from __future__ import annotations

import json
import math
import subprocess
import time
from collections import Counter
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path

import numpy as np

from vigil.analysis import cascades, change_points
from vigil.analysis.cascades import add_cascades
from vigil.analysis.change_points import add_change_points
from vigil.analysis.contradictions import add_contradictions
from vigil.analysis.scan import scan_trends
from vigil.analysis.score import WEIGHTS
from vigil.analysis.trend import deseasonalise

CONTRACT_VERSION = "0.1"

# Fixed before the scan and written into run.settings, so the file says what the scan used.
MIN_COVERAGE = 0.9
THRESHOLD = 0.05
CI_LEVEL = 0.95
MAX_LAG_MONTHS = 6
RANK_CAP = {"top_n": 20, "max_share": 0.25}

#: The most cells POST /analyse will take on, so a request finishes while the caller waits.
#: The full scan of about 2,300 land cells takes a minute and a half.
MAX_REGION_CELLS = 400

#: How many findings, from the top of the ranking, a stored document carries series for.
#: The rest are built on request from the arrays. Dan's open question in the contract.
SERIES_FOR_TOP = 50

EXPLORATORY = "The finding is exploratory and has not been re-tested."

#: The full variable objects. The first four are the contract fixture's.
VARIABLES = {
    "temperature": {
        "id": "temperature", "name": "Air temperature at 2 m", "units": "K",
        "mission": "MERRA-2", "product": "M2TMNXSLV", "field": "T2M",
        "source_url": "https://search.earthdata.nasa.gov/search?q=M2TMNXSLV",
        "native_resolution": "0.5 by 0.625 degrees, monthly",
    },
    "precipitation": {
        "id": "precipitation", "name": "Precipitation", "units": "mm per month",
        "mission": "GPM IMERG", "product": "GPM_3IMERGM", "field": "precipitation",
        "source_url": "https://search.earthdata.nasa.gov/search?q=GPM_3IMERGM",
        "native_resolution": "0.1 degrees, monthly",
    },
    "soil_moisture": {
        "id": "soil_moisture", "name": "Soil moisture, 0 to 10 cm", "units": "kg per m2",
        "mission": "GLDAS Noah", "product": "GLDAS_NOAH025_M", "field": "SoilMoi0_10cm_inst",
        "source_url": "https://search.earthdata.nasa.gov/search?q=GLDAS_NOAH025_M",
        "native_resolution": "0.25 degrees, monthly",
    },
    "vegetation": {
        "id": "vegetation", "name": "Vegetation (NDVI)", "units": "NDVI",
        "mission": "Terra MODIS", "product": "MOD13C2", "field": "CMG 0.05 Deg Monthly NDVI",
        "source_url": "https://search.earthdata.nasa.gov/search?q=MOD13C2",
        "native_resolution": "0.05 degrees, monthly",
    },
    "snow_cover": {
        "id": "snow_cover", "name": "Snow cover", "units": "percent",
        "mission": "Terra MODIS", "product": "MOD10CM", "field": "Snow_Cover_Monthly_CMG",
        "source_url": "https://search.earthdata.nasa.gov/search?q=MOD10CM",
        "native_resolution": "0.05 degrees, monthly",
    },
    "sea_surface_temperature": {
        "id": "sea_surface_temperature", "name": "Sea surface temperature", "units": "°C",
        "mission": "Aqua MODIS", "product": "MODISA_L3m_SST", "field": "sst",
        "source_url": "https://search.earthdata.nasa.gov/search?q=MODISA_L3m_SST",
        "native_resolution": "9 km, monthly",
    },
    "chlorophyll": {
        "id": "chlorophyll", "name": "Chlorophyll-a at the sea surface", "units": "mg per m3",
        "mission": "Aqua MODIS", "product": "MODISA_L3m_CHL", "field": "chlor_a",
        "source_url": "https://search.earthdata.nasa.gov/search?q=MODISA_L3m_CHL",
        "native_resolution": "9 km, monthly",
    },
    "earthquakes": {
        "id": "earthquakes", "name": "Earthquakes, magnitude 4.5 and above", "units": "events per month",
        "mission": "USGS ComCat", "product": "USGS earthquake catalogue", "field": "count",
        "source_url": "https://earthquake.usgs.gov/fdsnws/event/1/",
        "native_resolution": "point events, binned to the scan grid and to months",
    },
}

# One line about each source, for a finding's limitations.
SOURCE_NOTES = {
    "temperature": "MERRA-2 is a reanalysis, not a direct satellite measurement.",
    "precipitation": "GPM IMERG is a satellite-based estimate.",
    "soil_moisture": "GLDAS is land-surface model output.",
    "vegetation": "MODIS NDVI is a satellite index that cloud and sensor changes can affect.",
    "snow_cover": "MODIS sees snow only in daylight and clear sky. Months of polar night are missing, so cells in the far "
                  "north and south are mostly left out, and cloud can hide snow that is there. Where snow is rare, a few "
                  "misread pixels can make a trend out of almost nothing, so read the size of the change before its p-value.",
    "sea_surface_temperature": "Aqua MODIS measures the temperature of the sea's skin in daytime and clear sky. Its record "
                               "starts in August 2002, and persistent cloud or sea ice leaves gaps. Aqua's orbit has drifted "
                               "in recent years, which changes the time of day it measures and may affect the last years of the record.",
    "chlorophyll": "Chlorophyll is estimated from the colour of the sea, which sediment and dissolved matter also change "
                   "near coasts. The record starts in July 2002, and there is none in polar winter or under cloud. The sensor "
                   "has aged over the record, and a slow drift in its calibration would look like a slow change in the sea.",
}

#: The raw units of every variable, for the analysis, which knows only the first five by itself.
UNITS = {variable: described["units"] for variable, described in VARIABLES.items()}

EARTHQUAKE_NOTES = [
    "The catalogue grows more complete with time, so a rising count can reflect better detection.",
    "Earthquakes cluster: one large event with its aftershocks can produce a trend or a step. No declustering is applied.",
    "The result is a statement about the count of magnitude 4.5 and above events, and says nothing about cause or prediction.",
]

DEFAULT_REGRIDDING = "Area-weighted mean onto the scan grid"
MISSING_DATA_RULE = (
    f"A cell is scanned when at least {MIN_COVERAGE:.0%} of months are present. "
    "Missing months are left out of the test."
)


# ----- What the pipeline hands over -----


@dataclass
class ScanData:
    """The gridded monthly data for a scan, as the pipeline hands it over.

    ``data[variable]`` is a float array of shape ``(rows, columns, months)``: row 0 the
    southernmost, column 0 the westernmost, months oldest first with none left out, NaN for a
    missing value. ``mask`` is ``None``, one ``(rows, columns)`` array, or a dict from
    variable id to such an array; an entry is ``None`` to test the cell, or ``"ocean"`` or
    ``"ice"``. See ``vigil/analysis/scan.py`` for the full rules.

    On disk it is one ``.npz`` file (``numpy.savez_compressed``) holding:

    * one array per variable, under the variable's id;
    * ``meta``: a JSON string with ``period_start`` (``"2001-01-01"``), ``grid``
      (``cell_degrees``, ``rows``, ``columns``, ``lat_min``, ``lon_min``) and optionally
      ``regridding`` (the rule used, in words) and ``synthetic`` (true for made-up data);
    * optionally ``mask``, a ``(rows, columns)`` array of strings, ``""`` to test the cell,
      used for every variable, or ``mask.<variable>`` entries, one per masked variable.
      Not both: the analysis does not apply a shared mask to earthquakes, which happen at sea.
    """

    data: dict[str, np.ndarray]
    grid: dict
    period_start: str
    mask: object = None
    regridding: str = DEFAULT_REGRIDDING
    synthetic: bool = False
    dates: list[str] = field(init=False)
    start_month: int = field(init=False)

    def __post_init__(self):
        if not self.data:
            raise ValueError("no variables in the scan data")
        shapes = {variable: array.shape for variable, array in self.data.items()}
        first = next(iter(shapes.values()))
        if len(first) != 3 or any(shape != first for shape in shapes.values()):
            raise ValueError(f"every variable must have the same (rows, columns, months) shape; got {shapes}")
        if first[:2] != (self.grid["rows"], self.grid["columns"]):
            raise ValueError(f"the data are {first[0]} x {first[1]} cells but the grid says {self.grid['rows']} x {self.grid['columns']}")
        start = datetime.strptime(self.period_start, "%Y-%m-%d")
        if start.day != 1:
            raise ValueError("period_start must be the first day of a month")
        self.start_month = start.month
        self.dates = month_dates(self.period_start, first[2])

    @property
    def months(self) -> int:
        return len(self.dates)

    @classmethod
    def load(cls, path) -> "ScanData":
        with np.load(path, allow_pickle=False) as stored:
            if "meta" not in stored:
                raise ValueError(f"{path}: no 'meta' entry. See ScanData for the file's layout.")
            meta = json.loads(str(stored["meta"]))
            data, own_masks, shared_mask = {}, {}, None
            for name in stored.files:
                if name == "meta":
                    continue
                if name == "mask":
                    shared_mask = _mask_from_strings(stored[name])
                elif name.startswith("mask."):
                    own_masks[name[len("mask."):]] = _mask_from_strings(stored[name])
                else:
                    data[name] = np.asarray(stored[name], dtype=float)
        if own_masks and shared_mask is not None:
            # The analysis treats one mask for every variable differently from a mask per
            # variable (a shared mask is not applied to earthquakes), so the file must pick one.
            raise ValueError(f"{path}: give either 'mask' or 'mask.<variable>' entries, not both")
        mask = own_masks or shared_mask
        return cls(data=data, grid=meta["grid"], period_start=meta["period_start"], mask=mask,
                   regridding=meta.get("regridding", DEFAULT_REGRIDDING), synthetic=bool(meta.get("synthetic", False)))

    def save(self, path) -> None:
        """Writes the file ``load`` reads. Mainly for tests and for trying the service on made-up data."""
        arrays = dict(self.data)
        arrays["meta"] = np.array(json.dumps({
            "period_start": self.period_start, "grid": self.grid,
            "regridding": self.regridding, "synthetic": self.synthetic,
        }))
        if isinstance(self.mask, dict):
            for variable, one in self.mask.items():
                if one is not None:
                    arrays[f"mask.{variable}"] = _mask_to_strings(one)
        elif self.mask is not None:
            arrays["mask"] = _mask_to_strings(self.mask)
        np.savez_compressed(path, **arrays)


def _mask_from_strings(stored: np.ndarray) -> np.ndarray:
    mask = np.empty(stored.shape, dtype=object)
    for index, value in np.ndenumerate(stored):
        mask[index] = str(value) or None
    return mask


def _mask_to_strings(mask) -> np.ndarray:
    return np.array([[cell or "" for cell in row] for row in np.asarray(mask, dtype=object)], dtype=str)


def month_dates(period_start: str, months: int) -> list[str]:
    """One ``YYYY-MM-01`` string per month, in order, from ``period_start``."""
    start = datetime.strptime(period_start, "%Y-%m-%d")
    first = start.year * 12 + start.month - 1
    return [f"{(first + i) // 12:04d}-{(first + i) % 12 + 1:02d}-01" for i in range(months)]


# ----- The text the analysis leaves to us -----


def summary(finding: dict) -> str:
    """One line for the findings list. Never says one thing causes another."""
    names = [variable["name"] for variable in finding["variables"]]
    kind = finding["kind"]
    if kind == "trend":
        result = finding["result"]
        if result["direction"] == "none":
            return f"{names[0]}: no significant trend"
        return f"{names[0]}: {result['direction']} at {result['slope_per_decade']:+.3g} {result['units']}"
    if kind == "contradiction":
        paired = finding["contradiction"]["paired_cell"]["id"]
        return f"{names[0]}: opposite trends in neighbouring cells {finding['cell']['id']} and {paired}"
    if kind == "change_point":
        step = finding["change_point"]
        return f"{names[0]}: level shifts by {step['shift']:+.3g} {step['units']} around {step['date'][:7]}"
    cascade = finding["cascade"]
    plural = "" if cascade["lag_months"] == 1 else "s"
    return (f"{names[1]} follows {names[0].lower()} by {cascade['lag_months']} month{plural} "
            f"(r = {cascade['correlation']:+.2f}). An association, not a cause.")


def anomaly_text(finding: dict) -> str:
    variables = [variable["id"] for variable in finding["variables"]]
    if "earthquakes" in variables:
        return "Annual means of monthly counts, no seasonal removal"
    text = "Monthly climatology of the same cell over the period, subtracted"
    return text + ", then each series' own least-squares line" if finding["kind"] == "cascade" else text


def limitations_for(finding: dict, max_lag_months: int = MAX_LAG_MONTHS) -> list[str]:
    variables = [variable["id"] for variable in finding["variables"]]
    kind = finding["kind"]
    notes: list[str] = []
    if kind == "cascade":
        notes += cascades.limitations(max_lag_months)
    elif kind == "change_point":
        notes += change_points.limitations()
    elif kind == "contradiction":
        notes.append("Neighbouring cells can differ for reasons the scan cannot see. The confounders listed are some of them.")
    for variable in variables:
        if variable == "earthquakes":
            notes += EARTHQUAKE_NOTES
        elif variable in SOURCE_NOTES:
            notes.append(SOURCE_NOTES[variable])
    notes.append(EXPLORATORY)
    return list(dict.fromkeys(notes))       # a cascade's two sources can share a note


def describe(findings: list[dict], dates: list[str], regridding: str, max_lag_months: int = MAX_LAG_MONTHS) -> None:
    """Adds, in place, everything a finding needs that the analysis does not write."""
    period = {"start": dates[0], "end": dates[-1], "step": "monthly"}
    for finding in findings:
        for variable in finding["variables"]:
            if variable["id"] not in VARIABLES:
                raise ValueError(f"{finding['id']}: no source is recorded for the variable {variable['id']!r}")
        finding["variables"] = [dict(VARIABLES[variable["id"]]) for variable in finding["variables"]]
        finding["period"] = dict(period)
        finding["summary"] = summary(finding)
        finding["preprocessing"].update({
            "regridding": regridding,
            "aggregation": "Monthly count" if finding["variables"][0]["id"] == "earthquakes" else "Monthly mean",
            "anomaly": anomaly_text(finding),
            "missing_data_rule": MISSING_DATA_RULE,
        })
        finding.setdefault("links", [])     # the analysis has already written the links: never wipe them
        finding["standing"] = {"status": "exploratory", "limitations": limitations_for(finding, max_lag_months)}


# ----- Series for the evidence view -----


def _clean(values) -> list:
    return [None if not np.isfinite(value) else float(value) for value in values]


def make_series(data: dict, cell: dict, variable: str, dates: list[str], start_month: int,
                offset: tuple[int, int] = (0, 0)) -> dict:
    """The contract's series for one cell and variable. ``offset`` is where ``data`` starts on the full grid."""
    raw = data[variable][cell["row"] - offset[0], cell["column"] - offset[1]]
    # deseasonalise is the analysis's own, because the checker rebuilds each anomaly from its raw values.
    return {"cell_id": cell["id"], "variable": variable, "units": VARIABLES[variable]["units"],
            "dates": dates, "raw": _clean(raw), "anomaly": _clean(deseasonalise(raw, start_month))}


def cells_and_variables(finding: dict) -> list[tuple[dict, str]]:
    """Every (cell, variable) a finding's evidence view draws."""
    needed = [(finding["cell"], variable["id"]) for variable in finding["variables"]]
    if finding["kind"] == "contradiction":
        needed.append((finding["contradiction"]["paired_cell"], finding["contradiction"]["variable"]))
    return needed


def series_for(findings: list[dict], data: dict, dates: list[str], start_month: int,
               offset: tuple[int, int] = (0, 0)) -> list[dict]:
    """One series per cell and variable the findings name, without repeats."""
    made: dict[tuple[str, str], dict] = {}
    for finding in findings:
        for cell, variable in cells_and_variables(finding):
            key = (cell["id"], variable)
            if key not in made:
                made[key] = make_series(data, cell, variable, dates, start_month, offset)
    return list(made.values())


# ----- The run record -----


def code_version() -> str:
    """The git commit the code is at, or "unknown" where there is no repository (a deployed copy)."""
    try:
        done = subprocess.run(["git", "rev-parse", "--short", "HEAD"], cwd=Path(__file__).parent,
                              capture_output=True, text=True, timeout=5)
        return done.stdout.strip() or "unknown"
    except (OSError, subprocess.SubprocessError):
        return "unknown"


def _timestamp(moment: float) -> str:
    return datetime.fromtimestamp(moment, timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def run_record(scan_data: ScanData, counts: dict, findings: list[dict], dates: list[str], started: float,
               region: dict | None = None, rank_cap: dict | None = RANK_CAP) -> dict:
    finished = time.time()
    settings = {
        "grid": dict(scan_data.grid),
        "period": {"start": dates[0], "end": dates[-1], "step": "monthly"},
        "variables": list(scan_data.data),
        "min_coverage": MIN_COVERAGE,
        "threshold": THRESHOLD,
        "multiple_testing": "benjamini_hochberg",
        "ci_level": CI_LEVEL,
        "max_lag_months": MAX_LAG_MONTHS,
        "score_weights": dict(WEIGHTS),
    }
    if region is not None:
        settings["region"] = region
    if rank_cap is not None:
        settings["rank_cap"] = dict(rank_cap)
    prefix = "region" if region is not None else "scan"
    return {
        "id": f"{prefix}-{datetime.fromtimestamp(started, timezone.utc):%Y%m%dT%H%M%SZ}",
        "started": _timestamp(started),
        "finished": _timestamp(finished),
        "duration_seconds": round(finished - started, 3),
        "code_version": code_version(),
        "settings": settings,
        # Only the scan's counts are tests in the run record's sense: one variable in one cell.
        # The other kinds of finding add to the findings counts and nothing else.
        "counts": {**counts, "findings_total": len(findings),
                   "findings_by_kind": dict(Counter(finding["kind"] for finding in findings))},
    }


# ----- The two runs -----


def _four_calls(data: dict, mask, grid: dict, start_month: int, period_start: str,
                offset: tuple[int, int] = (0, 0), include_not_significant: bool = False):
    """The analysis, in the order it must run. Returns the scan and the final, ranked findings."""
    shared = dict(start_month=start_month, grid=grid, mask=mask, min_coverage=MIN_COVERAGE,
                  threshold=THRESHOLD, ci_level=CI_LEVEL, rank_cap=RANK_CAP, cell_offset=offset, units=UNITS)
    scan = scan_trends(data, include_not_significant=include_not_significant, **shared)
    step2 = add_contradictions(scan.findings, scan.evidence, grid=grid, threshold=THRESHOLD,
                               rank_cap=RANK_CAP, cell_offset=offset)
    step3 = add_cascades(data, step2.findings, step2.evidence, max_lag_months=MAX_LAG_MONTHS, **shared)
    step4 = add_change_points(data, step3.findings, step3.evidence, period_start=period_start, **shared)
    return scan, step4.findings


def full_scan(scan_data: ScanData, series_for_top: int = SERIES_FOR_TOP) -> dict:
    """The stored scan: every cell of the grid, as one results document with every part."""
    started = time.time()
    scan, findings = _four_calls(scan_data.data, scan_data.mask, scan_data.grid,
                                 scan_data.start_month, scan_data.period_start)
    describe(findings, scan_data.dates, scan_data.regridding)
    return {
        "contract_version": CONTRACT_VERSION,
        "synthetic": scan_data.synthetic,
        "run": run_record(scan_data, scan.counts, findings, scan_data.dates, started),
        "variables": [dict(VARIABLES[variable]) for variable in scan_data.data],
        "findings": findings,
        "series": series_for(findings[:series_for_top], scan_data.data, scan_data.dates, scan_data.start_month),
        "grids": scan.grids,
    }


class RegionError(ValueError):
    """The region or period asked for cannot be analysed. The message is fit to show the user."""


def _slice_mask(mask, rows: slice, columns: slice):
    if mask is None:
        return None
    if isinstance(mask, dict):
        return {variable: None if one is None else np.asarray(one, dtype=object)[rows, columns]
                for variable, one in mask.items()}
    return np.asarray(mask, dtype=object)[rows, columns]


def analyse_region(scan_data: ScanData, lat_min: float, lat_max: float, lon_min: float, lon_max: float,
                   start: str | None = None, end: str | None = None) -> dict:
    """``POST /analyse``: the same analysis on a block of cells and a span of months.

    ``start`` and ``end`` are months as ``YYYY-MM-01``, both included; left out, the whole
    period. Cell ids stay the full grid's. Corrections for multiple testing are made over the
    region's own tests, so a result here can differ from the same cell in the full scan.
    Findings that did not pass the threshold are returned too, marked ``significant: false``.
    """
    started = time.time()
    grid = scan_data.grid
    if not (-90 <= lat_min < lat_max <= 90):
        raise RegionError("lat_min must be below lat_max, both between -90 and 90.")
    if not (-180 <= lon_min <= 180 and -180 <= lon_max <= 180):
        raise RegionError("Longitudes must be between -180 and 180.")
    if lon_min >= lon_max:
        raise RegionError("A region that crosses the date line (lon_min above lon_max) is not supported.")

    size = grid["cell_degrees"]
    row0 = max(0, math.floor((lat_min - grid["lat_min"]) / size))
    row1 = min(grid["rows"], math.ceil((lat_max - grid["lat_min"]) / size))
    column0 = max(0, math.floor((lon_min - grid["lon_min"]) / size))
    column1 = min(grid["columns"], math.ceil((lon_max - grid["lon_min"]) / size))
    if row1 <= row0 or column1 <= column0:
        raise RegionError("The region does not cover any cell of the scan grid.")
    cells = (row1 - row0) * (column1 - column0)
    if cells > MAX_REGION_CELLS:
        raise RegionError(f"The region covers {cells} cells of the scan grid. The most one request can analyse is "
                          f"{MAX_REGION_CELLS}: choose a smaller area.")

    dates = scan_data.dates
    try:
        month0 = 0 if start is None else dates.index(start)
        month1 = len(dates) if end is None else dates.index(end) + 1
    except ValueError:
        raise RegionError(f"start and end must be months between {dates[0]} and {dates[-1]}, written YYYY-MM-01.") from None
    if month1 <= month0:
        raise RegionError("start must not be after end.")

    rows, columns = slice(row0, row1), slice(column0, column1)
    data = {variable: array[rows, columns, month0:month1] for variable, array in scan_data.data.items()}
    region_grid = {**grid, "rows": row1 - row0, "columns": column1 - column0,
                   "lat_min": grid["lat_min"] + row0 * size, "lon_min": grid["lon_min"] + column0 * size}
    region_dates = dates[month0:month1]
    start_month = (scan_data.start_month - 1 + month0) % 12 + 1
    offset = (row0, column0)

    try:
        scan, findings = _four_calls(data, _slice_mask(scan_data.mask, rows, columns), region_grid, start_month,
                                     region_dates[0], offset, include_not_significant=True)
    except ValueError as problem:
        # Too small a region or too short a period for one of the tests. The analysis says which.
        raise RegionError(str(problem)) from problem

    describe(findings, region_dates, scan_data.regridding)
    region = {"row": row0, "column": column0, "rows": row1 - row0, "columns": column1 - column0}
    return {
        "contract_version": CONTRACT_VERSION,
        "synthetic": scan_data.synthetic,
        # settings.grid stays the full grid so the ids resolve; settings.region says what was covered.
        "run": run_record(scan_data, scan.counts, findings, region_dates, started, region=region),
        "variables": [dict(VARIABLES[variable]) for variable in scan_data.data],
        "findings": findings,
        # Findings that did not pass are returned too, so there can be many: series for the top of the list only.
        "series": series_for(findings[:SERIES_FOR_TOP], data, region_dates, start_month, offset),
    }


def write(document: dict, path) -> None:
    """Writes the document. A stray NaN fails here, where it can be fixed, not in the app."""
    Path(path).write_text(json.dumps(document, allow_nan=False), encoding="utf-8")


if __name__ == "__main__":
    import sys

    if len(sys.argv) != 3:
        sys.exit("usage: python -m vigil.api.document <scan-data.npz> <snapshot.json>")
    loaded = ScanData.load(sys.argv[1])
    print(f"Scanning {len(loaded.data)} variables on {loaded.grid['rows']} x {loaded.grid['columns']} cells, "
          f"{loaded.months} months from {loaded.dates[0]}. This takes a few minutes.")
    result = full_scan(loaded)
    write(result, sys.argv[2])
    print(f"Wrote {sys.argv[2]}: {len(result['findings'])} findings, {len(result['series'])} series. "
          "Now run contract/tools/check.py on it.")
