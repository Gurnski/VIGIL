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

Earthquakes
-----------
``earthquakes`` is a fifth variable that sits beside the four climate ones in the same
scan, with its own globe layer, its own false-discovery family and its own findings.
The value for a cell and month is the **number of earthquakes of magnitude 4.5 or more**
in that cell that month (units: ``events per month``). Differences from the climate variables:

* A month with no earthquakes is a real zero, not a missing value. NaN is only for months
  the catalogue does not cover, so coverage is normally 100%.
* Earthquakes happen at sea, so a mask given for all variables at once is not applied to
  them. A mask given for ``earthquakes`` by name is.
* A quiet cell has too few events to test. A cell is tested only if it holds at least
  ``DEFAULT_MIN_TOTAL["earthquakes"]`` events over the period (100); fewer are skipped
  with the reason ``coverage``, which the contract has no better word for. In the run
  record those skips therefore mean "too little data", months or events.
* The test runs on **annual means**: the mean monthly count of each run of 12 months from
  the first value (a partial run at the end is left out). Month by month a count series is
  mostly zeros, which leaves Sen's slope resting on ties (with the raw 2001 to 2025
  catalogue it was exactly zero in 96% of the busy cells). Annual means have far fewer.
  Annual means have no seasonal cycle, so none is removed. Mann-Kendall, Hamed-Rao (lag
  3 years) and the pre-whitening cross-check run on them as usual, and at least 10 whole
  years are needed.
* The slope is still **events per month per decade**, and the interval, the noise and the
  effect size are in the same unit. ``months_present`` and ``months_expected`` are the
  months in the years used. ``method.parameters`` says ``aggregation`` and
  ``max_lag_years``, not ``max_lag_months``. The series the app charts stay monthly.
* A test whose Sen slope is exactly zero is not called significant (``zero_slope_tests``).
* In the ranking, earthquakes are their own group in the rank cap, and take no part in
  the cross-system score: a quake in the same cell as a warming trend is not "two Earth
  systems changing together" (``NOT_CROSS_SYSTEM`` in ``score.py``).

What the data cannot say, and the pipeline should state in ``standing.limitations`` for
every earthquake finding: the catalogue grows more complete with time, so a rising count
can reflect better detection; earthquakes cluster, and one large event with its aftershocks
can produce a "trend" or a step without any change in the underlying rate; no declustering
is applied; and the result is a statement about the count of M4.5+ events, nothing about
cause or prediction. The catalogue is binned onto the scan grid by the pipeline, not here.

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
from dataclasses import dataclass, replace
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
    "earthquakes": "events per month",
}

# Variables that are counts of events (zeros are real values, not missing data) and
# that happen at sea as well as on land. A mask given for all variables at once is
# not applied to them; only a mask given for the variable by name is. See "Earthquakes".
UNMASKED_BY_DEFAULT = frozenset({"earthquakes"})

# A cell is tested only if its values add up to at least this much over the whole
# period. For a count variable this means "enough events to have a series worth
# testing"; cells below it are skipped with the reason ``coverage``. Override per
# variable with ``min_total``. Variables not listed here have no such rule.
DEFAULT_MIN_TOTAL = {"earthquakes": 100}

# Variables tested on annual means instead of monthly values. A count series is mostly
# zeros month by month, which leaves Sen's slope (the median of the pairwise slopes)
# resting on ties; the mean of each run of 12 months has far fewer. The slope is still
# reported per month per decade, so it stays comparable with the monthly series.
ANNUAL_VARIABLES = frozenset({"earthquakes"})
MONTHS_PER_YEAR = 12
MIN_YEARS_TO_TEST = 10
# Hamed-Rao autocorrelation lag for annual series, in years (monthly series use 12 months).
ANNUAL_MAX_LAG = 3

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
    # Tests whose p-value passed the threshold but whose Sen slope is exactly zero.
    # They have no direction to report, so they were given p = 1. Informational.
    zero_slope_tests: int = 0


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
    zero_slope: bool = False


# ------------------------------------------------------------------ one series

def _annual_means(series: np.ndarray) -> np.ndarray:
    """Mean of each run of 12 months, from the first value. A run with a missing month is NaN.

    A partial run at the end is left out.
    """
    years = series.size // MONTHS_PER_YEAR
    return series[: years * MONTHS_PER_YEAR].reshape(years, MONTHS_PER_YEAR).mean(axis=1)


def _annual_result(result: TrendResult, years_present: int, years_total: int) -> TrendResult:
    """Put a trend test run on annual means into the units and labels the rest of the scan uses.

    ``trend_test`` multiplies by 120 for "per decade" on the assumption that each step is a
    month. For annual steps the factor is 10, so the slope and interval are divided by 12.
    The month counts are the years' months, and the lag is labelled in years.
    """
    factor = 1.0 / MONTHS_PER_YEAR
    parameters = {k: v for k, v in result.parameters.items() if k != "max_lag_months"}
    parameters["max_lag_years"] = result.parameters["max_lag_months"]
    parameters["aggregation"] = "annual mean of monthly values"
    return replace(
        result,
        slope_per_decade=result.slope_per_decade * factor,
        ci_low=result.ci_low * factor,
        ci_high=result.ci_high * factor,
        months_present=years_present * MONTHS_PER_YEAR,
        months_expected=years_total * MONTHS_PER_YEAR,
        parameters=parameters,
    )


def _test_series(
    series: np.ndarray, start_month: int, threshold: float, ci_level: float, annual: bool = False, keep_all: bool = False
) -> _Outcome:
    """Deseasonalise and test one raw monthly series (or, with ``annual``, its annual means).

    The full result and the noise are kept only when the raw p-value is below the threshold (the
    only tests that can be significant), or for every test with ``keep_all``.
    """
    if annual:
        anomalies = _annual_means(series)        # a sum over whole years has no seasonal cycle to remove
        reference = anomalies
    else:
        anomalies = deseasonalise(series, start_month)
        reference = series
    present = anomalies[np.isfinite(anomalies)]
    scale = float(np.max(np.abs(reference[np.isfinite(reference)])))
    if float(np.ptp(present)) <= FLAT_TOLERANCE * scale:
        return _Outcome(p_value=1.0, slope_per_decade=0.0, flat=True)

    if annual:
        result = _annual_result(
            trend_test(anomalies, alpha=threshold, ci_level=ci_level, max_lag=ANNUAL_MAX_LAG),
            present.size, anomalies.size,
        )
    else:
        result = trend_test(anomalies, alpha=threshold, ci_level=ci_level)
    if not math.isfinite(result.p_value) or not math.isfinite(result.slope_per_decade):
        raise ValueError("the trend test returned a p-value or slope that is not a number")
    if result.p_value >= threshold:
        if keep_all and result.slope_per_decade != 0:
            return _Outcome(result.p_value, result.slope_per_decade, result, detrended_sd(anomalies))
        return _Outcome(result.p_value, result.slope_per_decade)
    if result.slope_per_decade == 0:
        # Mann-Kendall sees a drift but the median slope is zero, which happens when most
        # values are tied. A trend with no size and no direction cannot be reported, so it
        # is not called significant.
        return _Outcome(1.0, 0.0, zero_slope=True)
    # For annual means the values are already in the unit of the slope (per month), so the
    # noise is in the same unit and the effect size comes out in sigma per decade as usual.
    return _Outcome(result.p_value, result.slope_per_decade, result, detrended_sd(anomalies))


# ------------------------------------------------------------------ cells and ids

def cell_id(row: int, column: int) -> str:
    """The contract's cell id, such as ``r60c112``."""
    return f"r{row:02d}c{column:03d}"


def check_cell_offset(cell_offset) -> tuple[int, int]:
    """``cell_offset`` as a pair of whole numbers that are not negative."""
    try:
        row0, column0 = cell_offset
    except (TypeError, ValueError):
        raise ValueError("cell_offset must be a pair (row, column)") from None
    if any(isinstance(v, bool) or not isinstance(v, (int, np.integer)) or v < 0 for v in (row0, column0)):
        raise ValueError("cell_offset must be two whole numbers that are not negative")
    return int(row0), int(column0)


def cell_object(row: int, column: int, grid: dict, cell_offset: tuple[int, int] = (0, 0)) -> dict:
    """The contract's ``cell`` object: id, row, column and the cell's bounds.

    ``row`` and ``column`` are positions in the array that was scanned, and ``grid`` describes
    that array (its south-west corner), so the bounds follow from them. ``cell_offset`` is where
    that array's corner sits on the full scan grid: the id, row and column are the full grid's,
    so a cell keeps its id whether it is scanned alone or with the rest of the world.
    """
    deg = float(grid["cell_degrees"])
    lat_min = float(grid["lat_min"]) + row * deg
    lon_min = float(grid["lon_min"]) + column * deg
    row0, column0 = cell_offset
    return {
        "id": cell_id(row + row0, column + column0),
        "row": int(row) + row0,
        "column": int(column) + column0,
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
    if isinstance(mask, dict):
        given = mask.get(variable)
    else:
        given = None if variable in UNMASKED_BY_DEFAULT else mask
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
    min_total: dict[str, float] | None = None,
    annual_variables=ANNUAL_VARIABLES,
    cell_offset: tuple[int, int] = (0, 0),
    include_not_significant: bool = False,
    progress: Callable[[int, int], None] | None = None,
) -> ScanResult:
    """Test every unmasked cell of every variable and rank the significant trends.

    See the module docstring for the array layout, the mask and what is returned.

    ``grid``       ``{"cell_degrees", "lat_min", "lon_min"}`` as in ``run.settings.grid``
                   (``rows`` and ``columns`` are allowed and checked against the data).
    ``rank_cap``   ``{"top_n": 20, "max_share": 0.25}`` as in ``run.settings.rank_cap``, or
                   ``None`` for no cap.
    ``weights``    score weights; default ``WEIGHTS`` from ``score.py``.
    ``units``      units of the raw values per variable; defaults cover the five known variables.
    ``min_total``  per variable, the least the values must add up to over the period for a cell to
                   be tested (``DEFAULT_MIN_TOTAL``: 100 events for ``earthquakes``). Cells below
                   it are skipped as ``coverage``.
    ``annual_variables``  variables tested on annual means (``ANNUAL_VARIABLES``: ``earthquakes``).
                   They need at least 10 whole years; a year with a missing month counts as missing.
    ``cell_offset``  ``(row, column)`` of the data's first cell on the full scan grid, for a scan of a
                   region. ``grid`` then describes the region's own south-west corner (``lat_min`` and
                   ``lon_min`` of its first cell), ``data`` and ``mask`` cover the region, and every cell id,
                   row and column in the output is the full grid's (a cell is ``r60c112`` however it is
                   scanned). The correction for many tests then covers the region's tests only.
                   Pass the same offset to every later step. The layers describe the region.
    ``include_not_significant``  also return a finding (``significant: false``, ``direction: none``) for
                   every test that ran and did not pass, so a question about one cell can be answered. This is
                   what ``POST /analyse`` on a region wants. Off for the stored scan, where every finding is a
                   significant test. The counts do not change, but ``len(findings)`` is then more than
                   ``counts["tests_significant"]``. Flat series and zero-slope tests have no result to report
                   and get no finding. Every later step ignores the findings that are not significant.
    ``progress``   called as ``progress(tests_done, tests_to_run)`` now and then.

    Raises ``ValueError`` for bad input, and names the variable and cell if one
    series makes a test fail. It does not carry on past a failure: a quiet gap in
    a scan is worse than a loud stop.
    """
    began = time.perf_counter()
    arrays = {v: np.asarray(a, dtype=float) for v, a in data.items()}
    all_units = {**DEFAULT_UNITS, **(units or {})}
    rows, columns, months = _check_inputs(arrays, start_month, grid, min_coverage, threshold, ci_level, all_units)
    cell_offset = check_cell_offset(cell_offset)
    variables = list(arrays)
    annual = {v for v in variables if v in set(annual_variables)}
    if annual and months < MIN_YEARS_TO_TEST * MONTHS_PER_YEAR:
        raise ValueError(
            f"{sorted(annual)} are tested on annual means and need at least {MIN_YEARS_TO_TEST} years, got {months} months"
        )

    # --- decide which cells are tested
    skip = {}
    minimum = {**DEFAULT_MIN_TOTAL, **(min_total or {})}
    for variable in variables:
        reasons = _mask_for(mask, variable, rows, columns)
        if variable in annual:
            years = months // MONTHS_PER_YEAR
            blocks = arrays[variable][:, :, : years * MONTHS_PER_YEAR].reshape(rows, columns, years, MONTHS_PER_YEAR)
            present = np.isfinite(blocks.mean(axis=3)).sum(axis=2)
            too_thin = (present / years < min_coverage) | (present < MIN_YEARS_TO_TEST)
        else:
            present = np.isfinite(arrays[variable]).sum(axis=2)
            too_thin = (present / months < min_coverage) | (present < MIN_MONTHS_TO_TEST)
        if variable in minimum:
            too_thin = too_thin | (np.nansum(arrays[variable], axis=2) < minimum[variable])
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
                    arrays[variable][row, column], start_month, threshold, ci_level, variable in annual,
                    include_not_significant,
                )
            except Exception as error:
                raise ValueError(f"{variable} {cell_id(int(row) + cell_offset[0], int(column) + cell_offset[1])}: {error}") from error
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
    significant_set = set(significant_keys)
    reported = keys if include_not_significant else significant_keys
    for key in reported:
        variable, row, column = key
        outcome = outcomes[key]
        if outcome.trend is None:
            continue                       # a flat series or a zero slope: nothing to report
        finding, finding_evidence = _finding(
            variable, row, column, grid, all_units[variable], outcome, p_adjusted_by_key[key], threshold, cell_offset,
            key in significant_set,
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
        zero_slope_tests=sum(1 for o in outcomes.values() if o.zero_slope),
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


def _finding(variable, row, column, grid, units, outcome: _Outcome, p_adjusted, threshold, cell_offset=(0, 0),
             significant=True):
    result = outcome.trend
    cell = cell_object(row, column, grid, cell_offset)
    finding = {
        "id": f"trend.{variable}.{cell['id']}",
        "kind": "trend",
        "significant": significant,
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
