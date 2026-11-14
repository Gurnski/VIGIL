"""Change points: a sudden, lasting shift in the level of one variable in one cell.

A change point is a month after which a cell's anomalies sit at a different level:
soil moisture that drops in 2012 and stays lower is the kind of thing meant. One step
is looked for in each series, tested, dated with a range, and reported only where a
step fits clearly better than a steady trend.

    from vigil.analysis.change_points import add_change_points

    with_steps = add_change_points(
        data,                                        # the same arrays scan_trends took
        with_cascades.findings, with_cascades.evidence,   # from add_cascades (or any earlier step)
        start_month=1, period_start="2001-01-01",    # the first month of the data, as in run.settings.period
        grid=GRID, mask=mask,                        # the same settings as the scan
        min_coverage=0.9, threshold=0.05, ci_level=0.95, rank_cap=RANK_CAP,
    )
    with_steps.findings    # every kind, ranked together, links written
    with_steps.evidence    # the Evidence for all of them
    with_steps.counts      # one row per variable: what was tested and what was found

The design is Will's (14 Nov 2026). Where a detail was settled by simulation it says so.

Which series
------------
Every variable in ``data`` that is not in ``exclude_variables`` (default
``NOT_CROSS_SYSTEM``, the earthquakes), in every cell the scan tested for it: not masked,
and with at least ``min_coverage`` of its months present. Cells the scan skipped stay
skipped. Earthquakes are left out because they are counts that the scan tests on annual
means, and a step after a large earthquake is its aftershock sequence. If they are let
in, they are tested on monthly values like the rest and the scan's own rules for counts
are not applied.

The test
--------
All of it runs on the anomalies (each value minus the mean of its calendar month).

1. **The step.** Every month that leaves at least ``min_segment_months`` (24) present
   months on each side is tried as the first month of a new level. The reported date is
   the one where two levels fit the series best (least squares); the levels are the
   means either side and ``shift`` is their difference. This is also the single
   breakpoint ``ruptures`` finds with its ``l2`` cost.
2. **Significance.** Pettitt's test asks whether the values before some month tend to
   be lower or higher than those after it, using ranks. Its p-value is
   ``2 * exp(-6 * K**2 / (n**3 + n**2))``. Monthly anomalies carry over from month to
   month, which makes a plain Pettitt test call a step in 22 to 99% of unchanged series
   (AR(1) 0.3 to 0.9). The correction is an effective sample size
   ``n_eff = n * (1 - r) / (1 + r)``, where r is the lag-1 autocorrelation of the
   residuals of the step fit, corrected for its small-sample bias (``r + (1 + 4r) / n``)
   and never below 0. K is a sum over pairs of months and grows as n squared, so it is
   scaled to the effective size too; together that divides the exponent by ``n / n_eff``.
   (Replacing n in the formula and leaving K alone would make the test *more* confident,
   not less.) Pettitt's own date is kept in ``method.parameters``; it is not the date
   reported, because off the middle of the record it is a poor estimate (within 3 months
   of a planted 2 sd step at month 60 of 300 in 59% of series, against 91% for least squares).
3. **Step or trend.** A steady trend passes any test for a step, so a warming cell would
   be reported twice. A series is a candidate only if a step fits **clearly** better than
   a straight line: the BIC of the line minus the BIC of the step, both on the effective
   sample size, must exceed ``min_bic_gain`` (10, "very strong" on the usual scale). The
   step has one more parameter than the line (two levels and a date against an intercept
   and a slope). With 10, at most 0.4% of pure-trend series are reported as steps; with 6
   it was 1.7%, with 2 up to 10%.
4. **Multiple testing.** One false-discovery family per variable, as for trends.
   Benjamini-Hochberg runs over the candidates, each charged for every cell tested for
   the variable (``family_tests``), as cascades do. Correcting every cell first and
   dropping the trends afterwards would let the hundreds of trending cells, which all
   pass Pettitt, loosen the threshold for everything else.
5. **The date range.** Not from ``ruptures``, which gives a date and no interval. For a
   single step in Gaussian noise the squared error at each candidate month is already
   known from step 1, and ``exp(-error / (2 * sigma**2))`` is, up to a constant, how
   likely each month is to be the date. ``date_low`` and ``date_high`` are the first and
   last month of the **smallest set of months holding** ``ci_level`` (95%) of that
   weight, so the range always contains the date. sigma squared is the residual variance
   of the step fit multiplied by ``n / n_eff``; without that the range is too narrow (it
   held a planted date 53 to 96% of the time, against 96 to 99% with it).
6. **Robustness.** ``ruptures`` (kernel change point detection with a linear kernel,
   which is the ``l2`` cost with any number of breakpoints) is run at two penalties. The
   first is the usual one, ``2 * sigma**2 * log(n)``, with no allowance for
   autocorrelation: ``ruptures`` is then free to place several breakpoints, and they can
   pull the date. The second is twice as strict and has the allowance,
   ``4 * sigma**2 * (n / n_eff) * log(n)``: only a strong step survives it. At each, the
   breakpoint nearest the reported date is compared with it by
   ``change_point_robustness`` (1 for the same month, 0 for a year or more away, or for
   no breakpoint), and the lower of the two is the robustness. A clean single step
   scores 1 or close to it; a series with two steps, which one step fits badly, scores
   about 0.6 on average. This is a sensitivity check only. It is not the date range.
   (With the allowance in both penalties every reported step scored exactly 1, which
   told findings apart no better than a constant.)

A change point is listed when its adjusted p is below ``threshold``. **Change points
that are not significant are not returned.** A flat series (a desert with no rain) is
tested and never significant. **One step per series**: several steps in one series are
not looked for, and a second step is neither reported nor allowed for.

Measured on made-up series (300 months, 5% level; see the tests and the hand-over note):
unchanged autocorrelated series pass the corrected Pettitt test 3.5 to 4.5% of the time
and are reported as change points 0.1% of the time or less, because the step-or-trend
rule removes most of what passes by chance.

The finding
-----------
* ``id`` is ``change_point.<variable>.<cell>``.
* ``change_point.date`` is the first month at the new level. ``units`` are the variable's.
* The contract's ``change_point`` block has no p-values, so they are in
  ``method.parameters`` (``p_value``, ``p_adjusted``) with everything else that was measured.
* ``links``: a change point and the trend finding for the same variable in the same cell
  link to each other. Two change points in the same cell, in different variables, whose
  dates are within ``link_within_months`` (12) of each other link to each other with
  ``lag_months``, the gap between their dates in months (never negative; the dates say
  which came first). Existing links are kept, so the pipeline must only add
  ``links = []`` where there is none.

Scoring
-------
The effect is the shift in units of the noise either side of it (``step_sd``, not the
detrended noise used for trends). The certainty falls as the date range widens (a range
of 12 months scores 0.5). ``rank_findings`` does the rest; a change point counts its
variable towards the cross-system score of its cell, and in the rank cap it belongs to
its variable's group.

What the result cannot claim
----------------------------
The date range is the model's uncertainty about where one step fits best. It is not
proof of when a change happened, and says nothing about why. A shift can come from the
instrument or the processing (a new satellite, a new product version) as well as from
the place: ``counts`` gives, per variable, the commonest date and the share of change
points that fall on it, and a large share on one month is a warning sign. A change that
is part trend and part step can be missed or dated badly. ``limitations()`` returns
these as sentences for ``standing.limitations``.

What the pipeline still adds, as for a trend: ``variables`` metadata (the entry here
holds only ``id`` and ``units``), ``period``, ``summary``, ``standing``, and the text
fields of ``preprocessing``. It also needs a ``series`` for each change point, so the
evidence view can draw the step. Change points are not tests in the run record's sense:
leave the test counts alone and add them to ``findings_total`` and ``findings_by_kind``.
"""

from __future__ import annotations

import math
import time
from collections import Counter
from dataclasses import dataclass
from datetime import date
from itertools import combinations

import numpy as np
import ruptures
from scipy.stats import rankdata

from vigil.analysis.cascades import adjust_candidates, anomalies, detrend
from vigil.analysis.scan import (
    DEFAULT_UNITS,
    FLAT_TOLERANCE,
    MIN_MONTHS_TO_TEST,
    _check_inputs,
    _mask_for,
    _plain,
    cell_object,
    check_cell_offset,
)
from vigil.analysis.score import (
    NOT_CROSS_SYSTEM,
    Evidence,
    change_point_robustness,
    rank_findings,
    step_sd,
)

METHOD = "pettitt_single_step"
DEFAULT_MIN_SEGMENT = 24
DEFAULT_MIN_BIC_GAIN = 10.0
DEFAULT_LINK_WITHIN = 12

# The lag-1 autocorrelation used for the effective sample size is kept below this, so the
# variance inflation (1 + r) / (1 - r) stays finite.
MAX_AUTOCORRELATION = 0.99
# The two ruptures penalties of the robustness check. The first is in units of sigma^2 * log(n)
# (no allowance for autocorrelation), the second in units of sigma^2 * (n / n_eff) * log(n).
FIRST_PENALTY = 2.0
SECOND_PENALTY = 4.0
ROBUSTNESS_TEST = "ruptures, linear-kernel change point detection at two penalties"


@dataclass
class ChangePointResult:
    """Everything so far plus the change points, ranked together, with links written on both ends.

    ``counts`` has one row per variable, for the log and the method text: ``tests_run`` (the
    false-discovery family), ``flat``, ``candidates`` (a step fits clearly better than a line),
    ``significant`` (the change points listed), and ``commonest_date`` with
    ``share_on_commonest_date``, the check for a shift that many cells share.
    """

    findings: list[dict]
    evidence: dict[str, Evidence]
    change_points: int
    counts: list[dict]
    elapsed_seconds: float = 0.0


@dataclass
class StepTests:
    """The single-step fit and test for a batch of anomaly series. One entry per series.

    ``split`` is the index of the first month at the new level. ``low`` and ``high`` are the
    first and last index of the date range. Where ``testable`` is false (no month leaves enough
    data on both sides, or the series does not vary) the p-value is 1 and the rest means nothing.
    """

    testable: np.ndarray
    split: np.ndarray
    low: np.ndarray
    high: np.ndarray
    level_before: np.ndarray
    level_after: np.ndarray
    p_value: np.ndarray
    pettitt_statistic: np.ndarray
    pettitt_split: np.ndarray
    autocorrelation: np.ndarray        # of the step fit's residuals, bias-corrected, 0 or more
    inflation: np.ndarray              # n / n_eff = (1 + r) / (1 - r)
    months_present: np.ndarray
    residual_variance: np.ndarray      # of the step fit, before inflation
    bic_gain: np.ndarray               # BIC of a straight line minus BIC of the step


# ------------------------------------------------------------------ the statistics

def step_tests(block, min_segment: int = DEFAULT_MIN_SEGMENT, level: float = 0.95) -> StepTests:
    """Fit and test one step in each row of ``block``, anomaly series of shape (series, months).

    NaN is a missing month. See the module docstring for the method.
    """
    x = np.atleast_2d(np.asarray(block, dtype=float))
    series, months = x.shape
    rows = np.arange(series)
    present = np.isfinite(x)
    filled = np.where(present, x, 0.0)
    n = present.sum(axis=1)

    # A split s (1 .. months - 1) puts months 0 .. s-1 before and s .. after. It is allowed when both
    # sides hold enough present months and month s itself is present (else s and s + 1 are one split).
    before = np.cumsum(present, axis=1)[:, :-1]
    after = n[:, None] - before
    allowed = (before >= min_segment) & (after >= min_segment) & present[:, 1:]
    mean = np.divide(filled.sum(axis=1), n, out=np.zeros(series), where=n > 0)
    total_spread = (np.where(present, x - mean[:, None], 0.0) ** 2).sum(axis=1)
    testable = allowed.any(axis=1) & (total_spread > 0)

    # --- least squares: the squared error of two levels, for every split
    safe_before, safe_after = np.maximum(before, 1), np.maximum(after, 1)
    centred = np.where(present, x - mean[:, None], 0.0)
    running = np.cumsum(centred, axis=1)[:, :-1]           # sum before the split; the sum after is its negative
    gain = running**2 / safe_before + running**2 / safe_after
    error = np.where(allowed, total_spread[:, None] - gain, np.inf)
    best = np.where(testable, error.argmin(axis=1), 0)
    split = best + 1
    step_error = np.where(testable, np.maximum(error[rows, best], 0.0), total_spread)
    level_before = mean + running[rows, best] / safe_before[rows, best]
    level_after = mean - running[rows, best] / safe_after[rows, best]
    month = np.arange(months)[None, :]
    residual = x - np.where(month < split[:, None], level_before[:, None], level_after[:, None])

    # --- autocorrelation of the residuals, and what it does to the sample size
    r = _lag_one(residual)
    r = np.clip(r + (1.0 + 4.0 * r) / np.maximum(n, 1), 0.0, MAX_AUTOCORRELATION)
    inflation = (1.0 + r) / (1.0 - r)
    effective = n / inflation

    # --- Pettitt, on ranks
    ranks = np.where(present, rankdata(np.where(present, x, np.inf), axis=1), 0.0)     # missing months rank last, then count 0
    u = 2.0 * np.cumsum(ranks, axis=1)[:, :-1] - before * (n[:, None] + 1.0)
    size = np.where(allowed, np.abs(u), -1.0)
    pettitt_best = size.argmax(axis=1)
    k = np.where(testable, size[rows, pettitt_best], 0.0)
    cube = n.astype(float) ** 3 + n.astype(float) ** 2
    p_value = np.where(testable, np.minimum(1.0, 2.0 * np.exp(-6.0 * k**2 / np.maximum(cube, 1.0) / inflation)), 1.0)

    # --- a step against a straight line, by BIC on the effective sample size
    line_error = np.nansum(detrend(x) ** 2, axis=1)
    floor = np.maximum(step_error, 1e-300)
    bic_gain = np.where(
        testable,
        effective * np.log(np.maximum(line_error, 1e-300) / floor) - np.log(np.maximum(effective, 2.0)),
        -np.inf,
    )

    # --- the date range: the smallest set of months holding ``level`` of exp(-error / (2 sigma^2 inflation))
    variance = step_error / np.maximum(n - 2, 1)
    scale = np.maximum(2.0 * variance * inflation, 1e-300)
    weight = np.where(allowed, np.exp(-(np.where(allowed, error, 0.0) - step_error[:, None]) / scale[:, None]), 0.0)
    weight_total = np.maximum(weight.sum(axis=1, keepdims=True), 1e-300)
    weight = weight / weight_total
    order = np.argsort(-weight, axis=1, kind="stable")
    cumulative = np.cumsum(np.take_along_axis(weight, order, axis=1), axis=1)
    taken = np.minimum((cumulative < level).sum(axis=1) + 1, weight.shape[1])
    in_set = np.arange(weight.shape[1])[None, :] < taken[:, None]
    low = np.where(in_set, order, months).min(axis=1) + 1
    high = np.where(in_set, order, -1).max(axis=1) + 1
    low, high = np.minimum(low, split), np.maximum(high, split)        # the date is the heaviest month, so this changes nothing

    return StepTests(
        testable=testable,
        split=split,
        low=np.where(testable, low, split),
        high=np.where(testable, high, split),
        level_before=level_before,
        level_after=level_after,
        p_value=p_value,
        pettitt_statistic=k,
        pettitt_split=pettitt_best + 1,
        autocorrelation=r,
        inflation=inflation,
        months_present=n,
        residual_variance=variance,
        bic_gain=bic_gain,
    )


def _lag_one(residual: np.ndarray) -> np.ndarray:
    """Lag-1 autocorrelation of each row over the pairs of neighbouring months that are both present."""
    present = np.isfinite(residual)
    x = np.where(present, residual, 0.0)
    count = np.maximum(present.sum(axis=1), 1)
    x = x - (x.sum(axis=1) / count)[:, None] * present
    variance = (x * x).sum(axis=1) / count
    pairs = (present[:, :-1] & present[:, 1:]).sum(axis=1)
    products = (x[:, :-1] * x[:, 1:]).sum(axis=1)
    usable = (pairs > 0) & (variance > 0)
    return np.clip(np.divide(products, pairs * variance, out=np.zeros(count.shape), where=usable), -1.0, 1.0)


def breakpoints(series, penalty: float, min_segment: int = DEFAULT_MIN_SEGMENT) -> list[int]:
    """Month indices where ``ruptures`` puts a change of level in one series, at one penalty.

    Each index is the first month of a new level. Missing months are left out of the search and
    the result is mapped back to the real months.
    """
    x = np.asarray(series, dtype=float)
    where = np.flatnonzero(np.isfinite(x))
    if where.size < 2 * min_segment:
        return []
    search = ruptures.KernelCPD(kernel="linear", min_size=min_segment).fit(x[where].reshape(-1, 1))
    return [int(where[end]) for end in search.predict(pen=float(penalty)) if end < where.size]


def robustness(series, split: int, variance: float, inflation: float, min_segment: int = DEFAULT_MIN_SEGMENT):
    """The sensitivity check for one series. Returns ``(score, nearest_at_each_penalty)``.

    ``nearest_at_each_penalty`` holds, for each of the two penalties, the month index of the
    breakpoint nearest ``split``, or ``None`` when ``ruptures`` found none.
    """
    n = int(np.isfinite(np.asarray(series, dtype=float)).sum())
    nearest: list[int | None] = []
    for penalty in (FIRST_PENALTY * variance * math.log(n), SECOND_PENALTY * variance * inflation * math.log(n)):
        found = breakpoints(series, penalty, min_segment)
        nearest.append(min(found, key=lambda month: abs(month - split)) if found else None)
    return min(change_point_robustness(split, month) for month in nearest), nearest


def month_date(period_start: date, index: int) -> str:
    """The contract's date for the month ``index`` months after ``period_start``."""
    total = period_start.year * 12 + period_start.month - 1 + int(index)
    return f"{total // 12:04d}-{total % 12 + 1:02d}-01"


def limitations() -> list[str]:
    """Sentences for ``standing.limitations`` of every change point. True of any, not claims about one."""
    return [
        "The date range is the model's uncertainty about where a single step fits best. It is not proof of when "
        "a change happened, and it says nothing about why",
        "Only one step is looked for in each series. A second step, or a change that is part trend and part step, "
        "can be missed or dated badly",
        "A step is reported only where it fits clearly better than a straight line, so a real but gradual change "
        "is left to the trend findings",
        "A sudden shift can come from the instrument or the processing, such as a new satellite or product version, "
        "as well as from the place. The same date in many cells of one product is a warning sign",
        "The p-value and the date range allow for the memory in monthly series with an effective sample size, "
        "which is an approximation",
    ]


# ------------------------------------------------------------------ building the output

def _change_point(variable: str, units: str, cell: dict, tests: StepTests, i: int, months: int,
                  period_start: date, p_adjusted: float, noise: float, check, settings: dict) -> dict:
    split = int(tests.split[i])
    level_before, level_after = float(tests.level_before[i]), float(tests.level_after[i])
    present = int(tests.months_present[i])
    score, nearest = check
    as_date = lambda month: "none" if month is None else month_date(period_start, month)  # noqa: E731
    return {
        "id": f"change_point.{variable}.{cell['id']}",
        "kind": "change_point",
        "significant": bool(p_adjusted < settings["threshold"]),
        "cell": cell,
        "variables": [{"id": variable, "units": units}],
        "preprocessing": {"months_present": present, "months_expected": int(months), "coverage": present / months},
        "method": {
            "test": METHOD,
            "parameters": {
                "series": "anomaly",
                "date_estimator": "least squares, one step",
                "min_segment_months": settings["min_segment"],
                "p_value": float(tests.p_value[i]),
                "p_adjusted": float(p_adjusted),
                "pettitt_statistic": float(tests.pettitt_statistic[i]),
                "pettitt_date": month_date(period_start, int(tests.pettitt_split[i])),
                "autocorrelation_correction": "effective sample size from the lag-1 autocorrelation of the residuals",
                "residual_autocorrelation": round(float(tests.autocorrelation[i]), 4),
                "effective_months": round(present / float(tests.inflation[i]), 1),
                "step_against_trend": "BIC of a straight line minus BIC of the step, on the effective months",
                "bic_gain": round(float(tests.bic_gain[i]), 2),
                "min_bic_gain": settings["min_bic_gain"],
                "date_range": "smallest set of months holding the level, from the likelihood of each date",
                "date_range_level": settings["ci_level"],
                "noise_sd": float(noise),
                "robustness_test": ROBUSTNESS_TEST,
                "robustness_date_first_penalty": as_date(nearest[0]),
                "robustness_date_second_penalty": as_date(nearest[1]),
                "robustness": round(float(score), 4),
                "multiple_testing": "benjamini_hochberg",
                "family": variable,
                "family_tests": settings["family_tests"],
                "threshold": settings["threshold"],
            },
        },
        "change_point": {
            "date": month_date(period_start, split),
            "date_low": month_date(period_start, int(tests.low[i])),
            "date_high": month_date(period_start, int(tests.high[i])),
            "level_before": level_before,
            "level_after": level_after,
            "shift": level_after - level_before,
            "units": units,
        },
    }


def _check_finite(finding: dict) -> None:
    """Fail here, naming the finding, if a number is not finite. Never write NaN."""
    block = finding["change_point"]
    numbers = [block["level_before"], block["level_after"], block["shift"], finding["preprocessing"]["coverage"],
               *(v for v in finding["method"]["parameters"].values() if isinstance(v, float))]
    if not all(math.isfinite(x) for x in numbers):
        raise ValueError(f"{finding['id']}: a value is not a finite number")


def _months_apart(a: str, b: str) -> int:
    da, db = date.fromisoformat(a), date.fromisoformat(b)
    return abs((db.year - da.year) * 12 + (db.month - da.month))


# ------------------------------------------------------------------ the entry point

def add_change_points(
    data: dict[str, np.ndarray],
    findings: list[dict],
    evidence: dict[str, Evidence],
    *,
    start_month: int,
    period_start: str,
    grid: dict,
    mask=None,
    min_coverage: float = 0.9,
    threshold: float = 0.05,
    ci_level: float = 0.95,
    rank_cap: dict | None = None,
    weights: dict | None = None,
    units: dict[str, str] | None = None,
    min_segment_months: int = DEFAULT_MIN_SEGMENT,
    min_bic_gain: float = DEFAULT_MIN_BIC_GAIN,
    link_within_months: int = DEFAULT_LINK_WITHIN,
    exclude_variables=NOT_CROSS_SYSTEM,
    cell_offset: tuple[int, int] = (0, 0),
) -> ChangePointResult:
    """Find the change points and rank them together with the findings so far.

    ``data``, ``start_month``, ``grid``, ``mask``, ``min_coverage``, ``threshold``,
    ``ci_level``, ``rank_cap``, ``weights`` and ``units`` are the ones the scan took.
    ``findings`` and ``evidence`` come from the step before (``add_cascades``,
    ``add_contradictions`` or ``scan_trends``); every kind already in the list passes
    through with its links kept. Inputs are not changed.

    ``period_start``         the date of the first month of the data, ``YYYY-MM-01``, as in
                             ``run.settings.period.start``. The analysis needs it to write dates.
                             Its month must be ``start_month``.
    ``min_segment_months``   present months that must lie on each side of a step.
    ``min_bic_gain``         how much better a step must fit than a straight line to be a candidate.
    ``link_within_months``   two change points in one cell link when their dates are this close.
    ``ci_level``             also the level of the date range.
    ``cell_offset``          the one the scan was given for a region: the data's first cell sits at this
                             ``(row, column)`` of the full grid, and the cell ids are the full grid's.

    Raises ``ValueError`` for bad input, or if the list already holds change points.
    """
    began = time.perf_counter()
    arrays = {v: np.asarray(a, dtype=float) for v, a in data.items()}
    all_units = {**DEFAULT_UNITS, **(units or {})}
    rows, columns, months = _check_inputs(arrays, start_month, grid, min_coverage, threshold, ci_level, all_units)
    cell_offset = check_cell_offset(cell_offset)
    try:
        first = date.fromisoformat(period_start)
    except (TypeError, ValueError) as error:
        raise ValueError(f"period_start must be a date such as 2001-01-01, got {period_start!r}") from error
    if first.day != 1 or first.month != start_month:
        raise ValueError(f"period_start {period_start} must be the first day of month {start_month}, the start_month given")
    if int(min_segment_months) != min_segment_months or min_segment_months < 2:
        raise ValueError("min_segment_months must be a whole number of months, 2 or more")
    min_segment = int(min_segment_months)
    if months < 3 * min_segment:
        raise ValueError(f"{months} months are too few to look for a step with {min_segment} months on each side")
    if link_within_months < 0:
        raise ValueError("link_within_months must be 0 or more")
    if any(f["kind"] == "change_point" for f in findings):
        raise ValueError("the findings already hold change points; add them once, to a list without any")

    excluded = set(exclude_variables)
    new: list[dict] = []
    new_evidence: dict[str, Evidence] = {}
    counts: list[dict] = []
    for variable in (v for v in arrays if v not in excluded):
        present = np.isfinite(arrays[variable]).sum(axis=2)
        tested = (_mask_for(mask, variable, rows, columns) == None) & ~(  # noqa: E711
            (present / months < min_coverage) | (present < MIN_MONTHS_TO_TEST))
        where_rows, where_columns = np.nonzero(tested)
        raw = arrays[variable][where_rows, where_columns]
        block = anomalies(raw, start_month)
        flat = _is_flat(raw, block)
        tests = step_tests(np.where(flat[:, None], 0.0, block), min_segment, ci_level)

        shift = tests.level_after - tests.level_before
        candidates = np.nonzero(tests.testable & ~flat & (tests.bic_gain > min_bic_gain) & (shift != 0))[0]
        row = {"variable": variable, "tests_run": int(tested.sum()), "flat": int(flat.sum()),
               "candidates": int(candidates.size), "significant": 0,
               "commonest_date": None, "share_on_commonest_date": None}
        counts.append(row)
        adjusted = adjust_candidates(tests.p_value[candidates], row["tests_run"])
        settings = {"threshold": threshold, "ci_level": ci_level, "min_segment": min_segment,
                    "min_bic_gain": float(min_bic_gain), "family_tests": row["tests_run"]}
        dates: Counter = Counter()
        for i, p_adjusted in zip(candidates, adjusted):
            if not p_adjusted < threshold:
                continue
            cell = cell_object(int(where_rows[i]), int(where_columns[i]), grid, cell_offset)
            label = f"change_point.{variable}.{cell['id']}"
            split = int(tests.split[i])
            try:
                noise = step_sd(block[i], split)
                check = robustness(block[i], split, float(tests.residual_variance[i]), float(tests.inflation[i]), min_segment)
            except Exception as error:
                raise ValueError(f"{label}: {error}") from error
            if not math.isfinite(noise) or noise <= 0:
                raise ValueError(f"{label}: the noise either side of the step is zero, so its effect size is undefined "
                                 "(a series that is a perfect step?)")
            finding = _plain(_change_point(variable, all_units[variable], cell, tests, int(i), months, first,
                                           float(p_adjusted), noise, check, settings))
            _check_finite(finding)
            new.append(finding)
            new_evidence[finding["id"]] = Evidence(noise_sd=noise, robustness=check[0])
            dates[finding["change_point"]["date"]] += 1
            row["significant"] += 1
        if dates:
            commonest, shared = sorted(dates.items(), key=lambda item: (-item[1], item[0]))[0]
            row["commonest_date"], row["share_on_commonest_date"] = commonest, round(shared / row["significant"], 4)

    # links, on both ends: the existing findings' lists are extended in copies, never in place
    linked = {f["id"]: {**f, "links": list(f.get("links", []))} for f in findings}
    trend_at = {(f["variables"][0]["id"], f["cell"]["id"]): f["id"] for f in findings if f["kind"] == "trend"}
    in_cell: dict[str, list[dict]] = {}
    for finding in new:
        finding["links"] = []
        trend_id = trend_at.get((finding["variables"][0]["id"], finding["cell"]["id"]))
        if trend_id is not None:
            finding["links"].append({"finding_id": trend_id, "kind": "trend"})
            linked[trend_id]["links"].append({"finding_id": finding["id"], "kind": "change_point"})
        in_cell.setdefault(finding["cell"]["id"], []).append(finding)
    for group in in_cell.values():
        for a, b in combinations(group, 2):
            gap = _months_apart(a["change_point"]["date"], b["change_point"]["date"])
            if gap <= link_within_months:
                a["links"].append({"finding_id": b["id"], "kind": "change_point", "lag_months": gap})
                b["links"].append({"finding_id": a["id"], "kind": "change_point", "lag_months": gap})

    combined = list(linked.values()) + new
    all_evidence = {**evidence, **new_evidence}
    cap = {"top_n": int(rank_cap["top_n"]), "max_share": float(rank_cap["max_share"])} if rank_cap is not None else {}
    ranked = rank_findings(combined, all_evidence, weights=weights, diversify=rank_cap is not None, **cap)
    return ChangePointResult(findings=ranked, evidence=all_evidence, change_points=len(new), counts=counts,
                             elapsed_seconds=time.perf_counter() - began)


def _is_flat(raw: np.ndarray, anomaly: np.ndarray) -> np.ndarray:
    """The scan's rule for a series with nothing to test: its anomalies do not vary beyond rounding error."""
    present = np.isfinite(anomaly)
    high = np.where(present, anomaly, -np.inf).max(axis=1, initial=-np.inf)
    low = np.where(present, anomaly, np.inf).min(axis=1, initial=np.inf)
    scale = np.where(np.isfinite(raw), np.abs(raw), 0.0).max(axis=1, initial=0.0)
    return ~(high - low > FLAT_TOLERANCE * scale)
