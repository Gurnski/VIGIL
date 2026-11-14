"""Test helper: bin the USGS earthquake catalogue onto the scan grid.

This exists so the scan can be tested against the real catalogue in the repo
(``data/usgs_earthquakes/m45/earthquakes.csv``). It is not the pipeline's
binning. Ethan's pipeline owns that, and must produce the same array: float,
shape ``(rows, columns, months)``, row 0 the southernmost, column 0 the
westernmost, the value the number of earthquakes in that cell that month.

The convention to match, so cells line up with the other variables:

* a cell holds its lower edges and excludes its upper ones, so latitude 0 belongs
  to the row above the equator and 87.5 to the row starting at 87.5;
* latitude 90 and longitude 180 sit on the outer edge and go in the last row
  and the last column;
* the month is the UTC month of the event's ``time``.
"""

from __future__ import annotations

from pathlib import Path

import numpy as np

REPO = Path(__file__).resolve().parents[4]
CATALOGUE = REPO / "data" / "usgs_earthquakes" / "m45" / "earthquakes.csv"


def bin_earthquakes(
    csv_path: Path = CATALOGUE,
    *,
    rows: int = 72,
    columns: int = 144,
    cell_degrees: float = 2.5,
    lat_min: float = -90.0,
    lon_min: float = -180.0,
    first_year: int = 2001,
    months: int = 300,
    min_magnitude: float = 4.5,
) -> np.ndarray:
    """Count earthquakes per cell and month. Returns floats of shape ``(rows, columns, months)``."""
    import pandas as pd  # a dependency of statsmodels, so always present

    table = pd.read_csv(csv_path, usecols=["time", "latitude", "longitude", "mag", "type"])
    table = table[(table["type"] == "earthquake") & (table["mag"] >= min_magnitude)]
    time = pd.to_datetime(table["time"], utc=True)
    month = (time.dt.year - first_year) * 12 + time.dt.month - 1
    row = np.floor((table["latitude"] - lat_min) / cell_degrees).clip(0, rows - 1)
    column = np.floor((table["longitude"] - lon_min) / cell_degrees).clip(0, columns - 1)

    keep = ((month >= 0) & (month < months)).to_numpy()
    counts = np.zeros((rows, columns, months))
    np.add.at(
        counts,
        (row.to_numpy()[keep].astype(int), column.to_numpy()[keep].astype(int), month.to_numpy()[keep].astype(int)),
        1.0,
    )
    return counts
