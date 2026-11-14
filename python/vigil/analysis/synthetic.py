"""Made-up monthly series where the right answer is known.

Used to check the statistics before any real data exists. If the tests find a
trend in a flat line of noise, or miss a trend that was put in on purpose, the
method is wrong.
"""

from __future__ import annotations

import numpy as np


def make_series(
    n_months: int = 300,
    *,
    trend_per_decade: float = 0.0,
    noise_sd: float = 1.0,
    ar1: float = 0.0,
    seasonal_amplitude: float = 0.0,
    step_month: int | None = None,
    step_size: float = 0.0,
    baseline: float = 0.0,
    missing_fraction: float = 0.0,
    start_month: int = 1,
    rng: np.random.Generator | None = None,
) -> np.ndarray:
    """Build one monthly series, with NaN for any missing month.

    ``trend_per_decade`` is the true slope, per ten years. ``noise_sd`` is the
    standard deviation of the noise. With ``ar1`` above zero each month's noise
    partly carries on from the last, as real climate anomalies do; the variance
    stays ``noise_sd`` squared whatever ``ar1`` is. ``seasonal_amplitude`` adds a
    yearly cycle. ``step_month`` and ``step_size`` add a sudden change in level.
    """
    if not -1 < ar1 < 1:
        raise ValueError("ar1 must be between -1 and 1")
    rng = rng if rng is not None else np.random.default_rng()

    t = np.arange(n_months)
    series = baseline + trend_per_decade * t / 120.0 + _ar1_noise(n_months, noise_sd, ar1, rng)

    if seasonal_amplitude:
        calendar_month = (start_month - 1 + t) % 12
        series = series + seasonal_amplitude * np.sin(2 * np.pi * calendar_month / 12)
    if step_month is not None:
        series = series + np.where(t >= step_month, step_size, 0.0)
    if missing_fraction:
        series[rng.random(n_months) < missing_fraction] = np.nan
    return series


def _ar1_noise(n: int, sd: float, phi: float, rng: np.random.Generator) -> np.ndarray:
    z = rng.standard_normal(n)
    if phi == 0:
        return sd * z
    noise = np.empty(n)
    noise[0] = sd * z[0]
    innovation_sd = sd * np.sqrt(1 - phi**2)
    for i in range(1, n):
        noise[i] = phi * noise[i - 1] + innovation_sd * z[i]
    return noise
