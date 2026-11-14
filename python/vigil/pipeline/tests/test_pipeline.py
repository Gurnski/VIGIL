"""The pipeline's pieces that need no download: the grid, the mask, the regridder and the earthquake binning."""

from __future__ import annotations

import importlib.util
from pathlib import Path

import numpy as np
import pytest

from vigil.pipeline import regrid
from vigil.pipeline.build import check
from vigil.pipeline.earthquakes import CATALOGUE, bin_earthquakes
from vigil.pipeline.grid import GRID, MONTHS, land_mask


def native_grid(step: float, north_first: bool = False, zero_to_360: bool = False):
    latitudes = np.arange(-90 + step / 2, 90, step)
    longitudes = np.arange(-180 + step / 2, 180, step)
    if north_first:
        latitudes = latitudes[::-1]
    if zero_to_360:
        longitudes = np.sort(longitudes % 360)
    return latitudes, longitudes


def test_the_mask_has_land_ocean_and_ice_the_right_way_up():
    mask = land_mask()
    assert mask.shape == (GRID["rows"], GRID["columns"])
    assert set(np.unique(mask[mask != None])) == {"ocean", "ice"}      # noqa: E711
    assert (mask[:4] == "ice").mean() > 0.5, "Antarctica is at the south, in the first rows"
    # London, about 51.5 N 0.1 W, is land; the middle of the Pacific is not.
    assert mask[56, 71] is None
    assert mask[36, 10] == "ocean"


def test_a_constant_field_stays_constant():
    latitudes, longitudes = native_grid(0.5)
    out = regrid.to_scan_grid(np.full((latitudes.size, longitudes.size), 7.0), latitudes, longitudes)
    assert out.shape == (GRID["rows"], GRID["columns"])
    assert np.allclose(out, 7.0)


def test_orientation_does_not_depend_on_how_the_file_is_laid_out():
    latitudes, longitudes = native_grid(0.5)
    field = latitudes[:, None] + 0.01 * longitudes[None, :]
    expected = regrid.to_scan_grid(field, latitudes, longitudes)

    flipped_lat, _ = native_grid(0.5, north_first=True)
    assert np.allclose(regrid.to_scan_grid(field[::-1], flipped_lat, longitudes), expected)

    # The same field in a file whose longitudes run 0 to 360.
    order = np.argsort(longitudes % 360)
    assert np.allclose(regrid.to_scan_grid(field[:, order], latitudes, (longitudes % 360)[order]), expected)

    # Row 0 is the south, column 0 the west.
    assert expected[0, 0] < expected[-1, 0] and expected[0, 0] < expected[0, -1]


def test_the_mean_is_weighted_by_area():
    latitudes, longitudes = native_grid(0.5)
    field = np.tile(latitudes[:, None], (1, longitudes.size))
    out = regrid.to_scan_grid(field, latitudes, longitudes)
    inside = latitudes[(latitudes >= 60) & (latitudes < 62.5)]
    weights = np.cos(np.radians(inside))
    assert out[60, 0] == pytest.approx((inside * weights).sum() / weights.sum())
    assert out[60, 0] < inside.mean(), "the wider, lower-latitude cells count for more"


def test_missing_values_are_left_out_and_a_mostly_empty_cell_is_missing():
    latitudes, longitudes = native_grid(0.5)
    field = np.full((latitudes.size, longitudes.size), 2.0)
    in_cell = (latitudes[:, None] >= 0) & (latitudes[:, None] < 2.5) & (longitudes[None, :] >= 0) & (longitudes[None, :] < 2.5)
    few_missing = field.copy()
    few_missing[np.argwhere(in_cell)[0][0], np.argwhere(in_cell)[0][1]] = np.nan
    assert regrid.to_scan_grid(few_missing, latitudes, longitudes)[36, 72] == pytest.approx(2.0)

    mostly_missing = field.copy()
    mostly_missing[in_cell] = np.nan
    keep = np.argwhere(in_cell)[0]
    mostly_missing[keep[0], keep[1]] = 2.0
    assert np.isnan(regrid.to_scan_grid(mostly_missing, latitudes, longitudes)[36, 72])


def test_a_fill_value_left_as_infinity_is_refused():
    latitudes, longitudes = native_grid(2.5)
    field = np.ones((latitudes.size, longitudes.size))
    field[0, 0] = np.inf
    with pytest.raises(ValueError, match="infinity"):
        regrid.to_scan_grid(field, latitudes, longitudes)


def test_the_plausibility_check_catches_a_fill_value():
    array = np.full((GRID["rows"], GRID["columns"], MONTHS), 280.0)
    check("temperature", array)
    array[0, 0, 0] = 1e15
    with pytest.raises(ValueError, match="fill value"):
        check("temperature", array)


@pytest.mark.skipif(not CATALOGUE.exists(), reason="the earthquake catalogue is not in this checkout")
def test_earthquake_binning_matches_the_analysis_reference():
    spec = importlib.util.spec_from_file_location(
        "quake_grid", Path(__file__).resolve().parents[2] / "analysis" / "tests" / "quake_grid.py")
    reference = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(reference)

    counts = bin_earthquakes()
    assert counts.shape == (GRID["rows"], GRID["columns"], MONTHS)
    assert np.array_equal(counts, reference.bin_earthquakes())
    assert counts.min() == 0 and counts.sum() > 100_000
