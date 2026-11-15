"""The scan grid, the period and the land mask: the things every source is put onto.

The grid and the period are the ones the contract's fixture uses. Dan and Will still owe a
final word on the period (see ``PERIOD_NOTE``).
"""

from __future__ import annotations

from pathlib import Path

import numpy as np

REPO = Path(__file__).resolve().parents[3]

#: 2.5 degree cells, rows from the south, columns from the west.
GRID = {"cell_degrees": 2.5, "rows": 72, "columns": 144, "lat_min": -90.0, "lon_min": -180.0}

PERIOD_START = "2001-01-01"
MONTHS = 300                     # January 2001 to December 2025

#: GPM IMERG's monthly product stops at September 2025 at the time of writing, so its last
#: three months are missing (NaN). At 297 of 300 months it still clears the 90% coverage rule.
PERIOD_NOTE = "GPM IMERG has no data for October to December 2025."

#: Where the regridded arrays are kept between the download and the scan. Never committed.
SCAN_FOLDER = REPO / "data" / "scan"

#: Where the second products are kept, one array per scan variable. They are for re-testing
#: what the scan found and never enter the scan, so they sit apart from its arrays.
RETEST_FOLDER = SCAN_FOLDER / "retest"


def land_mask() -> np.ndarray:
    """The rough land, ice and ocean map the fixture uses, as the scan's mask.

    ``(rows, columns)`` of ``None`` (land: test the cell), ``"ocean"`` or ``"ice"``, row 0 the
    southernmost. The file lists the northernmost row first, so it is reversed here.
    """
    lines = (REPO / "contract" / "tools" / "land_mask.txt").read_text().split()
    if len(lines) != GRID["rows"] or any(len(line) != GRID["columns"] for line in lines):
        raise ValueError("land_mask.txt does not match the scan grid")
    meaning = {".": "ocean", "I": "ice", "#": None}
    mask = np.empty((GRID["rows"], GRID["columns"]), dtype=object)
    for row, line in enumerate(reversed(lines)):
        for column, character in enumerate(line):
            mask[row, column] = meaning[character]
    return mask
