"""Gathers the regridded variables into the one file the scan reads.

    python -m vigil.pipeline.build                 every variable there is an array for
    python -m vigil.pipeline.build earthquakes     only the ones named

Each variable is an array of shape ``(rows, columns, months)`` saved as
``data/scan/<variable>.npy``. Earthquakes need no download: they are binned here from the
catalogue in the repository. The result is ``data/scan/scan-data.npz``, in the layout
``vigil.api.document.ScanData`` describes. Then:

    python -m vigil.api.document ../data/scan/scan-data.npz ../data/scan/snapshot.json
    python ../contract/tools/check.py ../data/scan/snapshot.json
"""

from __future__ import annotations

import sys

import numpy as np

from vigil.api.document import ScanData
from vigil.pipeline import regrid
from vigil.pipeline.earthquakes import bin_earthquakes
from vigil.pipeline.grid import GRID, MONTHS, PERIOD_START, SCAN_FOLDER, land_mask

#: The four variables that come from downloads, in the order the apps list them.
DOWNLOADED = ["temperature", "precipitation", "soil_moisture", "vegetation"]

# Values outside these mean a fill value or a wrong unit is still in the data.
PLAUSIBLE = {
    "temperature": (150.0, 350.0),        # K
    "precipitation": (0.0, 3000.0),       # mm per month
    "soil_moisture": (0.0, 150.0),        # kg per m2 in the top 10 cm; GLDAS reaches 100
    "vegetation": (-0.3, 1.0),            # NDVI
    "earthquakes": (0.0, 1e5),            # events per month
}


def check(variable: str, array: np.ndarray) -> None:
    """Stops on a wrong shape, an infinity or a value no real measurement could have."""
    expected = (GRID["rows"], GRID["columns"], MONTHS)
    if array.shape != expected:
        raise ValueError(f"{variable}: shape is {array.shape}, not {expected}")
    if np.isinf(array).any():
        raise ValueError(f"{variable}: holds infinity. A missing value must be NaN.")
    low, high = PLAUSIBLE[variable]
    finite = array[np.isfinite(array)]
    if finite.size and (finite.min() < low or finite.max() > high):
        raise ValueError(f"{variable}: values run from {finite.min():.4g} to {finite.max():.4g}, outside {low} to {high}. "
                         "A fill value or a wrong unit is probably still in the data.")


def build(variables: list[str] | None = None) -> ScanData:
    SCAN_FOLDER.mkdir(parents=True, exist_ok=True)
    wanted = variables or [v for v in DOWNLOADED if (SCAN_FOLDER / f"{v}.npy").exists()] + ["earthquakes"]
    data: dict[str, np.ndarray] = {}
    for variable in wanted:
        if variable == "earthquakes":
            array = bin_earthquakes()
        else:
            path = SCAN_FOLDER / f"{variable}.npy"
            if not path.exists():
                raise FileNotFoundError(f"{path} is missing: {variable} has not been downloaded and regridded yet")
            array = np.load(path)
        check(variable, array)
        data[variable] = array

    climate = [v for v in data if v != "earthquakes"]
    # The land mask goes on the climate variables only. Earthquakes happen at sea, so they get none.
    mask = {variable: land_mask() for variable in climate} or None
    return ScanData(data=data, grid=dict(GRID), period_start=PERIOD_START, mask=mask,
                    regridding=regrid.RULE, synthetic=False)


if __name__ == "__main__":
    scan_data = build(sys.argv[1:] or None)
    out = SCAN_FOLDER / "scan-data.npz"
    scan_data.save(out)
    for variable, array in scan_data.data.items():
        present = np.isfinite(array).mean()
        print(f"{variable:14} {present:6.1%} of values present, mean {np.nanmean(array):.4g}")
    print(f"Wrote {out}")
