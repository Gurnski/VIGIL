"""Fetches a NASA product month by month and puts each month on the scan grid.

    python -m vigil.pipeline.download vegetation

For every month of the period it downloads that month's file, reads the one field the scan
uses, turns fill values into NaN, puts it in the right unit, regrids it and keeps the result
as a small file under ``data/scan/months/<variable>/``. The large download is deleted as soon
as its month is saved, so the run needs little disk and can be stopped and started again: a
month already saved is skipped. When every month has been tried it writes
``data/scan/<variable>.npy``, shaped ``(rows, columns, months)``, with NaN for any month the
archive does not have.

It needs a NASA Earthdata Login in the environment variables ``EARTHDATA_USERNAME`` and
``EARTHDATA_PASSWORD``, or in a ``.netrc`` file. The three products held by GES DISC
(temperature, precipitation, soil moisture) also need "NASA GESDISC DATA ARCHIVE" approved
under Applications in the Earthdata Login profile, or every download fails with a EULA error.
"""

from __future__ import annotations

import calendar
import sys
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Callable

import numpy as np

from vigil.api.document import month_dates
from vigil.pipeline import regrid
from vigil.pipeline.build import check
from vigil.pipeline.grid import GRID, MONTHS, PERIOD_START, REPO, SCAN_FOLDER

RAW_FOLDER = REPO / "data" / "raw"
#: Files fetched at once. Each batch is processed and deleted before the next starts.
BATCH = 6

#: A native field: values (NaN for missing), and the latitude and longitude of its cell centres.
Field = tuple[np.ndarray, np.ndarray, np.ndarray]


@dataclass(frozen=True)
class Product:
    variable: str
    short_name: str
    version: str | None
    read: Callable[[Path, str], Field]


def read_vegetation(path: Path, month: str) -> Field:
    """MOD13C2: NDVI on a 0.05 degree grid, north row first, stored as whole numbers times 10,000."""
    from pyhdf.SD import SD, SDC

    file = SD(str(path), SDC.READ)
    try:
        dataset = file.select("CMG 0.05 Deg Monthly NDVI")
        attributes = dataset.attributes()
        stored = dataset.get().astype(float)
    finally:
        file.end()
    low, high = attributes["valid_range"]
    values = np.where((stored == attributes["_FillValue"]) | (stored < low) | (stored > high), np.nan,
                      stored / attributes["scale_factor"])       # MODIS divides by its scale factor
    rows, columns = values.shape
    latitudes = 90.0 - (np.arange(rows) + 0.5) * (180.0 / rows)
    longitudes = -180.0 + (np.arange(columns) + 0.5) * (360.0 / columns)
    return values, latitudes, longitudes


def _read_netcdf(path: Path, group: str | None, name: str) -> tuple[np.ndarray, dict, object]:
    import netCDF4

    with netCDF4.Dataset(path) as file:
        file.set_auto_maskandscale(False)                # fill values are handled here, in the open
        holder = file[group] if group else file
        variable = holder[name]
        attributes = {key: variable.getncattr(key) for key in variable.ncattrs()}
        coordinates = {key: np.asarray(holder[key][:], dtype=float) for key in ("lat", "lon") if key in holder.variables}
        return np.asarray(variable[:], dtype=float), attributes, (variable.dimensions, coordinates)


def _without_fill(values: np.ndarray, attributes: dict) -> np.ndarray:
    for key in ("_FillValue", "missing_value"):
        if key in attributes:
            values = np.where(np.isclose(values, float(np.ravel(attributes[key])[0])), np.nan, values)
    # MERRA-2 marks missing with 1e15, and nothing we read is that large.
    return np.where(np.abs(values) > 1e14, np.nan, values)


def _lat_lon_field(values: np.ndarray, dimensions: tuple, coordinates: dict) -> Field:
    """Drops the single time step and puts latitude first, whatever order the file uses."""
    values = values[0] if values.ndim == 3 else values
    spatial = [name for name in dimensions if name != "time"]
    if spatial == ["lon", "lat"]:
        values = values.T
    elif spatial != ["lat", "lon"]:
        raise ValueError(f"unexpected dimensions {dimensions}")
    return values, coordinates["lat"], coordinates["lon"]


def read_temperature(path: Path, month: str) -> Field:
    """MERRA-2 M2TMNXSLV: T2M, already in kelvin."""
    values, attributes, (dimensions, coordinates) = _read_netcdf(path, None, "T2M")
    if attributes.get("units") != "K":
        raise ValueError(f"T2M is in {attributes.get('units')!r}, not K")
    return _lat_lon_field(_without_fill(values, attributes), dimensions, coordinates)


def read_precipitation(path: Path, month: str) -> Field:
    """GPM IMERG monthly: a mean rate in mm per hour, turned into mm per month."""
    values, attributes, (dimensions, coordinates) = _read_netcdf(path, "Grid", "precipitation")
    if attributes.get("units") != "mm/hr":
        raise ValueError(f"precipitation is in {attributes.get('units')!r}, not mm/hr")
    field, latitudes, longitudes = _lat_lon_field(_without_fill(values, attributes), dimensions, coordinates)
    year, number = int(month[:4]), int(month[5:7])
    return field * 24.0 * calendar.monthrange(year, number)[1], latitudes, longitudes


def read_soil_moisture(path: Path, month: str) -> Field:
    """GLDAS Noah: water in the top 10 cm of soil, kg per m2. Land only."""
    values, attributes, (dimensions, coordinates) = _read_netcdf(path, None, "SoilMoi0_10cm_inst")
    if attributes.get("units") not in ("kg m-2", "kg/m^2", "kg/m2"):
        raise ValueError(f"soil moisture is in {attributes.get('units')!r}, not kg per m2")
    return _lat_lon_field(_without_fill(values, attributes), dimensions, coordinates)


PRODUCTS = {
    "temperature": Product("temperature", "M2TMNXSLV", None, read_temperature),
    "precipitation": Product("precipitation", "GPM_3IMERGM", "07", read_precipitation),
    "soil_moisture": Product("soil_moisture", "GLDAS_NOAH025_M", "2.1", read_soil_moisture),
    "vegetation": Product("vegetation", "MOD13C2", "061", read_vegetation),
}


def fetch(variable: str) -> np.ndarray:
    import earthaccess

    product = PRODUCTS[variable]
    dates = month_dates(PERIOD_START, MONTHS)
    months_folder = SCAN_FOLDER / "months" / variable
    raw_folder = RAW_FOLDER / variable
    months_folder.mkdir(parents=True, exist_ok=True)
    raw_folder.mkdir(parents=True, exist_ok=True)

    earthaccess.login(strategy="environment") if _has_environment_login() else earthaccess.login()
    search = dict(short_name=product.short_name, temporal=(dates[0], f"{dates[-1][:8]}28"), count=-1)
    if product.version:
        search["version"] = product.version
    by_month: dict[str, object] = {}
    for granule in earthaccess.search_data(**search):
        start = granule["umm"]["TemporalExtent"]["RangeDateTime"]["BeginningDateTime"][:7] + "-01"
        by_month.setdefault(start, granule)
    print(f"{variable}: the archive has {sum(d in by_month for d in dates)} of {len(dates)} months", flush=True)

    todo = [d for d in dates if d in by_month and not (months_folder / f"{d[:7]}.npy").exists()]
    started = time.time()
    for at in range(0, len(todo), BATCH):
        batch = todo[at:at + BATCH]
        files = earthaccess.download([by_month[d] for d in batch], str(raw_folder))
        for file in files:
            path = Path(file)
            month = _month_of(path, batch, by_month)
            values, latitudes, longitudes = product.read(path, month)
            np.save(months_folder / f"{month[:7]}.npy", regrid.to_scan_grid(values, latitudes, longitudes).astype(np.float32))
            path.unlink()
        done = at + len(batch)
        rate = (time.time() - started) / done
        print(f"{variable}: {done} of {len(todo)} months, about {rate * (len(todo) - done) / 60:.0f} minutes left", flush=True)

    array = np.full((GRID["rows"], GRID["columns"], len(dates)), np.nan)
    for index, month in enumerate(dates):
        saved = months_folder / f"{month[:7]}.npy"
        if saved.exists():
            array[:, :, index] = np.load(saved)
    check(variable, array)
    np.save(SCAN_FOLDER / f"{variable}.npy", array)
    missing = [d[:7] for i, d in enumerate(dates) if not np.isfinite(array[:, :, i]).any()]
    print(f"{variable}: wrote {SCAN_FOLDER / (variable + '.npy')}. Months with no data: {missing or 'none'}", flush=True)
    return array


def _has_environment_login() -> bool:
    import os

    return bool(os.environ.get("EARTHDATA_USERNAME") and os.environ.get("EARTHDATA_PASSWORD"))


def _month_of(file, batch: list[str], by_month: dict) -> str:
    """Which month of the batch a downloaded file belongs to, by its name."""
    name = Path(file).name
    for month in batch:
        if any(Path(link).name == name for link in by_month[month].data_links()):
            return month
    raise ValueError(f"{name} does not belong to any month asked for")


if __name__ == "__main__":
    if len(sys.argv) < 2 or any(name not in PRODUCTS for name in sys.argv[1:]):
        sys.exit(f"usage: python -m vigil.pipeline.download <{'|'.join(PRODUCTS)}> ...")
    for name in sys.argv[1:]:
        fetch(name)
