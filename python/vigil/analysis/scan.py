"""One call from gridded monthly data to ranked trend findings.

This is the entry point the pipeline calls. It runs every trend test of the
scan, applies the false-discovery-rate correction, scores and ranks the
significant findings, and returns the globe layers and the run counts. The
pipeline needs no statistics of its own.

    from vigil.analysis.scan import scan_trends

    result = scan_trends(
        data,                      # {"temperature": array, ...}, see "Input layout"
        start_month=1,             # calendar month of the first value
        grid={"cell_degrees": 2.5, "lat_min": -90.0, "lon_min": -180.0},
        mask=mask,                 # optional, see below
        min_coverage=0.9, threshold=0.05, ci_level=0.95,
        rank_cap={"top_n": 20, "max_share": 0.25},   # or None for no cap
    )
    result.findings   # significant trend findings, scored and in rank order
    result.grids      # one globe layer per variable
    result.counts     # the test counts for the run record

Input layout
------------
``data[variable]`` is a float array of shape ``(rows, columns, months)``:

* axis 0 is the row, counted from the south (row 0 is the southernmost);
* axis 1 is the column, counted from the west (column 0 is the westernmost);
* axis 2 is the month, oldest first, one value per calendar month with none
  left out, so ``months`` is the same for every variable;
* a missing value is NaN. Infinity is refused.

This is the same ``[row][column]`` order as the contract's globe layers, so
``data[v][r, c]`` is the series for cell ``r{r:02d}c{c:03d}``.

``mask`` says which cells are not tested. It is either one 2-D array of shape
``(rows, columns)`` used for every variable, or a dict from variable id to such
an array (a variable missing from the dict is not masked). Each entry is
``None`` (or an empty string) for "test this cell", or ``"ocean"`` or
``"ice"``. A masked cell is skipped whatever its data hold.

How a cell is handled
---------------------
1. Masked: skipped with the mask's reason.
2. Months present / months expected below ``min_coverage`` (or fewer than 10
   months present, which no test can use): skipped, reason ``coverage``.
3. Otherwise ``deseasonalise``, then ``trend_test`` (Mann-Kendall with the
   Hamed-Rao correction, Sen's slope, trend-free pre-whitening cross-check).
4. A series that is flat once the seasonal cycle is removed, such as a desert
   with no rain in any month, has no trend to find and ``trend_test`` cannot run
   on it. It counts as tested, with slope 0 and p = 1. It is never significant.

Then, per variable, ``adjust_by_family`` corrects the p-values for the number of
tests made, and a test is significant when its adjusted p is below
``threshold``. Every significant test becomes a trend finding, so
``len(result.findings) == counts["tests_significant"]``.

Every cell and variable is either tested (slope and p present, skip reason
``None``) or skipped (slope and p ``None``, a skip reason). Never both.

What the pipeline still adds
----------------------------
The findings carry what the analysis owns. These contract fields are left to
the pipeline, because they come from the data source or the run, not from the
statistics:

* ``variables[]``: each entry holds only ``id`` and ``units``. Add ``name``,
  ``mission``, ``product``, ``field``, ``source_url`` and ``native_resolution``.
  (``id`` stays: the ranking reads it.)
* ``period`` (``start``, ``end``, ``step``): the pipeline knows the dates.
* ``summary``: the one-line text.
* ``standing`` (``status`` and ``limitations``).
* ``preprocessing`` text fields: ``regridding``, ``aggregation``, ``anomaly``
  and ``missing_data_rule``. The counts (``months_present``,
  ``months_expected``, ``coverage``) are here.
* ``links``: empty here. Links join findings, and only exist once cascades and
  contradictions are found.

And on the run record: ``id``, ``started``, ``finished``, ``duration_seconds``,
``code_version``, the ``settings`` block (copy ``WEIGHTS`` from ``score.py`` into
``score_weights``), and ``findings_total`` / ``findings_by_kind`` once every kind
of finding has been added. ``counts`` here holds the test counts plus
``cells_total`` and ``cells_scanned``.

``method.parameters`` is flat (strings, numbers, booleans), as the contract asks.
``result.units`` and each layer's ``units`` are the variable's units plus
" per decade", for example ``K per decade`` and ``mm per month per decade``.

Cross-system scores count only the trend findings made here. When cascades and
contradictions are added later, rank the combined list again with
``rank_findings``, passing ``result.evidence`` plus the new findings' own.
"""

from __future__ import annotations

import math
import re
import time
from dataclasses import dataclass
from typing import Callable

import numpy as np

from vigil.analysis.fdr import adjust_by_family
from vigil.analysis.score import Evidence, detrended_sd, rank_findings, trend_robustness
from vigil.analysis.trend import TrendResult, deseasonalise, trend_test

SKIP_REASONS = ("ocean", "ice", "coverage")
MASK_REASONS = ("ocean", "ice")

# Variable ids in the contract, and the units of their raw values.
DEFAULT_UNITS = {
    "temperature": "K",
    "precipitation": "mm per month",
    "soil_moisture": "kg per m2",
    "vegetation": "NDVI",
}

# trend_test needs at least this many non-missing months.
MIN_MONTHS_TO_TEST = 10

# A series whose anomalies vary by less than this share of its largest raw value
# is flat: the variation is rounding error, not data.
FLAT_TOLERANCE = 1e-10

_ID = re.compile(r"^[a-z0-9_]+$")


@dataclass
class ScanResult:
    """Everything one scan produces. ``grids``, ``counts`` and ``findings`` are JSON-ready.

    ``evidence`` maps finding id to the ``Evidence`` the ranking used. Keep it if
    other kinds of finding will be ranked together with these later.
    """

    grids: list[dict]
    counts: dict
    findings: list[dict]
    evidence: dict[str, Evidence]
    elapsed_seconds: float = 0.0
    # Cells that were tested but had no trend to find (flat series). Informational.
    flat_tests: int = 0


@dataclass
class _Outcome:
    """The result of testing one series (before the multiple-testing correction)."""

    p_value: float
    slope_per_decade: float
    # Kept only when the raw p-value is below the threshold: a test can only be
    # significant after correction if it is, so nothing else needs these.
    trend: TrendResult | None = None
    noise_sd: float | None = None
    flat: bool = False


# ------------------------------------------------------------------ one series

def _test_series(series: np.ndarray, start_month: int, threshold: float, ci_level: float) -> _Outcome:
    """Deseasonalise and test one raw monthly series."""
    anomalies = deseasonalise(series, start_month)
    present = anomalies[np.isfinite(anomalies)]
    scale = float(np.max(np.abs(series[np.isfinite(series)])))
    if float(np.ptp(present)) <= FLAT_TOLERANCE * scale:
        return _Outcome(p_value=1.0, slope_per_decade=0.0, flat=True)

    result = trend_test(anomalies, alpha=threshold, ci_level=ci_level)
    if not math.isfinite(result.p_value) or not math.isfinite(result.slope_per_decade):
        raise ValueError("the trend test returned a p-value or slope that is not a number")
    if result.p_value >= threshold:
        return _Outcome(result.p_value, result.slope_per_decade)
    return _Outcome(result.p_value, result.slope_per_decade, result, detrended_sd(anomalies))


# ------------------------------------------------------------------ cells and ids

def cell_id(row: int, column: int) -> str:
    """The contract's cell id, such as ``r60c112``."""
    return f"r{row:02d}c{column:03d}"


def cell_object(row: int, column: int, grid: dict) -> dict:
    """The contract's ``cell`` object: id, row, column and the cell's bounds."""
    deg = float(grid["cell_degrees"])
    lat_min = float(grid["lat_min"]) + row * deg
    lon_min = float(grid["lon_min"]) + column * deg
    return {
        "id": cell_id(row, column),
        "row": int(row),
        "column": int(column),
        "lat_min": round(lat_min, 6),
        "lat_max": round(lat_min + deg, 6),
        "lon_min": round(lon_min, 6),
        "lon_max": round(lon_min + deg, 6),
    }


# ------------------------------------------------------------------ input checks

def _check_inputs(data, start_month, grid, min_coverage, threshold, ci_level, units):
    if not data:
        raise ValueError("data must hold at least one variable")
    shapes = set()
    for variable, array in data.items():
        if not _ID.match(variable):
            raise ValueError(f"variable id {variable!r} must be lower case letters, digits and underscores")
        if array.ndim != 3:
            raise ValueError(f"{variable}: data must have shape (rows, columns, months), got {array.shape}")
        if np.isinf(array).any():
            raise ValueError(f"{variable}: data holds an infinite value; missing values must be NaN")
        shapes.add(array.shape)
        if variable not in units:
            raise ValueError(f"{variable}: no units known; pass units={{'{variable}': ...}}")
    if len(shapes) != 1:
        raise ValueError(f"every variable must have the same shape, got {sorted(shapes)}")
    rows, columns, months = shapes.pop()
    if months < MIN_MONTHS_TO_TEST:
        raise ValueError(f"need at least {MIN_MONTHS_TO_TEST} months, got {months}")
    if not 1 <= start_month <= 12:
        raise ValueError("start_month must be between 1 and 12")
    for key in ("cell_degrees", "lat_min", "lon_min"):
        if key not in grid:
            raise ValueError(f"grid is missing {key!r}")
    if float(grid["cell_degrees"]) <= 0:
        raise ValueError("grid cell_degrees must be positive")
    for key, size in (("rows", rows), ("columns", columns)):
        if key in grid and int(grid[key]) != size:
            raise ValueError(f"grid {key} is {grid[key]} but the data has {size}")
    if not 0 <= min_coverage <= 1:
        raise ValueError("min_coverage must be between 0 and 1")
    if not 0 < threshold < 1:
        raise ValueError("threshold must be between 0 and 1")
    if not 0 < ci_level < 1:
        raise ValueError("ci_level must be between 0 and 1")
    return rows, columns, months


def _mask_for(mask, variable: str, rows: int, columns: int) -> np.ndarray:
    """Skip reason per cell as an object array of shape (rows, columns): None or a reason."""
    reasons = np.full((rows, columns), None, dtype=object)
    if mask is None:
        return reasons
    given = mask.get(variable) if isinstance(mask, dict) else mask
    if given is None:
        return reasons
    given = np.asarray(given, dtype=object)
    if given.shape != (rows, columns):
        raise ValueError(f"{variable}: mask has shape {given.shape}, expected {(rows, columns)}")
    for (r, c), value in np.ndenumerate(given):
        if value is None or value == "":
            continue
        if value not in MASK_REASONS:
            raise ValueError(f"{variable}: mask value {value!r} at row {r}, column {c} is not None, 'ocean' or 'ice'")
        reasons[r, c] = value
    return reasons


# ------------------------------------------------------------------ the scan

def scan_trends(
    data: dict[str, np.ndarray],
    *,
    start_month: int,
    grid: dict,
    mask=None,
    min_coverage: float = 0.9,
    threshold: float = 0.05,
    ci_level: float = 0.95,
    rank_cap: dict | None = None,
    weights: dict | None = None,
    units: dict[str, str] | None = None,
    progress: Callable[[int, int], None] | None = None,
) -> ScanResult:
    """Test every unmasked cell of every variable and rank the significant trends.

    See the module docstring for the array layout, the mask and what is returned.

    ``grid``       ``{"cell_degrees", "lat_min", "lon_min"}`` as in ``run.settings.grid``
                   (``rows`` and ``columns`` are allowed and checked against the data).
    ``rank_cap``   ``{"top_n": 20, "max_share": 0.25}`` as in ``run.settings.rank_cap``, or
                   ``None`` for no cap.
    ``weights``    score weights; default ``WEIGHTS`` from ``score.py``.
    ``units``      units of the raw values per variable; defaults cover the four contract variables.
    ``progress``   called as ``progress(tests_done, tests_to_run)`` now and then.

    Raises ``ValueError`` for bad input, and names the variable and cell if one
    series makes a test fail. It does not carry on past a failure: a quiet gap in
    a scan is worse than a loud stop.
    """
    began = time.perf_counter()
    arrays = {v: np.asarray(a, dtype=float) for v, a in data.items()}
    all_units = {**DEFAULT_UNITS, **(units or {})}
    rows, columns, months = _check_inputs(arrays, start_month, grid, min_coverage, threshold, ci_level, all_units)
    variables = list(arrays)

    # --- decide which cells are tested
    skip = {}
    for variable in variables:
        reasons = _mask_for(mask, variable, rows, columns)
        present = np.isfinite(arrays[variable]).sum(axis=2)
        too_thin = (present / months < min_coverage) | (present < MIN_MONTHS_TO_TEST)
        reasons[(reasons == None) & too_thin] = "coverage"  # noqa: E711  (elementwise, on an object array)
        skip[variable] = reasons
    to_run = sum(int((skip[v] == None).sum()) for v in variables)  # noqa: E711

    # --- run the tests
    outcomes: dict[tuple[str, int, int], _Outcome] = {}
    done = 0
    for variable in variables:
        for row, column in zip(*np.nonzero(skip[variable] == None)):  # noqa: E711
            try:
                outcomes[(variable, int(row), int(column))] = _test_series(
                    arrays[variable][row, column], start_month, threshold, ci_level
                )
            except Exception as error:
                raise ValueError(f"{variable} {cell_id(int(row), int(column))}: {error}") from error
            done += 1
            if progress and done % 200 == 0:
                progress(done, to_run)
    if progress:
        progress(done, to_run)

    # --- correct for multiple testing, one family per variable
    keys = list(outcomes)
    p_adjusted_by_key: dict[tuple[str, int, int], float] = {}
    if keys:
        p_adjusted, _ = adjust_by_family(
            [outcomes[k].p_value for k in keys], [k[0] for k in keys], alpha=threshold
        )
        p_adjusted_by_key = {k: float(p) for k, p in zip(keys, p_adjusted)}
    significant_keys = [k for k in keys if p_adjusted_by_key[k] < threshold]

    # --- globe layers
    grids = [_layer(v, arrays[v].shape, grid, all_units[v], skip[v], outcomes, p_adjusted_by_key) for v in variables]

    # --- counts
    counts = _counts(variables, rows, columns, skip, outcomes, significant_keys)

    # --- findings, scored and ranked
    findings, evidence = [], {}
    for key in significant_keys:
        variable, row, column = key
        outcome = outcomes[key]
        finding, finding_evidence = _finding(
            variable, row, column, grid, all_units[variable], outcome, p_adjusted_by_key[key], threshold
        )
        findings.append(finding)
        evidence[finding["id"]] = finding_evidence
    _check_evidence(evidence)
    cap = rank_cap or {}
    ranked = rank_findings(
        findings,
        evidence,
        weights=weights,
        diversify=rank_cap is not None,
        **({"top_n": int(cap["top_n"]), "max_share": float(cap["max_share"])} if rank_cap is not None else {}),
    )

    return ScanResult(
        grids=grids,
        counts=counts,
        findings=ranked,
        evidence=evidence,
        elapsed_seconds=time.perf_counter() - began,
        flat_tests=sum(1 for o in outcomes.values() if o.flat),
    )


# ------------------------------------------------------------------ building the output

def _layer(variable, shape, grid, units, skip, outcomes, p_adjusted_by_key) -> dict:
    rows, columns, _ = shape
    deg = float(grid["cell_degrees"])
    slope = [[None] * columns for _ in range(rows)]
    p_adj = [[None] * columns for _ in range(rows)]
    reason = [[None] * columns for _ in range(rows)]
    for r in range(rows):
        for c in range(columns):
            if skip[r, c] is not None:
                reason[r][c] = skip[r, c]
            else:
                key = (variable, r, c)
                slope[r][c] = float(outcomes[key].slope_per_decade)
                p_adj[r][c] = float(p_adjusted_by_key[key])
    return {
        "variable": variable,
        "units": f"{units} per decade",
        "rows": rows,
        "columns": columns,
        "lat_min": round(float(grid["lat_min"]), 6),
        "lat_max": round(float(grid["lat_min"]) + rows * deg, 6),
        "lon_min": round(float(grid["lon_min"]), 6),
        "lon_max": round(float(grid["lon_min"]) + columns * deg, 6),
        "slope_per_decade": slope,
        "p_adjusted": p_adj,
        "skip_reason": reason,
    }


def _counts(variables, rows, columns, skip, outcomes, significant_keys) -> dict:
    by_variable = []
    skipped_by_reason = {r: 0 for r in SKIP_REASONS}
    scanned_cells = np.zeros((rows, columns), dtype=bool)
    for variable in variables:
        tested = skip[variable] == None  # noqa: E711
        scanned_cells |= tested
        run = int(tested.sum())
        significant = sum(1 for k in significant_keys if k[0] == variable)
        for reason in SKIP_REASONS:
            skipped_by_reason[reason] += int((skip[variable] == reason).sum())
        by_variable.append(
            {
                "variable": variable,
                "tests_run": run,
                "tests_significant": significant,
                "tests_not_significant": run - significant,
                "tests_skipped": rows * columns - run,
            }
        )
    total = rows * columns * len(variables)
    run = sum(v["tests_run"] for v in by_variable)
    significant = sum(v["tests_significant"] for v in by_variable)
    return {
        "cells_total": rows * columns,
        "cells_scanned": int(scanned_cells.sum()),
        "tests_total": total,
        "tests_run": run,
        "tests_significant": significant,
        "tests_not_significant": run - significant,
        "tests_skipped": total - run,
        "skipped_by_reason": skipped_by_reason,
        "by_variable": by_variable,
    }


def _finding(variable, row, column, grid, units, outcome: _Outcome, p_adjusted, threshold):
    result = outcome.trend
    cell = cell_object(row, column, grid)
    finding = {
        "id": f"trend.{variable}.{cell['id']}",
        "kind": "trend",
        "significant": True,
        "cell": cell,
        "variables": [{"id": variable, "units": units}],
        "preprocessing": {
            "months_present": result.months_present,
            "months_expected": result.months_expected,
            "coverage": result.coverage,
        },
        "method": {
            "test": result.test,
            "parameters": {
                **result.parameters,
                "slope_estimator": "sen",
                "robustness_test": result.robust_test,
                "robustness_p_value": result.robust_p_value,
                "multiple_testing": "benjamini_hochberg",
                "threshold": threshold,
            },
        },
        "result": result.result_block(p_adjusted, f"{units} per decade", threshold),
    }
    return _plain(finding), Evidence(noise_sd=outcome.noise_sd, robustness=trend_robustness(result))


def _plain(value):
    """Turn numpy scalars into Python ones, so the finding is JSON-ready (``json`` refuses ``np.bool_``)."""
    if isinstance(value, dict):
        return {k: _plain(v) for k, v in value.items()}
    if isinstance(value, (list, tuple)):
        return [_plain(v) for v in value]
    if isinstance(value, np.generic):
        return value.item()
    return value


def _check_evidence(evidence: dict[str, Evidence]) -> None:
    """Fail before ranking, naming the findings, if a significant test has no usable noise level."""
    bad = [fid for fid, e in evidence.items() if e.noise_sd is None or not math.isfinite(e.noise_sd) or e.noise_sd <= 0]
    if bad:
        raise ValueError(
            f"{len(bad)} significant trend(s) have a detrended noise of zero, so their effect size is undefined "
            f"(a series that is a perfect straight line?): {', '.join(sorted(bad)[:5])}"
        )
