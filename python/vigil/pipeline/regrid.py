"""Puts a product's own latitude and longitude grid onto the scan grid.

Each scan cell takes the area-weighted mean of the native cells whose centres fall inside it.
A native cell's area goes with the cosine of its latitude. Missing values (NaN) are left out,
and a scan cell with too small a share of valid native cells is missing itself, so that a
coastline cell with a sliver of land does not get a value from three pixels.

This is the rule written into every finding's ``preprocessing.regridding``: see ``RULE``.
"""

from __future__ import annotations

import numpy as np

from vigil.pipeline.grid import GRID

#: The least share of a scan cell's native cells, by area, that must hold a value.
MIN_VALID_SHARE = 0.5

RULE = (f"Area-weighted mean of the product's own cells inside each {GRID['cell_degrees']} degree cell, "
        f"weighted by the cosine of latitude, ignoring missing values. A cell is missing when under "
        f"{MIN_VALID_SHARE:.0%} of it holds a value.")


def to_scan_grid(values: np.ndarray, latitudes: np.ndarray, longitudes: np.ndarray,
                 min_valid_share: float = MIN_VALID_SHARE) -> np.ndarray:
    """Regrids one field. ``values`` is ``(len(latitudes), len(longitudes))`` with NaN for missing.

    ``latitudes`` and ``longitudes`` are the native cell centres in degrees, in any order.
    Longitudes may run -180 to 180 or 0 to 360. Returns ``(rows, columns)`` on the scan grid,
    row 0 the southernmost and column 0 the westernmost.
    """
    values = np.asarray(values, dtype=float)
    latitudes = np.asarray(latitudes, dtype=float)
    longitudes = np.asarray(longitudes, dtype=float)
    if values.shape != (latitudes.size, longitudes.size):
        raise ValueError(f"values are {values.shape} but there are {latitudes.size} latitudes and {longitudes.size} longitudes")
    if np.isinf(values).any():
        raise ValueError("the field holds infinity: a fill value has not been turned into NaN")

    rows, columns, size = GRID["rows"], GRID["columns"], GRID["cell_degrees"]
    longitudes = (longitudes + 180.0) % 360.0 - 180.0            # 0 to 360 becomes -180 to 180
    # A cell holds its lower edges; latitude 90 and longitude 180 go in the last row and column.
    row = np.clip(np.floor((latitudes - GRID["lat_min"]) / size), 0, rows - 1).astype(int)
    column = np.clip(np.floor((longitudes - GRID["lon_min"]) / size), 0, columns - 1).astype(int)

    weight = np.cos(np.radians(latitudes))[:, None] * np.ones(longitudes.size)[None, :]
    valid = np.isfinite(values)
    flat = (row[:, None] * columns + column[None, :]).ravel()
    weighted = np.bincount(flat, weights=np.where(valid, values * weight, 0.0).ravel(), minlength=rows * columns)
    valid_weight = np.bincount(flat, weights=np.where(valid, weight, 0.0).ravel(), minlength=rows * columns)
    all_weight = np.bincount(flat, weights=weight.ravel(), minlength=rows * columns)

    with np.errstate(invalid="ignore", divide="ignore"):
        mean = weighted / valid_weight
        share = valid_weight / all_weight
    mean[~(share >= min_valid_share) | (valid_weight <= 0)] = np.nan
    return mean.reshape(rows, columns)
