"""Checks that the trend tests get made-up series right.

Run from the ``python/`` folder:  python -m pytest vigil/analysis/tests -v

Each statistical case runs the test on 300 fresh series with a fixed seed, so a
single lucky run cannot pass it. The limits were chosen before the first run:
at a 5% significance level, 300 flat series should give about 15 false alarms,
and a result outside roughly 3% to 10% means something is wrong.
"""

import numpy as np
import pymannkendall as mk
import pytest

from vigil.analysis.fdr import adjust_by_family, benjamini_hochberg
from vigil.analysis.synthetic import make_series
from vigil.analysis.trend import (
    TrendResult,
    deseasonalise,
    sens_slope,
    trend_test,
)

RUNS = 300
MONTHS = 300  # 25 years, as in the scan
ALPHA = 0.05


def run_trials(seed, **series_options):
    """Test RUNS fresh series and return the TrendResult for each."""
    rng = np.random.default_rng(seed)
    results = []
    for _ in range(RUNS):
        raw = make_series(MONTHS, rng=rng, **series_options)
        results.append(trend_test(deseasonalise(raw), alpha=ALPHA))
    return results


def share_significant(results):
    return float(np.mean([r.p_value < ALPHA for r in results]))


def interval_coverage(results, true_slope_per_decade):
    return float(np.mean([r.ci_low <= true_slope_per_decade <= r.ci_high for r in results]))


# ---------------------------------------------------------------- the four cases


@pytest.fixture(scope="module")
def flat_noise():
    """Case 1: a flat line with noise. There is no trend to find."""
    return run_trials(seed=1, trend_per_decade=0.0, noise_sd=1.0)


@pytest.fixture(scope="module")
def trending():
    """Case 2: the same line with a real trend of 0.5 per decade."""
    return run_trials(seed=2, trend_per_decade=0.5, noise_sd=1.0)


@pytest.fixture(scope="module")
def flat_autocorrelated():
    """Case 3: a flat line whose noise carries on from month to month (AR1 = 0.5)."""
    return run_trials(seed=3, trend_per_decade=0.0, noise_sd=1.0, ar1=0.5)


def test_flat_noise_gives_about_five_percent_false_alarms(flat_noise):
    rate = share_significant(flat_noise)
    assert 0.03 <= rate <= 0.10, f"false-alarm rate {rate:.3f}, expected about 0.05"


def test_flat_noise_interval_contains_zero_about_ninety_five_percent(flat_noise):
    coverage = interval_coverage(flat_noise, 0.0)
    assert coverage >= 0.90, f"interval covers the true slope {coverage:.3f} of the time"


def test_real_trend_is_found_and_has_the_right_size(trending):
    found = share_significant(trending)
    assert found >= 0.95, f"trend found in only {found:.3f} of runs"
    slopes = np.array([r.slope_per_decade for r in trending])
    assert abs(slopes.mean() - 0.5) < 0.05, f"mean slope {slopes.mean():.3f}, expected 0.5"
    assert all(r.ci_low < r.slope_per_decade < r.ci_high for r in trending)


def test_real_trend_interval_contains_the_truth_about_ninety_five_percent(trending):
    coverage = interval_coverage(trending, 0.5)
    assert coverage >= 0.90, f"interval covers the true slope {coverage:.3f} of the time"


def test_autocorrelation_correction_matters(flat_autocorrelated):
    """Plain Mann-Kendall is fooled by carried-on noise. The corrected test must not be."""
    corrected = share_significant(flat_autocorrelated)

    rng = np.random.default_rng(3)  # the same series as the fixture
    plain_alarms = 0
    for _ in range(RUNS):
        anomalies = deseasonalise(make_series(MONTHS, rng=rng, ar1=0.5))
        plain_alarms += mk.original_test(anomalies, alpha=ALPHA).p < ALPHA
    plain = plain_alarms / RUNS

    assert plain > 0.20, f"plain Mann-Kendall false-alarm rate {plain:.3f}; the case is not testing anything"
    assert corrected <= 0.12, f"corrected false-alarm rate {corrected:.3f}, plain was {plain:.3f}"


def test_autocorrelated_interval_still_contains_zero(flat_autocorrelated):
    coverage = interval_coverage(flat_autocorrelated, 0.0)
    assert coverage >= 0.88, f"interval covers the true slope {coverage:.3f} of the time"


# ---------------------------------------------------------------- deseasonalising


def test_deseasonalise_removes_the_yearly_cycle():
    raw = make_series(MONTHS, seasonal_amplitude=10.0, noise_sd=0.1, rng=np.random.default_rng(4))
    anomalies = deseasonalise(raw)
    by_month = anomalies.reshape(-1, 12).mean(axis=0)
    assert np.abs(by_month).max() < 1e-9
    assert anomalies.std() < 0.2  # the cycle (std about 7) is gone


def test_a_big_seasonal_cycle_does_not_hide_a_trend():
    raw = make_series(
        MONTHS, trend_per_decade=0.5, seasonal_amplitude=10.0, noise_sd=0.5, rng=np.random.default_rng(5)
    )
    result = trend_test(deseasonalise(raw))
    assert result.p_value < 0.001
    assert abs(result.slope_per_decade - 0.5) < 0.1


def test_deseasonalise_respects_the_start_month_and_missing_months():
    # Two years starting in July. Calendar month means are 1..12 plus a constant.
    start_month = 7
    values = np.array([((start_month - 1 + i) % 12) + 1 for i in range(24)], dtype=float)
    values[3] = np.nan
    anomalies = deseasonalise(values, start_month=start_month)
    assert np.isnan(anomalies[3])
    assert np.allclose(anomalies[~np.isnan(anomalies)], 0.0)
    assert anomalies.shape == values.shape


def test_deseasonalise_leaves_an_empty_calendar_month_as_nan():
    values = np.arange(24, dtype=float)
    values[[2, 14]] = np.nan  # both Marches missing
    anomalies = deseasonalise(values)
    assert np.isnan(anomalies[[2, 14]]).all()


# ---------------------------------------------------------------- Sen's slope and gaps


def test_sens_slope_matches_pymannkendall_when_nothing_is_missing():
    x = make_series(MONTHS, trend_per_decade=0.7, rng=np.random.default_rng(6))
    ours, _, _ = sens_slope(x)
    theirs = mk.original_test(x).slope
    assert ours == pytest.approx(theirs, rel=1e-9)


def test_missing_months_do_not_distort_the_slope():
    rng = np.random.default_rng(7)
    raw = make_series(MONTHS, trend_per_decade=0.5, noise_sd=0.5, missing_fraction=0.08, rng=rng)
    assert np.isnan(raw).sum() > 0
    result = trend_test(deseasonalise(raw))
    assert result.months_present == int((~np.isnan(raw)).sum())
    assert result.months_expected == MONTHS
    assert abs(result.slope_per_decade - 0.5) < 0.1


def test_too_short_a_series_is_refused():
    with pytest.raises(ValueError):
        trend_test(np.arange(5, dtype=float))


# ---------------------------------------------------------------- the contract's result block


def test_result_block_has_the_fields_the_contract_asks_for():
    x = make_series(MONTHS, trend_per_decade=0.5, rng=np.random.default_rng(8))
    result = trend_test(deseasonalise(x))
    block = result.result_block(p_adjusted=0.001, units="K per decade")
    assert set(block) == {
        "direction", "slope_per_decade", "units", "ci_low", "ci_high", "ci_level", "p_value", "p_adjusted",
    }
    assert block["direction"] == "increasing"
    assert result.result_block(p_adjusted=0.5, units="K per decade")["direction"] == "none"
    assert isinstance(result, TrendResult)


# ---------------------------------------------------------------- false-discovery rate


def test_fdr_keeps_false_discoveries_low_across_many_flat_tests():
    """1000 flat cells plus 100 real trends: very few of the flat cells should be called significant."""
    rng = np.random.default_rng(9)
    p = np.concatenate([rng.uniform(size=1000), rng.uniform(0, 1e-4, size=100)])
    adjusted, significant = benjamini_hochberg(p, alpha=0.05)
    false_discoveries = significant[:1000].sum()
    assert significant[1000:].all()
    assert false_discoveries <= 0.05 * significant.sum() + 3
    assert np.all(adjusted >= p)


def test_fdr_ignores_skipped_tests():
    adjusted, significant = benjamini_hochberg([0.001, np.nan, 0.8, 0.002])
    assert np.isnan(adjusted[1]) and not significant[1]
    assert significant[0] and significant[3] and not significant[2]


def test_families_are_corrected_separately():
    p = np.array([0.01, 0.20, 0.30, 0.04])
    together, _ = benjamini_hochberg(p)
    split, _ = adjust_by_family(p, ["a", "a", "b", "b"])
    assert not np.allclose(together, split)
    # Family "b" is [0.30, 0.04]. Smallest p is rank 1 of 2, so 0.04 * 2 / 1 = 0.08.
    assert split[3] == pytest.approx(0.08)
