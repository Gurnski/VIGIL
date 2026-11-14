"""The ranking score: which findings the app shows first.

Every finding gets five parts, each from 0 to 1 with higher meaning better, and
``total`` is their weighted sum (the shape the contract asks for):

``effect_size``   how large the change is, in units of the series' own noise
``certainty``     how tight the estimate is (1 is exact, 0.5 is "interval touches zero")
``coverage``      months_present / months_expected
``robustness``    how well it survives a sensitivity check
``cross_system``  how many Earth systems (variables) change together in its cell

Weights, fixed before the first real scan and written into the run record:

    effect_size 0.30, certainty 0.20, coverage 0.05, robustness 0.20, cross_system 0.25

Effect size leads because it answers "how much did it change?". Cross-system is
next because connected change (a cascade) is what the project exists to find.
Certainty and robustness each count for about a fifth: a large effect that the
sensitivity check overturns, or with a wide interval, should not lead the
list. Coverage gets the least because cells below the minimum coverage are
already skipped, so what is left varies little and should not decide the order.
Do not tune these after seeing results.

Raw slopes cannot be compared across K, mm per month, kg per m2 and NDVI, so
effects are standardised: slope per decade divided by the standard deviation of
the detrended anomaly series ("sigma per decade"). Temperature warmed
significantly almost everywhere, so even a fair score fills the top of the list
with it. ``rank_findings`` therefore limits any one group (a variable, or the
cascades) to a quarter of the top 20.

Inputs. A finding (the contract's dict) holds the slope, interval, coverage and
p-values. Two things it does not hold come in as ``Evidence``: the noise
standard deviation (``detrended_sd``) and the robustness result.
"""

from __future__ import annotations

import math
from dataclasses import dataclass
from datetime import date

import numpy as np
from scipy.stats import norm

from vigil.analysis.trend import TrendResult, sens_slope

PART_NAMES = ("effect_size", "certainty", "coverage", "robustness", "cross_system")

WEIGHTS = {
    "effect_size": 0.30,
    "certainty": 0.20,
    "coverage": 0.05,
    "robustness": 0.20,
    "cross_system": 0.25,
}

# The effect maps x -> x / (x + HALF). HALF is the effect, in sigma, that earns 0.5.
# A trend of half a sigma per decade is already strong over 25 years.
TREND_HALF_SIGMA = 0.5
# A step of one sigma in the level of the series earns 0.5.
STEP_HALF_SIGMA = 1.0

# A change point date that is uncertain by this many months earns certainty 0.5.
DATE_HALF_MONTHS = 12
# A second change point setting that lands this many months away scores robustness 0.
DATE_TOLERANCE_MONTHS = 12
# Level of the interval on a cascade's correlation. Matches run.settings.ci_level.
CORRELATION_CI_LEVEL = 0.95

# Cross-system score by the number of distinct variables with a significant
# finding in the cell (the finding's own included). Two or more is the boost.
CROSS_SYSTEM_BY_VARIABLES = (0.0, 0.5, 0.8, 1.0)

TOP_N = 20
MAX_GROUP_SHARE = 0.25


@dataclass(frozen=True)
class Evidence:
    """What a finding needs for its score that the contract does not carry.

    ``noise_sd``     the noise standard deviation in the variable's units: ``detrended_sd``
                     for a trend or contradiction, ``step_sd`` for a change point.
    ``robustness``   0 to 1 from ``trend_robustness(result)``, ``change_point_robustness`` or
                     ``cascade_robustness``. For a contradiction, the lower of its two trends.
    ``certainty``    a contradiction only: the lower ``trend_certainty`` of its two trends.
    ``effective_months``  a cascade only, optional: the number of independent months
                     behind the correlation once autocorrelation is allowed for. Without
                     it ``preprocessing.months_present`` is used, which is optimistic.
    """

    noise_sd: float | None = None
    robustness: float = 0.0
    certainty: float | None = None
    effective_months: float | None = None


# ------------------------------------------------------------------ building blocks

def clip01(x: float) -> float:
    """Clamp to 0..1. A NaN or infinity is an error, never a quiet 0: it means an input was missing."""
    if not math.isfinite(x):
        raise ValueError(f"score part is not a finite number: {x}")
    return float(min(1.0, max(0.0, x)))


def detrended_sd(anomalies) -> float:
    """Standard deviation of a monthly anomaly series after removing its Sen trend."""
    x = np.asarray(anomalies, dtype=float)
    slope, _, _ = sens_slope(x)
    t = np.arange(x.size, dtype=float)
    keep = ~np.isnan(x)
    intercept = np.median(x[keep] - slope * t[keep])
    return float(np.std(x[keep] - slope * t[keep] - intercept, ddof=1))


def step_sd(anomalies, step_index: int) -> float:
    """Noise around a change point: the pooled standard deviation either side of ``step_index``.

    Use this, not ``detrended_sd``, for a change point. Fitting a straight line
    through a step leaves part of the step in the residual, which inflates the
    noise and understates the effect.
    """
    x = np.asarray(anomalies, dtype=float)
    before, after = x[:step_index], x[step_index:]
    before, after = before[~np.isnan(before)], after[~np.isnan(after)]
    if before.size < 2 or after.size < 2:
        raise ValueError("need at least 2 values either side of the change point")
    pooled = (np.sum((before - before.mean()) ** 2) + np.sum((after - after.mean()) ** 2)) / (before.size + after.size - 2)
    return float(np.sqrt(pooled))


def standardised_effect(sigmas: float, half: float) -> float:
    """Map a non-negative effect in sigma to 0..1. Rises steadily, never reaches 1, ``half`` gives 0.5."""
    s = abs(float(sigmas))
    return s / (s + half)


def trend_certainty(slope: float, ci_low: float, ci_high: float) -> float:
    """1 / (1 + relative half-width of the interval). A zero slope scores 0."""
    if slope == 0:
        return 0.0
    return 1.0 / (1.0 + (ci_high - ci_low) / 2.0 / abs(slope))


def correlation_certainty(correlation: float, months: float, ci_level: float = CORRELATION_CI_LEVEL) -> float:
    """Certainty of a correlation, built the same way as ``trend_certainty``.

    The interval on the correlation comes from Fisher's z transform:
    ``atanh(r)`` is close to normal with standard error ``1 / sqrt(months - 3)``.
    The score is 1 / (1 + relative half-width of that interval), so a weak
    correlation or a short record scores low and nothing reaches 1.

    This replaced a score based on the p-value, which hit 1 for almost any
    cascade over 25 years and so told findings apart no better than a constant.
    """
    if correlation == 0 or months <= 3:
        return 0.0
    r = min(abs(float(correlation)), 0.999999)
    half_z = norm.ppf(1 - (1 - ci_level) / 2) / math.sqrt(months - 3)
    low, high = math.tanh(math.atanh(r) - half_z), math.tanh(math.atanh(r) + half_z)
    return 1.0 / (1.0 + (high - low) / 2.0 / r)


def trend_robustness(result: TrendResult) -> float:
    """Robustness of a trend from its ``TrendResult``: does the cross-check back the trend?

    The cross-check is the trend-free pre-whitening test, with the same alpha
    the trend test was run at. See ``graded_robustness`` for the scale.
    """
    return graded_robustness(result.robust, result.robust_p_value, result.parameters["alpha"])


def graded_robustness(agree: bool, cross_check_p: float, alpha: float) -> float:
    """How strongly a cross-check supports a trend, from 0 to 1.

    When the cross-check passes at ``alpha``: 1 if it agrees with the primary
    test (``agree``), 0 if it does not (it found the opposite sign, or the
    primary test found nothing).

    When it does not pass: log(p) / log(alpha), which is 1 at ``p = alpha`` and
    falls smoothly to 0 at ``p = 1``. With alpha 0.05, p = 0.06 scores 0.94,
    p = 0.2 scores 0.54 and p = 0.5 scores 0.23. A plain pass-or-fail score
    would swing the total by its whole weight between p = 0.049 and p = 0.051.
    """
    if cross_check_p < alpha:
        return 1.0 if agree else 0.0
    return clip01(math.log(cross_check_p) / math.log(alpha))


def change_point_robustness(date_months: float, alt_date_months: float | None) -> float:
    """Compare the change point at the main penalty with the one at a second penalty.

    Dates are in months (any common origin). ``None`` means the second setting
    found no change point near it: 0. A gap of ``DATE_TOLERANCE_MONTHS`` or more scores 0.
    """
    if alt_date_months is None:
        return 0.0
    return clip01(1.0 - abs(date_months - alt_date_months) / DATE_TOLERANCE_MONTHS)


def cascade_robustness(correlations_by_lag, lag: int) -> float:
    """How well the correlation holds one month either side of the chosen lag.

    The mean of the neighbours' correlations as a share of the peak, counting a
    neighbour with the opposite sign as 0. With no neighbours it is 0.
    """
    r = list(correlations_by_lag)
    peak = r[lag]
    if peak == 0:
        return 0.0
    shares = [clip01(r[i] / peak) for i in (lag - 1, lag + 1) if 0 <= i < len(r)]
    return float(np.mean(shares)) if shares else 0.0


def months_between(a: str, b: str) -> int:
    """Whole months from date string ``a`` to ``b`` (``YYYY-MM-DD``)."""
    da, db = date.fromisoformat(a), date.fromisoformat(b)
    return (db.year - da.year) * 12 + (db.month - da.month)


def cross_system_scores(findings: list[dict]) -> dict[str, float]:
    """Cross-system part for every finding id, from the cell it sits in.

    Counts the distinct variables that have a significant finding in the same
    cell. A cascade contributes both of its variables. A finding that is not
    significant scores 0 whatever its neighbours do.
    """
    in_cell: dict[str, set[str]] = {}
    for f in findings:
        if f["significant"]:
            in_cell.setdefault(f["cell"]["id"], set()).update(v["id"] for v in f["variables"])
    top = len(CROSS_SYSTEM_BY_VARIABLES) - 1
    return {
        f["id"]: CROSS_SYSTEM_BY_VARIABLES[min(len(in_cell[f["cell"]["id"]]) - 1, top)] if f["significant"] else 0.0
        for f in findings
    }


# ------------------------------------------------------------------ one finding

def _need_sd(evidence: Evidence, finding_id: str) -> float:
    if evidence.noise_sd is None or not math.isfinite(evidence.noise_sd) or evidence.noise_sd <= 0:
        raise ValueError(f"{finding_id}: Evidence.noise_sd is needed and must be positive")
    return evidence.noise_sd


def effect_and_certainty(finding: dict, evidence: Evidence) -> tuple[float, float]:
    """The effect_size and certainty parts, by kind of finding."""
    kind, fid = finding["kind"], finding["id"]
    if kind == "trend":
        r = finding["result"]
        effect = standardised_effect(r["slope_per_decade"] / _need_sd(evidence, fid), TREND_HALF_SIGMA)
        certainty = trend_certainty(r["slope_per_decade"], r["ci_low"], r["ci_high"])
    elif kind == "change_point":
        c = finding["change_point"]
        effect = standardised_effect(c["shift"] / _need_sd(evidence, fid), STEP_HALF_SIGMA)
        width = months_between(c["date_low"], c["date_high"])
        certainty = 1.0 / (1.0 + width / DATE_HALF_MONTHS)
    elif kind == "cascade":
        c = finding["cascade"]
        effect = abs(c["correlation"])
        months = evidence.effective_months or finding["preprocessing"]["months_present"]
        certainty = correlation_certainty(c["correlation"], months)
    elif kind == "contradiction":
        c = finding["contradiction"]
        # Half the gap between the two slopes: the average size of the two opposite
        # trends. The full gap would score a contradiction twice as high as either
        # of the trends it is made from.
        half_gap = abs(c["slope_per_decade"] - c["paired_slope_per_decade"]) / 2.0
        effect = standardised_effect(half_gap / _need_sd(evidence, fid), TREND_HALF_SIGMA)
        if evidence.certainty is None:
            raise ValueError(f"{fid}: Evidence.certainty is needed for a contradiction")
        certainty = evidence.certainty
    else:
        raise ValueError(f"{fid}: unknown kind {kind!r}")
    return clip01(effect), clip01(certainty)


def score_finding(finding: dict, evidence: Evidence, cross_system: float, weights: dict | None = None) -> dict:
    """The ``score`` block for one finding: five parts and their weighted ``total``.

    Parts are rounded to 4 places first and the total is built from the rounded
    parts, so anyone recomputing it from the file gets the same number.
    """
    weights = weights or WEIGHTS
    if set(weights) != set(PART_NAMES):
        raise ValueError(f"weights must name exactly {PART_NAMES}")
    if abs(sum(weights.values()) - 1.0) > 1e-9:
        raise ValueError("weights must sum to 1")
    effect, certainty = effect_and_certainty(finding, evidence)
    parts = {
        "effect_size": effect,
        "certainty": certainty,
        "coverage": clip01(finding["preprocessing"]["coverage"]),
        "robustness": clip01(evidence.robustness),
        "cross_system": clip01(cross_system),
    }
    parts = {name: round(value, 4) for name, value in parts.items()}
    total = round(sum(weights[name] * parts[name] for name in PART_NAMES), 4)
    return {"total": total, **parts}


# ------------------------------------------------------------------ the whole list

def _group(finding: dict) -> str:
    """What a finding counts against in the cap: its variable, or ``cascade``."""
    return "cascade" if finding["kind"] == "cascade" else finding["variables"][0]["id"]


def rank_findings(
    findings: list[dict],
    evidence: dict[str, Evidence],
    weights: dict | None = None,
    diversify: bool = True,
    top_n: int = TOP_N,
    max_share: float = MAX_GROUP_SHARE,
) -> list[dict]:
    """Score every finding, order them and set ``score`` and ``rank``. Returns the new list.

    ``evidence`` maps finding id to its ``Evidence``. Input dicts are not changed.

    Order: significant findings first, then by ``score.total`` (highest first),
    then by id, so a tie never depends on the order the findings arrived in.

    With ``diversify`` on, no group (one variable, or the cascades) may hold more
    than ``max_share`` of the first ``top_n`` places among significant findings.
    A finding that would break the cap waits until after the window, keeping its
    order. If too few groups exist to fill the window the cap is relaxed, so
    nothing is dropped. This is what stops temperature crowding out the rest.
    Note the result is then not strictly in ``score.total`` order.
    """
    cross = cross_system_scores(findings)
    scored = []
    for f in findings:
        if f["id"] not in evidence:
            raise ValueError(f"{f['id']}: no Evidence given for this finding")
        scored.append({**f, "score": score_finding(f, evidence[f["id"]], cross[f["id"]], weights)})
    scored.sort(key=lambda f: (not f["significant"], -f["score"]["total"], f["id"]))

    if diversify:
        significant = [f for f in scored if f["significant"]]
        others = [f for f in scored if not f["significant"]]
        cap = max(1, math.ceil(max_share * top_n))
        used: dict[str, int] = {}
        head: list[dict] = []
        for f in significant:
            if len(head) >= top_n:
                break
            if used.get(_group(f), 0) < cap:
                head.append(f)
                used[_group(f)] = used.get(_group(f), 0) + 1
        chosen = {f["id"] for f in head}
        scored = head + [f for f in significant if f["id"] not in chosen] + others

    for position, f in enumerate(scored, start=1):
        f["rank"] = position
    return scored
