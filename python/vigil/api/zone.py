"""A zone drawn by hand: its cells, one series per variable over them, and the trend in each.

    from vigil.api.zone import analyse_zone

    answer = analyse_zone(scan_data, layers, run, polygon=[[-5, -70], [-5, -50], [-15, -50], [-15, -70]])

This is ``POST /zone``. The app sends the outline a user drew on the globe and gets back,
for each variable, the single series that stands for the whole zone and the result of the
scan's own trend test on it. The format is in ``contract/README.md`` under "A zone".

Like the rest of this folder it computes no statistics. It gathers the zone's cells into one
series per variable, hands those series to ``scan_trends`` as if the zone were one cell, and
keeps the blocks that describe the test. So a zone is tested exactly as a cell is: the same
anomalies, the same Mann-Kendall test and Sen's slope, and for earthquakes the same annual
means. A zone of one scanned cell gives that cell's own slope, interval and p-value.

The geometry
------------
The outline is a list of ``[latitude, longitude]`` points, closed from the last back to the
first. Its edges are straight lines in latitude and longitude. Longitudes are continuous
along it, so an outline that crosses the date line runs past 180 instead of jumping to
-180. A grid cell belongs to the zone when the cell's centre is inside the outline (even-odd
rule). The desktop app follows the same rule in ``Data/ZoneGeometry.cs``, and the answer
lists the cells, so the picture and the numbers can be checked against each other.

From cells to one series
------------------------
*Measured variables* (temperature, precipitation, soil moisture, vegetation): the cells
used are the zone's cells that the stored scan tested for that variable, read from the
scan's own layer, so a cell that was ocean, ice or too thin on data there is left out here
too. A month's value is the mean of those cells weighted by their area. The same cells are
used throughout: a month in which the cells holding a value make up less than
``MIN_COVERAGE`` of that area is left missing, not averaged from what is there.

*Count variables* (earthquakes): the events of every cell in the zone are added up, month
by month. A quiet cell's few events still count towards the zone.
"""

from __future__ import annotations

import math

import numpy as np

from vigil.analysis.scan import UNMASKED_BY_DEFAULT, cell_id, scan_trends
from vigil.analysis.trend import deseasonalise
from vigil.api.document import (CI_LEVEL, CONTRACT_VERSION, EXPLORATORY, MIN_COVERAGE, THRESHOLD, UNITS, VARIABLES,
                                ScanData, _clean, describe)

EARTH_RADIUS_KM = 6371.0088

#: The most points an outline may have. The app thins a drawn stroke well below this.
MAX_POINTS = 1000

#: Said of every zone result. A person chose the area, usually after looking at the map.
CHOSEN_BY_EYE = ("The zone was drawn by hand, most likely after looking at the map, so its p-value "
                 "overstates the evidence. It is a description of the area, not a test planned in advance.")
ONE_TEST = ("One test was made per variable for this zone, so no correction for many tests applies: "
            "the adjusted p-value equals the raw one.")
ZONE_EXPLORATORY = "The result is exploratory and has not been re-tested."

#: Under this mean cover, in percent, a zone has so little snow that a trend in it is a trend in a
#: handful of detections. The level was set on the Sunday morning, after a zone over the Amazon
#: reported a snow trend with a small p-value. It hides nothing: the result is still returned.
RARE_SNOW_PERCENT = 1.0
RARE_SNOW = "Snow is rare here"
RARE_SNOW_NOTE = ("Snow is rare over this zone: its mean cover over the period is under 1%. A trend this small "
                  "can come from a few pixels of cloud read as snow, so it is not evidence that the snow has changed.")


class ZoneError(ValueError):
    """The outline cannot be analysed. The message is fit to show the user."""


# ----- Geometry -----


def _outline(polygon) -> np.ndarray:
    try:
        points = np.asarray(polygon, dtype=float)
    except (TypeError, ValueError):
        raise ZoneError("polygon must be a list of [latitude, longitude] points.") from None
    if points.ndim != 2 or points.shape[1] != 2 or len(points) < 3:
        raise ZoneError("polygon must be a list of at least three [latitude, longitude] points.")
    if len(points) > MAX_POINTS:
        raise ZoneError(f"The outline has {len(points)} points. The most one request can take is {MAX_POINTS}.")
    if not np.isfinite(points).all():
        raise ZoneError("Every point of the outline must be a number.")
    if (np.abs(points[:, 0]) > 90).any():
        raise ZoneError("Latitudes must be between -90 and 90.")
    if (np.abs(points[:, 1]) > 540).any():
        raise ZoneError("Longitudes must be between -540 and 540.")
    longitudes = points[:, 1]
    if abs(longitudes[-1] - longitudes[0]) > 180 or longitudes.max() - longitudes.min() >= 360:
        # Its last point ends a whole turn of longitude from its first: it has no inside.
        raise ZoneError("A zone cannot go all the way round a pole.")
    return points


def inside(points: np.ndarray, latitude: np.ndarray, longitude: np.ndarray) -> np.ndarray:
    """Which of the places given are inside the outline, by the even-odd rule.

    ``latitude`` and ``longitude`` are arrays of the same shape. Longitudes are brought into
    the outline's own turn first, since an outline may run past 180.
    """
    west, east = points[:, 1].min(), points[:, 1].max()
    lon = longitude + 360.0 * np.ceil((west - longitude) / 360.0)
    result = np.zeros(latitude.shape, dtype=bool)
    lat_a, lon_a = points[:, 0], points[:, 1]
    lat_b, lon_b = np.roll(lat_a, 1), np.roll(lon_a, 1)
    for a_lat, a_lon, b_lat, b_lon in zip(lat_a, lon_a, lat_b, lon_b):
        if a_lat == b_lat:
            continue                                # a level edge is never crossed by a level ray
        crosses = (a_lat > latitude) != (b_lat > latitude)
        at = (b_lon - a_lon) * (latitude - a_lat) / (b_lat - a_lat) + a_lon
        result ^= crosses & (lon < at)
    return result & (lon <= east)


def zone_cells(polygon, grid: dict) -> np.ndarray:
    """The grid cells whose centres the outline holds, as a ``(rows, columns)`` array of true and false."""
    points = _outline(polygon)
    size = float(grid["cell_degrees"])
    latitudes = float(grid["lat_min"]) + (np.arange(grid["rows"]) + 0.5) * size
    longitudes = float(grid["lon_min"]) + (np.arange(grid["columns"]) + 0.5) * size
    lat, lon = np.meshgrid(latitudes, longitudes, indexing="ij")
    return inside(points, lat, lon)


def _row_weights(grid: dict) -> np.ndarray:
    """The share of the sphere each row's cells cover: sin of the north edge minus sin of the south edge."""
    size = float(grid["cell_degrees"])
    south = np.radians(float(grid["lat_min"]) + np.arange(grid["rows"]) * size)
    return np.sin(south + math.radians(size)) - np.sin(south)


# ----- From cells to one series -----


def _zone_series(array: np.ndarray, cells: np.ndarray, weights: np.ndarray, counts: bool) -> np.ndarray:
    """One value per month for the cells marked in ``cells``. See the module docstring for the rule."""
    values = array[cells]                                   # (cells, months)
    if counts:
        # A month the catalogue does not cover is NaN in every cell, and stays missing.
        return np.where(np.isfinite(values).all(axis=0), np.nansum(values, axis=0), np.nan)
    weight = np.broadcast_to(weights[:, None], cells.shape)[cells][:, None]     # (cells, 1)
    present = np.isfinite(values)
    held = (weight * present).sum(axis=0)
    total = np.where(present, weight * np.nan_to_num(values), 0.0).sum(axis=0)
    with np.errstate(invalid="ignore", divide="ignore"):
        mean = total / held
    return np.where(held >= MIN_COVERAGE * weight.sum(), mean, np.nan)


def _aggregation_text(variable: str, used: int, counts: bool) -> str:
    cell = "cell" if used == 1 else "cells"
    if counts:
        return f"Monthly count, added up over the {used} grid {cell} of the zone"
    return (f"Monthly mean, averaged over the {used} scanned grid {cell} of the zone, weighted by cell area. "
            f"A month counts when cells holding at least {MIN_COVERAGE:.0%} of that area have a value.")


def _summary_text(name: str, result: dict | None) -> str:
    if result is None:
        return f"{name} over the zone: no trend to report"
    if result["direction"] == "none":
        return f"{name} over the zone: no significant trend"
    return f"{name} over the zone: {result['direction']} at {result['slope_per_decade']:+.3g} {result['units']}"


def caution_for(variable: str, series: np.ndarray) -> tuple[str, str] | None:
    """A reason to distrust a zone's result that belongs beside the number: its short and long form, or None."""
    if variable == "snow_cover" and np.isfinite(series).any() and float(np.nanmean(series)) < RARE_SNOW_PERCENT:
        return RARE_SNOW, RARE_SNOW_NOTE
    return None


def analyse_zone(scan_data: ScanData, layers: dict[str, dict], run: dict | None, polygon) -> dict:
    """``POST /zone``. ``layers`` are the stored scan's globe layers by variable, and ``run`` its run record."""
    grid = scan_data.grid
    cells = zone_cells(polygon, grid)
    rows, columns = np.nonzero(cells)
    if rows.size == 0:
        raise ZoneError("The outline holds no centre of a grid cell. Draw a larger area.")

    size = float(grid["cell_degrees"])
    weights = _row_weights(grid)
    area = float((weights[rows] * math.radians(size)).sum() * EARTH_RADIUS_KM ** 2)

    series: dict[str, np.ndarray] = {}
    summaries: dict[str, dict] = {}
    for variable in scan_data.data:
        counts = variable in UNMASKED_BY_DEFAULT
        skipped: dict[str, int] = {}
        if counts:
            used = cells
        else:
            layer = layers.get(variable)
            if layer is None:
                raise ZoneError(f"The stored scan has no layer for {variable}, so the zone's cells cannot be chosen.")
            reasons = np.array(layer["skip_reason"], dtype=object)
            if reasons.shape != cells.shape:
                raise ZoneError(f"The stored layer for {variable} does not cover the scan grid.")
            used = cells & (reasons == None)                # noqa: E711  (elementwise, on an object array)
            for reason in reasons[cells & (reasons != None)]:   # noqa: E711
                skipped[reason] = skipped.get(reason, 0) + 1
        summaries[variable] = {
            "variable": variable,
            "units": VARIABLES[variable]["units"],
            "cells_used": int(used.sum()),
            "cells_skipped": skipped,
            "tested": False,
            "skip_reason": "no_cells",
            "significant": False,
            "result": None,
            "preprocessing": None,
            "method": None,
            "summary": f"{VARIABLES[variable]['name']} over the zone: none of its cells were scanned for this variable",
            "caution": None,
            "limitations": [],
            "series": None,
        }
        if used.any():
            series[variable] = _zone_series(scan_data.data[variable], used, weights, counts)

    if series:
        # The zone goes to the scan as a grid of one cell, so each variable is tested exactly as a
        # cell is. The cell the scan names is a stand-in and is dropped: a zone has no cell id.
        try:
            scan = scan_trends({variable: one[None, None, :] for variable, one in series.items()},
                               start_month=scan_data.start_month,
                               grid={"cell_degrees": 1.0, "lat_min": 0.0, "lon_min": 0.0},
                               min_coverage=MIN_COVERAGE, threshold=THRESHOLD, ci_level=CI_LEVEL,
                               rank_cap=None, include_not_significant=True, units=UNITS)
        except ValueError as problem:
            raise ZoneError(str(problem)) from problem
        tested = {layer["variable"]: layer["skip_reason"][0][0] is None for layer in scan.grids}
        found = {finding["variables"][0]["id"]: finding for finding in scan.findings}
        describe(list(found.values()), scan_data.dates, scan_data.regridding)

        for variable, one in series.items():
            summary = summaries[variable]
            counts = variable in UNMASKED_BY_DEFAULT
            name = VARIABLES[variable]["name"]
            summary["series"] = {
                "units": summary["units"], "dates": scan_data.dates, "raw": _clean(one),
                "anomaly": _clean(deseasonalise(one, scan_data.start_month)),
            }
            if not tested[variable]:
                summary["skip_reason"] = "coverage"
                summary["summary"] = (f"{name} over the zone: too few events to test" if counts
                                      else f"{name} over the zone: too few months with data to test")
                continue
            summary["tested"] = True
            summary["skip_reason"] = None
            summary["limitations"] = [CHOSEN_BY_EYE, ONE_TEST]
            finding = found.get(variable)
            if finding is None:
                # A flat series, or one whose median slope is exactly zero: tested, with nothing to report.
                summary["summary"] = _summary_text(name, None)
                continue
            finding["preprocessing"]["aggregation"] = _aggregation_text(variable, summary["cells_used"], counts)
            if not counts:
                finding["preprocessing"]["anomaly"] = "Monthly climatology of the zone's series over the period, subtracted"
            summary.update({
                "significant": bool(finding["significant"]),
                "result": finding["result"],
                "preprocessing": finding["preprocessing"],
                "method": finding["method"],
                "summary": _summary_text(name, finding["result"]),
                # The source's own notes follow. The sentence about a finding is put in a zone's words.
                "limitations": [CHOSEN_BY_EYE, ONE_TEST]
                               + [note for note in finding["standing"]["limitations"] if note != EXPLORATORY]
                               + [ZONE_EXPLORATORY],
            })
            caution = caution_for(variable, one)
            if caution:
                summary["caution"] = caution[0]
                summary["limitations"].insert(0, caution[1])

    return {
        "contract_version": CONTRACT_VERSION,
        "synthetic": scan_data.synthetic,
        "run_id": run["id"] if run else None,
        "zone": {
            "polygon": [[float(latitude), float(longitude)] for latitude, longitude in np.asarray(polygon, dtype=float)],
            "cells": [cell_id(int(row), int(column)) for row, column in zip(rows, columns)],
            "cell_count": int(rows.size),
            "area_km2": round(area),
            "lat_min": float(grid["lat_min"]) + int(rows.min()) * size,
            "lat_max": float(grid["lat_min"]) + (int(rows.max()) + 1) * size,
        },
        "period": {"start": scan_data.dates[0], "end": scan_data.dates[-1], "step": "monthly"},
        "variables": [dict(VARIABLES[variable]) for variable in scan_data.data],
        "summaries": list(summaries.values()),
    }
