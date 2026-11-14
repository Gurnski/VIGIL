"""Cascades: two variables in one cell where one follows the other by a month or more.

A cascade is an association in time. Month by month, once the seasons and each
series' own long-term trend are taken out, one variable's ups and downs are followed
some months later by the other's. Vegetation following soil moisture by two months is
the example in the contract. It never says one caused the other.

    from vigil.analysis.cascades import add_cascades

    with_cascades = add_cascades(
        data,                                      # the same arrays scan_trends took
        combined.findings, combined.evidence,      # from add_contradictions (or scan_trends)
        start_month=1, grid=GRID, mask=mask,       # the same settings as the scan
        min_coverage=0.9, threshold=0.05, ci_level=0.95,
        max_lag_months=6, rank_cap=RANK_CAP,
    )
    with_cascades.findings    # trends, contradictions and cascades, ranked together, links written
    with_cascades.evidence    # the Evidence for all of them
    with_cascades.counts      # one row per pair of variables: what was tested and what was found

The design choices below were put to Will on 14 Nov 2026 and are his.

Which pairs and which cells
---------------------------
Every pair of the climate variables in ``data`` is tested (six pairs for temperature,
precipitation, soil moisture and vegetation). Variables in ``exclude_variables``
(default ``NOT_CROSS_SYSTEM``, the earthquakes) take no part.

A pair is tested in a cell when

* the cell is not masked for either variable (the scan's mask),
* the months in which **both** variables have a value make up at least
  ``min_coverage`` of the period (the scan's rule, applied to the pair),
* neither series is flat once its seasonal cycle is removed (a desert with no rain has
  nothing to correlate), and
* at least one of the two variables has a significant trend finding in that cell.

The last rule keeps the list about change: a cascade here always sits beside a trend.
Without it nearly every land cell would list "soil moisture follows rain", and the
cross-system score would be the same everywhere. It does not bias the test, because the
test runs on detrended series and so cannot see the trend the cell was chosen for.
``cells="all"`` drops the rule and tests every cell where both variables were scanned.

The test
--------
1. **Anomalies.** Each series minus the mean of its calendar month, as in the scan.
2. **Detrending.** Each anomaly series has its own least-squares line subtracted. Two
   series that both trend are correlated at every lag without any month-to-month link,
   so correlating plain anomalies would mostly find the trends again.
3. **Lagged correlation.** Pearson's r between a[t] and b[t + lag] for every lag from
   ``-max_lag_months`` to ``+max_lag_months`` (13 lags for 6 months), over the months
   where both values are present. The **strongest** (largest absolute) correlation picks
   the lag, and its side picks the leader: the lead is measured, never assumed.
4. **Autocorrelation.** Monthly anomalies carry over from month to month, so 300 months
   are worth fewer than 300 independent ones and the plain test of r is far too
   confident. The effective number of months is (Pyper and Peterman 1998, after
   Bretherton and others 1999)

       months / (1 + 2 * sum over j of (1 - j / months) * ra(j) * rb(j))

   where ra and rb are the two series' autocorrelations at lag j, summed to a fifth of
   the record. It is never allowed above the real number of months. r is then tested
   with Student's t on that many months less two.
5. **The choice of lag.** Taking the best of 13 lags is 13 chances to be lucky. The
   p-value at the chosen lag is corrected for all of them (Sidak:
   ``1 - (1 - p) ** lags``). This is ``cascade.p_value``: the chance of a correlation
   this strong at *any* of the lags, in either direction, if the series were unrelated.
   The uncorrected one is kept in ``method.parameters`` as ``p_value_at_lag``.
6. **A clear lead.** A cascade needs one variable to move first. A pair is a candidate
   only when the strongest correlation is at a lag of one month or more **and** the
   correlation in the same month (lag 0) falls below the confidence interval of that
   peak (Fisher's z, on the effective months, level ``ci_level``). Pairs that move in
   the same month, or whose lead is not clear, are counted in ``counts`` and not listed.
7. **Multiple testing.** One false-discovery family per pair of variables, as trends
   have one per variable. Benjamini-Hochberg runs over the candidates, but every
   candidate is charged for **all** the cells tested for the pair (``family_tests``),
   not just for the candidates. Unrelated pairs nearly always pass rule 6 by chance,
   while real same-month links do not; correcting first and filtering afterwards would
   let the false ones pile up among the cascades that are listed.

A cascade is listed when its adjusted p is below ``threshold``. **Cascades that are not
significant are not returned**, as with trends: the list is of findings that passed.

Measured on made-up data (300 months, 5% level; see the tests): unrelated
autocorrelated pairs are called linked 1 to 5% of the time. Paying for the choice of lag
but not for the autocorrelation gives 20% at an AR(1) of 0.5 and 61% at 0.9. The test is
most cautious for very persistent series (about 1% at 0.9).

The finding
-----------
* ``id`` is ``cascade.<leading>-<following>.<cell>``; ``variables`` is the leading one,
  then the following one. There is at most one cascade per pair per cell.
* ``cascade.lag_months`` is 1 or more. ``correlation`` is the detrended correlation at
  that lag. ``correlations_by_lag`` runs from lag 0 to ``max_lag_months`` in the
  direction reported (the leader first). The best correlation the other way round is in
  ``method.parameters`` (``strongest_reverse_lag_months``, ``strongest_reverse_correlation``).
* ``preprocessing.months_present`` is the months in which both variables have a value.
* ``links``: the cascade links to the trend finding of each of its variables in the
  cell, where there is one, and that trend links back; both ends carry ``lag_months``
  and ``correlation``. Two cascades in the same cell that share a variable link to each
  other (a chain such as rain, soil, plants), with no lag on the link. Existing links
  are kept, so the pipeline must only add ``links = []`` where there is none.

Scoring
-------
The effect is the absolute correlation. The certainty comes from the interval on the
correlation, using the effective months (``Evidence.effective_months``), and the
robustness from how well the correlation holds one month either side of the chosen lag
(``cascade_robustness``). In the rank cap all cascades are one group. A cascade counts
both of its variables towards the cross-system score of every finding in its cell, so
adding cascades changes the scores of the trends beside them. That is intended.

What the result cannot claim
----------------------------
An association in time, not a cause: a third thing (a drought, an El Nino year) can
drive both. The link is between month-to-month departures, not between the long-term
trends, which were removed. Lags are whole months, so a lead shorter than a month looks
like no lead. A straight line does not remove a step or a curve in the series, and what
is left of one can still correlate. ``limitations()`` returns these as sentences for
``standing.limitations``.

What the pipeline still adds, as for a trend: ``variables`` metadata (each entry here
holds only ``id`` and ``units``), ``period``, ``summary`` (which must not say "causes"),
``standing``, and the text fields of ``preprocessing``. It also needs a ``series`` for
**each** of the cascade's two variables in the cell, so the evidence view can draw both.
The cascades are not tests in the run record's sense (a test there is one variable in
one cell): leave the test counts alone and add the cascades to ``findings_total`` and
``findings_by_kind``. ``max_lag_months`` goes in ``run.settings``.
"""

from __future__ import annotations

import math
import time
from dataclasses import dataclass
from itertools import combinations

import numpy as np
from scipy.stats import norm
from scipy.stats import t as student_t

from vigil.analysis.scan import (
    DEFAULT_UNITS,
    FLAT_TOLERANCE,
    MIN_MONTHS_TO_TEST,
    _check_inputs,
    _mask_for,
    _plain,
    cell_object,
)
from vigil.analysis.score import NOT_CROSS_SYSTEM, Evidence, cascade_robustness, rank_findings

METHOD = "lagged_pearson_correlation"
DEFAULT_MAX_LAG = 6

# The autocorrelations in the effective sample size are summed to this share of the
# record (Pyper and Peterman suggest about a fifth).
AUTOCORRELATION_SHARE = 5
# Below this many effective months nothing can be said about a correlation.
MIN_EFFECTIVE_MONTHS = 4.0

WITH_TREND = "trend"
ALL_CELLS = "all"
CELL_RULES = {
    WITH_TREND: "cells with a significant trend in either variable",
    ALL_CELLS: "every cell where both variables were scanned",
}


@dataclass
class CascadeResult:
    """Everything so far plus the cascades, ranked together, with links written on both ends.

    ``counts`` has one row per pair of variables, for the log and the method text:
    ``tests_run`` (the false-discovery family), ``strongest_at_lag_0`` and
    ``lead_not_clear`` (tested, not candidates), ``candidates`` and ``significant``
    (the cascades listed), plus the cells left out as ``skipped_coverage`` and ``skipped_flat``.
    """

    findings: list[dict]
    evidence: dict[str, Evidence]
    cascades: int
    counts: list[dict]
    elapsed_seconds: float = 0.0


@dataclass
class LinkTests:
    """The lagged-correlation test for a batch of pairs of series. One entry per pair.

    ``correlations[i, j]`` is the correlation at lag ``j - max_lag``; a positive lag means
    the first series leads. ``lag`` is the signed lag of the strongest correlation.
    """

    max_lag: int
    correlations: np.ndarray
    months_paired: np.ndarray        # months used at the chosen lag
    lag: np.ndarray
    correlation: np.ndarray
    effective_months: np.ndarray     # at the chosen lag
    p_value_at_lag: np.ndarray       # before paying for the choice of lag
    p_value: np.ndarray              # after
    ci_low: np.ndarray               # interval on the chosen correlation
    ci_high: np.ndarray
    clear_lead: np.ndarray           # lag is not 0 and lag 0 falls below the interval


# ------------------------------------------------------------------ the statistics

def anomalies(block, start_month: int = 1) -> np.ndarray:
    """``deseasonalise`` for many series at once: ``block`` has shape (series, months)."""
    x = np.asarray(block, dtype=float)
    month = (start_month - 1 + np.arange(x.shape[1])) % 12
    out = np.full(x.shape, np.nan)
    for m in range(12):
        columns = month == m
        part = x[:, columns]
        present = np.isfinite(part)
        count = present.sum(axis=1)
        total = np.where(present, part, 0.0).sum(axis=1)
        mean = np.divide(total, count, out=np.full(count.shape, np.nan), where=count > 0)
        out[:, columns] = part - mean[:, None]
    return out


def detrend(series) -> np.ndarray:
    """Subtract each row's own least-squares line, fitted to the months present. NaN stays NaN."""
    x = np.asarray(series, dtype=float)
    present = np.isfinite(x)
    n = np.maximum(present.sum(axis=1), 1)
    t = np.arange(x.shape[1], dtype=float)
    filled = np.where(present, x, 0.0)
    t_mean = (present * t).sum(axis=1) / n
    x_mean = filled.sum(axis=1) / n
    centred = (t[None, :] - t_mean[:, None]) * present
    spread = (centred * centred).sum(axis=1)
    slope = np.divide((centred * filled).sum(axis=1), spread, out=np.zeros(spread.shape), where=spread > 0)
    return x - x_mean[:, None] - slope[:, None] * (t[None, :] - t_mean[:, None])


def autocorrelations(series, lags: int) -> np.ndarray:
    """Autocorrelation of each row at lags 1 to ``lags``, over the pairs of months present.

    Rows are taken to have a mean of zero, which ``detrend`` leaves them with.
    """
    present = np.isfinite(series)
    x = np.where(present, series, 0.0)
    variance = (x * x).sum(axis=1) / np.maximum(present.sum(axis=1), 1)
    out = np.zeros((x.shape[0], lags))
    for j in range(1, lags + 1):
        pairs = (present[:, :-j] & present[:, j:]).sum(axis=1)
        products = (x[:, :-j] * x[:, j:]).sum(axis=1)
        usable = (pairs > 0) & (variance > 0)
        out[:, j - 1] = np.divide(products, pairs * variance, out=np.zeros(pairs.shape), where=usable)
    return np.clip(out, -1.0, 1.0)


def autocorrelation_factor(a, b) -> np.ndarray:
    """How many real months make one independent month for the correlation of ``a`` with ``b``.

    ``1 + 2 * sum((1 - j / months) * ra(j) * rb(j))``, never below 1: the correction may
    only make the test less ready to call a link, as with the trend test's variance floor.
    """
    months = a.shape[1]
    lags = max(1, months // AUTOCORRELATION_SHARE)
    weights = 1.0 - np.arange(1, lags + 1) / months
    factor = 1.0 + 2.0 * (weights * autocorrelations(a, lags) * autocorrelations(b, lags)).sum(axis=1)
    return np.maximum(factor, 1.0)


def lagged_correlations(a, b, max_lag: int):
    """Pearson's r of a[t] with b[t + lag] for lag = -max_lag .. max_lag.

    Returns ``(correlations, months_paired)``, each of shape (series, 2 * max_lag + 1).
    A lag with too few months or no variation comes back as NaN.
    """
    present_a, present_b = np.isfinite(a), np.isfinite(b)
    xa, xb = np.where(present_a, a, 0.0), np.where(present_b, b, 0.0)
    months = a.shape[1]
    r = np.full((a.shape[0], 2 * max_lag + 1), np.nan)
    paired = np.zeros(r.shape, dtype=int)
    for j, lag in enumerate(range(-max_lag, max_lag + 1)):
        first = slice(0, months - lag) if lag >= 0 else slice(-lag, months)
        second = slice(lag, months) if lag >= 0 else slice(0, months + lag)
        both = present_a[:, first] & present_b[:, second]
        u, v = xa[:, first] * both, xb[:, second] * both
        n = both.sum(axis=1)
        safe = np.maximum(n, 1)
        covariance = (u * v).sum(axis=1) - u.sum(axis=1) * v.sum(axis=1) / safe
        spread_u = (u * u).sum(axis=1) - u.sum(axis=1) ** 2 / safe
        spread_v = (v * v).sum(axis=1) - v.sum(axis=1) ** 2 / safe
        usable = (n >= 3) & (spread_u > 0) & (spread_v > 0)
        r[:, j] = np.divide(covariance, np.sqrt(np.where(usable, spread_u * spread_v, 1.0)),
                            out=np.full(n.shape, np.nan), where=usable)
        paired[:, j] = n
    return np.clip(r, -1.0, 1.0), paired


def lagged_link_tests(a, b, max_lag: int = DEFAULT_MAX_LAG, ci_level: float = 0.95) -> LinkTests:
    """Test each row of ``a`` against the same row of ``b`` for a lagged link.

    ``a`` and ``b`` are anomaly series of shape (series, months), NaN for a missing
    month. They are detrended here. See the module docstring for the method.
    """
    a, b = detrend(np.atleast_2d(a)), detrend(np.atleast_2d(b))
    if a.shape != b.shape:
        raise ValueError(f"the two blocks of series differ in shape: {a.shape} and {b.shape}")
    correlations, paired = lagged_correlations(a, b, max_lag)
    if np.isnan(correlations).any():
        bad = int(np.isnan(correlations).any(axis=1).sum())
        raise ValueError(f"{bad} pair(s) of series have a lag with too few months or no variation to correlate")
    rows = np.arange(a.shape[0])
    best = np.abs(correlations).argmax(axis=1)
    r = correlations[rows, best]
    months_paired = paired[rows, best]

    effective = np.minimum(months_paired / autocorrelation_factor(a, b), months_paired)
    testable = effective >= MIN_EFFECTIVE_MONTHS
    freedom = np.where(testable, effective - 2.0, 1.0)
    statistic = np.abs(r) * np.sqrt(freedom / np.maximum(1.0 - r * r, 1e-300))
    p_at_lag = np.where(testable, 2.0 * student_t.sf(statistic, freedom), 1.0)
    lags = 2 * max_lag + 1
    p_value = -np.expm1(lags * np.log1p(-np.minimum(p_at_lag, 1.0)))     # Sidak: 1 - (1 - p) ** lags

    # Interval on the chosen correlation (Fisher's z), and whether lag 0 falls below it.
    sign = np.where(r >= 0, 1.0, -1.0)
    z = np.arctanh(np.minimum(np.abs(r), 0.999999))
    half = norm.ppf(1 - (1 - ci_level) / 2) / np.sqrt(np.where(testable, effective - 3.0, 1.0))
    low, high = np.tanh(z - half), np.tanh(z + half)
    at_zero = sign * correlations[:, max_lag]
    clear = testable & (best != max_lag) & (at_zero < low)
    return LinkTests(
        max_lag=max_lag,
        correlations=correlations,
        months_paired=months_paired,
        lag=best - max_lag,
        correlation=r,
        effective_months=effective,
        p_value_at_lag=p_at_lag,
        p_value=np.clip(p_value, 0.0, 1.0),
        ci_low=np.where(sign > 0, low, -high),
        ci_high=np.where(sign > 0, high, -low),
        clear_lead=clear,
    )



def adjust_candidates(p_values, tests_in_family: int) -> np.ndarray:
    """Benjamini-Hochberg over the candidates, each charged for every test in the family.

    The k-th smallest p-value is scaled by ``tests_in_family / k``, not by the number of
    candidates over k. With ``tests_in_family`` equal to the number of p-values this is
    plain Benjamini-Hochberg. An adjusted p is never below its raw p.
    """
    p = np.asarray(p_values, dtype=float)
    if p.size == 0:
        return p.copy()
    if tests_in_family < p.size:
        raise ValueError("the family cannot be smaller than the number of candidates")
    order = np.argsort(p, kind="stable")
    scaled = np.minimum(1.0, p[order] * tests_in_family / np.arange(1, p.size + 1))
    adjusted = np.empty(p.size)
    adjusted[order] = np.minimum.accumulate(scaled[::-1])[::-1]
    return adjusted


def limitations(max_lag_months: int = DEFAULT_MAX_LAG) -> list[str]:
    """Sentences for ``standing.limitations`` of every cascade. True of any cascade, not claims about one."""
    return [
        "A lagged correlation shows that two series move together with a delay. It does not show that one caused "
        "the other: something else, such as a drought or an El Nino year, can drive both",
        "The seasonal cycle and each series' own straight-line trend were removed first, so this is a link between "
        "month-to-month departures and says nothing about the long-term trends",
        f"The lag is the strongest of the lags from 0 to {max_lag_months} months in either direction, in whole months. "
        "A lead shorter than a month cannot be seen",
        "The p-value allows for the memory in monthly series with an effective sample size, which is an approximation",
        "A step or a curve in either series is not removed by a straight line and can add to the correlation",
    ]


# ------------------------------------------------------------------ building the output

def _cascade(pair, tests: LinkTests, i: int, cell: dict, units: dict, both_present: int, months: int,
             p_adjusted: float, settings: dict) -> dict:
    a, b = pair
    lag = int(tests.lag[i])
    centre = tests.max_lag
    forward = [float(x) for x in tests.correlations[i, centre:]]                  # a leads: lags 0, 1, 2, ...
    backward = [float(x) for x in tests.correlations[i, centre::-1]]              # b leads
    if lag > 0:
        leading, following, by_lag, reverse = a, b, forward, backward
    else:
        leading, following, by_lag, reverse = b, a, backward, forward
    reverse_lag = max(range(1, centre + 1), key=lambda k: abs(reverse[k]))
    return {
        "id": f"cascade.{leading}-{following}.{cell['id']}",
        "kind": "cascade",
        "significant": bool(p_adjusted < settings["threshold"]),
        "cell": cell,
        "variables": [{"id": leading, "units": units[leading]}, {"id": following, "units": units[following]}],
        "preprocessing": {
            "months_present": int(both_present),
            "months_expected": int(months),
            "coverage": both_present / months,
        },
        "method": {
            "test": METHOD,
            "parameters": {
                "max_lag_months": centre,
                "lags_tested": 2 * centre + 1,
                "series": "anomaly",
                "detrended": True,
                "detrending": "least-squares line",
                "autocorrelation_correction": "effective sample size (Pyper and Peterman)",
                "effective_months": round(float(tests.effective_months[i]), 1),
                "months_paired": int(tests.months_paired[i]),
                "lag_selection_correction": "sidak",
                "p_value_at_lag": float(tests.p_value_at_lag[i]),
                "ci_level": settings["ci_level"],
                "correlation_ci_low": float(tests.ci_low[i]),
                "correlation_ci_high": float(tests.ci_high[i]),
                "lead_rule": "the strongest lag is 1 month or more and lag 0 falls below the interval",
                "strongest_reverse_lag_months": reverse_lag,
                "strongest_reverse_correlation": reverse[reverse_lag],
                "cells_tested": settings["cell_rule"],
                "multiple_testing": "benjamini_hochberg",
                "family": f"{a}-{b}",
                "family_tests": settings["family_tests"],
                "threshold": settings["threshold"],
            },
        },
        "cascade": {
            "leading_variable": leading,
            "following_variable": following,
            "lag_months": abs(lag),
            "correlation": float(tests.correlation[i]),
            "correlations_by_lag": by_lag,
            "p_value": float(tests.p_value[i]),
            "p_adjusted": float(p_adjusted),
        },
    }


def _check_finite(finding: dict) -> None:
    """Fail here, naming the finding, if a number is not finite. Never write NaN."""
    block = finding["cascade"]
    numbers = [block["correlation"], block["p_value"], block["p_adjusted"], *block["correlations_by_lag"],
               finding["preprocessing"]["coverage"],
               *(v for v in finding["method"]["parameters"].values() if isinstance(v, float))]
    if not all(math.isfinite(x) for x in numbers):
        raise ValueError(f"{finding['id']}: a value is not a finite number")


# ------------------------------------------------------------------ the entry point

def add_cascades(
    data: dict[str, np.ndarray],
    findings: list[dict],
    evidence: dict[str, Evidence],
    *,
    start_month: int,
    grid: dict,
    mask=None,
    min_coverage: float = 0.9,
    threshold: float = 0.05,
    ci_level: float = 0.95,
    max_lag_months: int = DEFAULT_MAX_LAG,
    rank_cap: dict | None = None,
    weights: dict | None = None,
    units: dict[str, str] | None = None,
    pairs=None,
    cells: str = WITH_TREND,
    exclude_variables=NOT_CROSS_SYSTEM,
) -> CascadeResult:
    """Find the cascades and rank them together with the findings so far.

    ``data``, ``start_month``, ``grid``, ``mask``, ``min_coverage``, ``threshold``,
    ``ci_level``, ``rank_cap``, ``weights`` and ``units`` are the ones the scan took.
    ``findings`` and ``evidence`` come from ``add_contradictions`` or straight from
    ``scan_trends``; every kind already in the list passes through with its links kept.
    Inputs are not changed.

    ``max_lag_months``  the longest lead looked for, in either direction (``run.settings.max_lag_months``).
    ``pairs``           the pairs of variables to test, as ``[(a, b), ...]``. Default: every pair of the
                        variables in ``data`` that are not in ``exclude_variables``. The order inside a
                        pair does not decide who leads.
    ``cells``           ``"trend"`` (default): only cells with a significant trend in either variable.
                        ``"all"``: every cell where both variables were scanned.

    Raises ``ValueError`` for bad input, or if the list already holds cascades.
    """
    began = time.perf_counter()
    arrays = {v: np.asarray(a, dtype=float) for v, a in data.items()}
    all_units = {**DEFAULT_UNITS, **(units or {})}
    rows, columns, months = _check_inputs(arrays, start_month, grid, min_coverage, threshold, ci_level, all_units)
    if int(max_lag_months) != max_lag_months or max_lag_months < 1:
        raise ValueError("max_lag_months must be a whole number of months, 1 or more")
    max_lag = int(max_lag_months)
    if months < 4 * max_lag + MIN_MONTHS_TO_TEST:
        raise ValueError(f"{months} months are too few to look for lags of up to {max_lag} months")
    if cells not in CELL_RULES:
        raise ValueError(f"cells must be one of {sorted(CELL_RULES)}, got {cells!r}")
    if any(f["kind"] == "cascade" for f in findings):
        raise ValueError("the findings already hold cascades; add them once, to a list without any")

    excluded = set(exclude_variables)
    usable = [v for v in arrays if v not in excluded]
    if pairs is None:
        pairs = list(combinations(usable, 2))
    else:
        pairs = [tuple(p) for p in pairs]
        for pair in pairs:
            if len(pair) != 2 or pair[0] == pair[1] or any(v not in arrays for v in pair):
                raise ValueError(f"pair {pair!r} must name two different variables in data")
        if len({frozenset(p) for p in pairs}) != len(pairs):
            raise ValueError("a pair of variables is listed twice")

    # where each variable was not masked, and where it has a significant trend
    unmasked = {v: _mask_for(mask, v, rows, columns) == None for v in {v for p in pairs for v in p}}  # noqa: E711
    trend_at: dict[tuple[str, int, int], str] = {}
    has_trend = {v: np.zeros((rows, columns), dtype=bool) for v in unmasked}
    for f in findings:
        variable = f["variables"][0]["id"]
        if f["kind"] == "trend" and f["significant"] and variable in has_trend:
            row, column = f["cell"]["row"], f["cell"]["column"]
            has_trend[variable][row, column] = True
            trend_at[(variable, row, column)] = f["id"]

    new: list[dict] = []
    new_evidence: dict[str, Evidence] = {}
    counts: list[dict] = []
    for a, b in pairs:
        wanted = unmasked[a] & unmasked[b]
        if cells == WITH_TREND:
            wanted &= has_trend[a] | has_trend[b]
        where_rows, where_columns = np.nonzero(wanted)
        raw_a, raw_b = arrays[a][where_rows, where_columns], arrays[b][where_rows, where_columns]

        both_present = (np.isfinite(raw_a) & np.isfinite(raw_b)).sum(axis=1)
        thin = (both_present / months < min_coverage) | (both_present < 4 * max_lag + MIN_MONTHS_TO_TEST)
        anomalies_a, anomalies_b = anomalies(raw_a, start_month), anomalies(raw_b, start_month)
        flat = ~thin & (_is_flat(raw_a, anomalies_a) | _is_flat(raw_b, anomalies_b))
        keep = ~thin & ~flat
        where_rows, where_columns, both_present = where_rows[keep], where_columns[keep], both_present[keep]

        row = {"pair": f"{a}-{b}", "tests_run": int(keep.sum()), "strongest_at_lag_0": 0, "lead_not_clear": 0,
               "candidates": 0, "significant": 0, "skipped_coverage": int(thin.sum()), "skipped_flat": int(flat.sum())}
        counts.append(row)
        if not keep.any():
            continue
        try:
            tests = lagged_link_tests(anomalies_a[keep], anomalies_b[keep], max_lag, ci_level)
        except ValueError as error:
            raise ValueError(f"{a} and {b}: {error}") from error

        candidates = np.nonzero(tests.clear_lead)[0]
        row["strongest_at_lag_0"] = int((tests.lag == 0).sum())
        row["lead_not_clear"] = int(((tests.lag != 0) & ~tests.clear_lead).sum())
        row["candidates"] = int(candidates.size)
        adjusted = adjust_candidates(tests.p_value[candidates], row["tests_run"])
        settings = {"threshold": threshold, "ci_level": ci_level, "cell_rule": CELL_RULES[cells],
                    "family_tests": row["tests_run"]}
        for i, p_adjusted in zip(candidates, adjusted):
            if not p_adjusted < threshold:
                continue
            cell = cell_object(int(where_rows[i]), int(where_columns[i]), grid)
            finding = _plain(_cascade((a, b), tests, int(i), cell, all_units, int(both_present[i]), months,
                                      float(p_adjusted), settings))
            _check_finite(finding)
            block = finding["cascade"]
            new.append(finding)
            new_evidence[finding["id"]] = Evidence(
                robustness=cascade_robustness(block["correlations_by_lag"], block["lag_months"]),
                effective_months=float(tests.effective_months[i]),
            )
            row["significant"] += 1

    # links, on both ends: the existing findings' lists are extended in copies, never in place
    linked = {f["id"]: {**f, "links": list(f.get("links", []))} for f in findings}
    in_cell: dict[str, list[dict]] = {}
    for finding in new:
        block = finding["cascade"]
        measured = {"lag_months": block["lag_months"], "correlation": block["correlation"]}
        finding["links"] = []
        for variable in (block["leading_variable"], block["following_variable"]):
            trend_id = trend_at.get((variable, finding["cell"]["row"], finding["cell"]["column"]))
            if trend_id is not None:
                finding["links"].append({"finding_id": trend_id, "kind": "trend", **measured})
                linked[trend_id]["links"].append({"finding_id": finding["id"], "kind": "cascade", **measured})
        in_cell.setdefault(finding["cell"]["id"], []).append(finding)
    for group in in_cell.values():
        for first, second in combinations(group, 2):
            if {v["id"] for v in first["variables"]} & {v["id"] for v in second["variables"]}:
                first["links"].append({"finding_id": second["id"], "kind": "cascade"})
                second["links"].append({"finding_id": first["id"], "kind": "cascade"})

    combined = list(linked.values()) + new
    all_evidence = {**evidence, **new_evidence}
    cap = {"top_n": int(rank_cap["top_n"]), "max_share": float(rank_cap["max_share"])} if rank_cap is not None else {}
    ranked = rank_findings(combined, all_evidence, weights=weights, diversify=rank_cap is not None, **cap)
    return CascadeResult(findings=ranked, evidence=all_evidence, cascades=len(new), counts=counts,
                         elapsed_seconds=time.perf_counter() - began)


def _is_flat(raw: np.ndarray, anomaly: np.ndarray) -> np.ndarray:
    """The scan's rule for a series with nothing to test: its anomalies do not vary beyond rounding error."""
    present = np.isfinite(anomaly)
    high = np.where(present, anomaly, -np.inf).max(axis=1, initial=-np.inf)
    low = np.where(present, anomaly, np.inf).min(axis=1, initial=np.inf)
    scale = np.where(np.isfinite(raw), np.abs(raw), 0.0).max(axis=1, initial=0.0)
    return ~(high - low > FLAT_TOLERANCE * scale)
