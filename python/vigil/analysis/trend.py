"""Trend and significance for one monthly series.

This is the module to read when checking the statistics. It does three things:

1. ``deseasonalise`` turns raw monthly values into anomalies (each value minus
   the mean of its own calendar month).
2. ``sens_slope`` gives Sen's slope and its confidence interval, using the
   real month numbers so that missing months do not shift the slope.
3. ``trend_test`` runs the Mann-Kendall test with the Hamed-Rao correction for
   autocorrelation (the primary test) and trend-free pre-whitening (the
   robustness check), and reports both.

Nothing here downloads data or knows about grid cells. A series goes in and a
``TrendResult`` comes out.
"""

from __future__ import annotations

import warnings
from dataclasses import dataclass

import numpy as np
import pymannkendall as mk
from scipy.stats import norm

MONTHS_PER_DECADE = 120

# Hamed-Rao looks at autocorrelation up to this many months. Capping it avoids
# the instability that comes from using every lag (see brief note A19).
DEFAULT_MAX_LAG = 12

PRIMARY_TEST = "Mann-Kendall, Hamed-Rao variance correction"
ROBUSTNESS_TEST = "Mann-Kendall, trend-free pre-whitening"


def deseasonalise(values, start_month: int = 1) -> np.ndarray:
    """Return anomalies: each value minus the mean of its calendar month.

    ``values`` is a monthly series with NaN for a missing month. ``start_month``
    is the calendar month (1 to 12) of the first value. A calendar month with no
    values at all stays NaN. The result has the same length as the input.
    """
    x = np.asarray(values, dtype=float)
    if x.ndim != 1:
        raise ValueError("values must be one-dimensional")
    if not 1 <= start_month <= 12:
        raise ValueError("start_month must be between 1 and 12")

    month_index = (start_month - 1 + np.arange(x.size)) % 12
    climatology = np.full(12, np.nan)
    for m in range(12):
        in_month = x[month_index == m]
        in_month = in_month[~np.isnan(in_month)]
        if in_month.size:
            climatology[m] = in_month.mean()
    return x - climatology[month_index]


def sens_slope(values, ci_level: float = 0.95, var_s: float | None = None):
    """Sen's slope per month, with a confidence interval.

    The slope is the median of the slopes between every pair of points, using
    each point's real month number, so a gap in the series is handled correctly.

    The interval follows Gilbert (1987): rank the pairwise slopes and take the
    ones that sit ``z * sqrt(var_s) / 2`` either side of the middle. ``var_s``
    is the variance of the Mann-Kendall score. Pass the Hamed-Rao corrected
    variance and the interval widens to match the autocorrelation. If it is not
    given, the uncorrected variance for the series is used.

    Returns ``(slope, ci_low, ci_high)``, all per month.
    """
    x = np.asarray(values, dtype=float)
    t = np.arange(x.size, dtype=float)
    keep = ~np.isnan(x)
    x, t = x[keep], t[keep]
    n = x.size
    if n < 4:
        raise ValueError("need at least 4 non-missing values")

    i, j = np.triu_indices(n, k=1)
    slopes = np.sort((x[j] - x[i]) / (t[j] - t[i]))
    n_pairs = slopes.size
    slope = float(np.median(slopes))

    if var_s is None:
        var_s = _variance_s(x)
    c = norm.ppf(1 - (1 - ci_level) / 2) * np.sqrt(var_s)
    low_rank = int(np.floor((n_pairs - c) / 2))
    high_rank = int(np.ceil((n_pairs + c) / 2))
    low = slopes[min(max(low_rank, 0), n_pairs - 1)]
    high = slopes[min(max(high_rank, 0), n_pairs - 1)]
    return slope, float(low), float(high)


def _variance_s(x: np.ndarray) -> float:
    """Variance of the Mann-Kendall score, with the usual tie correction."""
    n = x.size
    _, counts = np.unique(x, return_counts=True)
    ties = float(np.sum(counts * (counts - 1) * (2 * counts + 5)))
    return (n * (n - 1) * (2 * n + 5) - ties) / 18.0


@dataclass(frozen=True)
class TrendResult:
    """What the trend test found for one series."""

    slope_per_decade: float
    ci_low: float
    ci_high: float
    ci_level: float
    p_value: float
    months_present: int
    months_expected: int
    test: str
    parameters: dict
    robust_p_value: float
    robust_test: str
    # True when the primary and the robustness tests agree on whether the
    # trend is significant at ``alpha`` and on its sign.
    robust: bool

    @property
    def coverage(self) -> float:
        return self.months_present / self.months_expected

    def direction(self, p_adjusted: float, threshold: float = 0.05) -> str:
        """``increasing`` or ``decreasing`` when significant after correction, else ``none``."""
        if p_adjusted >= threshold or self.slope_per_decade == 0:
            return "none"
        return "increasing" if self.slope_per_decade > 0 else "decreasing"

    def result_block(self, p_adjusted: float, units: str, threshold: float = 0.05) -> dict:
        """The ``result`` block of a trend finding, in the shape the contract expects."""
        return {
            "direction": self.direction(p_adjusted, threshold),
            "slope_per_decade": self.slope_per_decade,
            "units": units,
            "ci_low": self.ci_low,
            "ci_high": self.ci_high,
            "ci_level": self.ci_level,
            "p_value": self.p_value,
            "p_adjusted": p_adjusted,
        }


def trend_test(
    anomalies,
    alpha: float = 0.05,
    ci_level: float = 0.95,
    max_lag: int = DEFAULT_MAX_LAG,
) -> TrendResult:
    """Test one monthly anomaly series for a trend.

    ``anomalies`` is a monthly series with NaN for a missing month. The primary
    p-value is the Mann-Kendall test with the Hamed-Rao correction. The
    trend-free pre-whitening p-value is kept beside it as a robustness check.

    The tests need a continuous series, so missing months are dropped before
    they run. The slope and its interval use the real month numbers, and the
    ``months_present`` count says how many months the test actually saw.
    """
    x = np.asarray(anomalies, dtype=float)
    present = x[~np.isnan(x)]
    if present.size < 10:
        raise ValueError("need at least 10 non-missing months to test a trend")

    with warnings.catch_warnings():
        # A negative corrected variance makes the library warn; it is handled below.
        warnings.simplefilter("ignore", RuntimeWarning)
        corrected = mk.hamed_rao_modification_test(present, alpha=alpha, lag=max_lag)
    robust = mk.trend_free_pre_whitening_modification_test(present, alpha=alpha)

    # The Hamed-Rao correction is meant to widen the variance when the series is
    # positively autocorrelated. When the autocorrelation is strongly negative
    # it can shrink the variance, and in the extreme make it negative, which
    # leaves no p-value at all. We never let it fall below the plain
    # Mann-Kendall variance. That is conservative: the test can only become
    # less ready to call a trend, never more.
    plain_variance = _variance_s(present)
    floored = not np.isfinite(corrected.var_s) or corrected.var_s < plain_variance
    if floored:
        primary = mk.original_test(present, alpha=alpha)
        var_s = plain_variance
    else:
        primary = corrected
        var_s = float(corrected.var_s)

    slope, low, high = sens_slope(x, ci_level=ci_level, var_s=var_s)

    primary_sig = primary.p < alpha
    robust_sig = robust.p < alpha
    same_sign = np.sign(primary.slope) == np.sign(robust.slope)
    agree = (primary_sig == robust_sig) and (same_sign or not primary_sig)

    return TrendResult(
        slope_per_decade=slope * MONTHS_PER_DECADE,
        ci_low=low * MONTHS_PER_DECADE,
        ci_high=high * MONTHS_PER_DECADE,
        ci_level=ci_level,
        p_value=float(primary.p),
        months_present=int(present.size),
        months_expected=int(x.size),
        test=PRIMARY_TEST,
        parameters={
            "alpha": alpha,
            "max_lag_months": max_lag,
            "ci_level": ci_level,
            "variance_inflation": round(var_s / plain_variance, 3),
            "variance_correction_floored": floored,
        },
        robust_p_value=float(robust.p),
        robust_test=ROBUSTNESS_TEST,
        robust=bool(agree),
    )
