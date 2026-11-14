"""Earthquakes per cell and month, from the USGS catalogue kept in the repository.

The value is the number of events of magnitude 4.5 or more in that cell in that UTC month. A
month with no events is 0, not missing. The cell convention is the one the other variables
use: a cell holds its lower edges and excludes its upper ones, and latitude 90 and longitude
180 go in the last row and column. The logic is the analysis tests' ``bin_earthquakes``,
which Will wrote as the reference for this.
"""

from __future__ import annotations

from pathlib import Path

import numpy as np

from vigil.pipeline.grid import GRID, MONTHS, PERIOD_START, REPO

CATALOGUE = REPO / "data" / "usgs_earthquakes" / "m45" / "earthquakes.csv"
MIN_MAGNITUDE = 4.5


def bin_earthquakes(csv_path: Path = CATALOGUE, months: int = MONTHS) -> np.ndarray:
    """Counts as floats of shape ``(rows, columns, months)``, from ``PERIOD_START``."""
    import pandas as pd

    rows, columns, size = GRID["rows"], GRID["columns"], GRID["cell_degrees"]
    first_year, first_month = int(PERIOD_START[:4]), int(PERIOD_START[5:7])

    table = pd.read_csv(csv_path, usecols=["time", "latitude", "longitude", "mag", "type"])
    table = table[(table["type"] == "earthquake") & (table["mag"] >= MIN_MAGNITUDE)]
    time = pd.to_datetime(table["time"], utc=True)
    month = (time.dt.year - first_year) * 12 + time.dt.month - first_month
    row = np.floor((table["latitude"] - GRID["lat_min"]) / size).clip(0, rows - 1)
    column = np.floor((table["longitude"] - GRID["lon_min"]) / size).clip(0, columns - 1)

    keep = ((month >= 0) & (month < months)).to_numpy()
    counts = np.zeros((rows, columns, months))
    np.add.at(counts, (row.to_numpy()[keep].astype(int), column.to_numpy()[keep].astype(int),
                       month.to_numpy()[keep].astype(int)), 1.0)
    return counts
