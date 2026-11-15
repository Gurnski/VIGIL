"""Fetches a NASA product month by month and puts each month on the scan grid.

    python -m vigil.pipeline.download vegetation
    python -m vigil.pipeline.download retest.precipitation

For every month of the period it downloads that month's file, reads the one field the scan
uses, turns fill values into NaN, puts it in the right unit, regrids it and keeps the result
as a small file under ``data/scan/months/<name>/``. The large download is deleted as soon
as its month is saved, so the run needs little disk and can be stopped and started again: a
month already saved is skipped. When every month has been tried it writes the array, shaped
``(rows, columns, months)``, with NaN for any month the archive does not have.

There are two kinds of product, and they are kept apart on disk:

* A scan variable, such as ``vegetation``. Its array is ``data/scan/<variable>.npy``, which
  ``build`` gathers into the file the scan reads.
* A second product for a scan variable, named ``retest.<variable>``, such as Aqua's NDVI for
  Terra's. Its array is ``data/scan/retest/<variable>.npy``. It never enters the scan: the
  re-test step (``vigil.api.retest``) uses it to ask whether a trend the scan found shows up
  in a second record. Its units may differ from the scan variable's, because only the
  direction and the significance of its trend are compared.

It needs a NASA Earthdata Login in the environment variables ``EARTHDATA_USERNAME`` and
``EARTHDATA_PASSWORD``, or in a ``.netrc`` file. The products held by GES DISC (MERRA-2,
IMERG, GLDAS, GPCP) also need "NASA GESDISC DATA ARCHIVE" approved under Applications in the
Earthdata Login profile, or every download fails with a EULA error. GISTEMP needs no login.
"""

from __future__ import annotations

import calendar
import gzip
import sys
import time
import urllib.request
from dataclasses import dataclass
from pathlib import Path
from typing import Callable

import numpy as np

from vigil.api.document import month_dates
from vigil.pipeline import regrid
from vigil.pipeline.build import PLAUSIBLE, check
from vigil.pipeline.grid import GRID, MONTHS, PERIOD_START, REPO, RETEST_FOLDER, SCAN_FOLDER

RAW_FOLDER = REPO / "data" / "raw"
#: Files fetched at once. Each batch is processed and deleted before the next starts.
BATCH = 6

#: Files an archive may send beside a data file, describing it. They hold no data and are not read.
SIDECARS = (".xml", ".met", ".jpg", ".png", ".txt")

#: A native field: values (NaN for missing), and the latitude and longitude of its cell centres.
Field = tuple[np.ndarray, np.ndarray, np.ndarray]


@dataclass(frozen=True)
class Product:
    """One product, and where its array goes.

    ``variable`` is the scan variable it is, or is a second record of. ``pattern`` is text
    a file's name must hold, for an archive that keeps several resolutions and periods under
    one short name. ``retest`` marks a second product, kept out of the scan. ``plausible``
    is the range its values must fall in; left out, the scan variable's own range is used.
    """

    variable: str
    short_name: str
    version: str | None
    read: Callable[[Path, str], Field]
    pattern: str | None = None
    retest: bool = False
    plausible: tuple[float, float] | None = None

    @property
    def name(self) -> str:
        return f"retest.{self.variable}" if self.retest else self.variable

    @property
    def array_path(self) -> Path:
        return (RETEST_FOLDER if self.retest else SCAN_FOLDER) / f"{self.variable}.npy"

    @property
    def limits(self) -> tuple[float, float]:
        return self.plausible or PLAUSIBLE[self.variable]


# ----- Readers -----


def _cmg_grid(shape: tuple[int, int]) -> tuple[np.ndarray, np.ndarray]:
    """Cell centres of a MODIS climate modelling grid: the whole globe, north row first, west column first."""
    rows, columns = shape
    latitudes = 90.0 - (np.arange(rows) + 0.5) * (180.0 / rows)
    longitudes = -180.0 + (np.arange(columns) + 0.5) * (360.0 / columns)
    return latitudes, longitudes


def _read_hdf4(path: Path, *names: str) -> list[tuple[np.ndarray, dict]]:
    from pyhdf.SD import SD, SDC

    file = SD(str(path), SDC.READ)
    try:
        return [(file.select(name).get(), file.select(name).attributes()) for name in names]
    finally:
        file.end()


#: MOD13C2 and MYD13C2 pixel reliability: 0 is good data and 1 is marginal data. 2 is snow or
#: ice and 3 is cloud, and under either the index is not a measurement of vegetation.
USABLE_RELIABILITY = (0, 1)


def read_vegetation(path: Path, month: str) -> Field:
    """MOD13C2 or MYD13C2: NDVI on a 0.05 degree grid, stored as whole numbers times 10,000.

    Only pixels the product itself ranks as good or marginal are kept (``USABLE_RELIABILITY``).
    """
    (stored, attributes), (reliability, _) = _read_hdf4(path, "CMG 0.05 Deg Monthly NDVI", "CMG 0.05 Deg Monthly pixel reliability")
    stored = stored.astype(float)
    low, high = attributes["valid_range"]
    unusable = (stored == attributes["_FillValue"]) | (stored < low) | (stored > high) | ~np.isin(reliability, USABLE_RELIABILITY)
    values = np.where(unusable, np.nan, stored / attributes["scale_factor"])      # MODIS divides by its scale factor
    return (values, *_cmg_grid(values.shape))


def read_snow_cover(path: Path, month: str) -> Field:
    """MOD10CM: the share of each 0.05 degree cell under snow that month, 0 to 100 percent.

    Values above 100 are codes, not shares (night, cloud, no decision, water, fill), and are missing.
    """
    ((stored, attributes),) = _read_hdf4(path, "Snow_Cover_Monthly_CMG")
    low, high = attributes["valid_range"]
    if (low, high) != (0, 100):
        raise ValueError(f"snow cover's valid range is {low} to {high}, not 0 to 100")
    stored = stored.astype(float)
    values = np.where((stored < low) | (stored > high), np.nan, stored)
    return (values, *_cmg_grid(values.shape))


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


def _days_in(month: str) -> int:
    return calendar.monthrange(int(month[:4]), int(month[5:7]))[1]


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
    return field * 24.0 * _days_in(month), latitudes, longitudes


def read_precipitation_gpcp(path: Path, month: str) -> Field:
    """GPCP monthly, the satellite and gauge estimate: a mean rate in mm per day, turned into mm per month."""
    values, attributes, (dimensions, coordinates) = _read_netcdf(path, None, "sat_gauge_precip")
    if attributes.get("units") != "mm/day":
        raise ValueError(f"GPCP precipitation is in {attributes.get('units')!r}, not mm/day")
    field, latitudes, longitudes = _lat_lon_field(_without_fill(values, attributes), dimensions, coordinates)
    return field * _days_in(month), latitudes, longitudes


def read_soil_moisture(path: Path, month: str) -> Field:
    """GLDAS Noah: water in the top 10 cm of soil, kg per m2. Land only."""
    values, attributes, (dimensions, coordinates) = _read_netcdf(path, None, "SoilMoi0_10cm_inst")
    if attributes.get("units") not in ("kg m-2", "kg/m^2", "kg/m2"):
        raise ValueError(f"soil moisture is in {attributes.get('units')!r}, not kg per m2")
    return _lat_lon_field(_without_fill(values, attributes), dimensions, coordinates)


def read_soil_wetness_merra(path: Path, month: str) -> Field:
    """MERRA-2 M2TMNXLND: GWETTOP, the wetness of the surface soil layer as a share of saturation, 0 to 1. Land only."""
    values, attributes, (dimensions, coordinates) = _read_netcdf(path, None, "GWETTOP")
    if attributes.get("units") != "1":
        raise ValueError(f"GWETTOP is in {attributes.get('units')!r}, not a plain share")
    return _lat_lon_field(_without_fill(values, attributes), dimensions, coordinates)


def _scaled(values: np.ndarray, attributes: dict) -> np.ndarray:
    """Stored whole numbers turned into the unit, after fill values and anything outside the valid range are dropped."""
    values = _without_fill(values, attributes)
    low, high = attributes.get("valid_min"), attributes.get("valid_max")
    if low is not None:
        values = np.where(values < float(low), np.nan, values)
    if high is not None:
        values = np.where(values > float(high), np.nan, values)
    return values * float(attributes.get("scale_factor", 1.0)) + float(attributes.get("add_offset", 0.0))


#: Aqua MODIS sea surface temperature quality levels: 0 is best and 1 is good. Above that the
#: retrieval is questionable (cloud, glint, a steep viewing angle).
USABLE_SST_QUALITY = 1


def read_sea_surface_temperature(path: Path, month: str) -> Field:
    """Aqua MODIS monthly sea surface temperature (daytime, 11 micron), 9 km, degrees Celsius. Ocean only."""
    values, attributes, (dimensions, coordinates) = _read_netcdf(path, None, "sst")
    if attributes.get("units") != "degree_C":
        raise ValueError(f"sea surface temperature is in {attributes.get('units')!r}, not degree_C")
    quality, quality_attributes, _ = _read_netcdf(path, None, "qual_sst")
    values = np.where(_without_fill(quality, quality_attributes) <= USABLE_SST_QUALITY, _scaled(values, attributes), np.nan)
    return _lat_lon_field(values, dimensions, coordinates)


def read_chlorophyll(path: Path, month: str) -> Field:
    """Aqua MODIS monthly chlorophyll-a concentration, 9 km, mg per m3. Ocean only."""
    values, attributes, (dimensions, coordinates) = _read_netcdf(path, None, "chlor_a")
    if attributes.get("units") != "mg m^-3":
        raise ValueError(f"chlorophyll is in {attributes.get('units')!r}, not mg m^-3")
    return _lat_lon_field(_scaled(values, attributes), dimensions, coordinates)


PRODUCTS = {product.name: product for product in (
    Product("temperature", "M2TMNXSLV", None, read_temperature),
    Product("precipitation", "GPM_3IMERGM", "07", read_precipitation),
    Product("soil_moisture", "GLDAS_NOAH025_M", "2.1", read_soil_moisture),
    Product("vegetation", "MOD13C2", "061", read_vegetation),
    Product("snow_cover", "MOD10CM", "61", read_snow_cover),
    Product("sea_surface_temperature", "MODISA_L3m_SST", None, read_sea_surface_temperature, pattern=".L3m.MO.SST.sst.9km."),
    Product("chlorophyll", "MODISA_L3m_CHL", None, read_chlorophyll, pattern=".L3m.MO.CHL.chlor_a.9km."),
    # Second products, for re-testing what the scan found. See the module docstring.
    Product("precipitation", "GPCPMON", "3.3", read_precipitation_gpcp, retest=True),
    Product("soil_moisture", "M2TMNXLND", None, read_soil_wetness_merra, retest=True, plausible=(0.0, 1.0)),
    Product("vegetation", "MYD13C2", "061", read_vegetation, retest=True),
)}

#: GISTEMP is one file for the whole record, with no login, so it has its own fetch.
GISTEMP = "retest.temperature"
GISTEMP_URL = "https://data.giss.nasa.gov/pub/gistemp/gistemp1200_GHCNv4_ERSSTv5.nc.gz"
#: Anomalies from the 1951 to 1980 mean, in kelvin. Nothing on Earth strays this far from it for a month.
GISTEMP_PLAUSIBLE = (-30.0, 30.0)


# ----- Fetching -----


def _latest(granules: list) -> object:
    """Of several records for one month, the one produced last. The file name settles a tie, so the choice never varies."""
    def produced(granule) -> tuple[str, str]:
        stamp = granule["umm"].get("DataGranule", {}).get("ProductionDateTime") or granule["meta"].get("revision-date", "")
        return str(stamp), Path(granule.data_links()[0]).name
    return max(granules, key=produced)


def fetch(name: str) -> np.ndarray:
    if name == GISTEMP:
        return fetch_gistemp()
    import earthaccess

    product = PRODUCTS[name]
    dates = month_dates(PERIOD_START, MONTHS)
    months_folder = SCAN_FOLDER / "months" / name
    raw_folder = RAW_FOLDER / name
    months_folder.mkdir(parents=True, exist_ok=True)
    raw_folder.mkdir(parents=True, exist_ok=True)

    earthaccess.login(strategy="environment") if _has_environment_login() else earthaccess.login()
    search = dict(short_name=product.short_name, temporal=(dates[0], f"{dates[-1][:8]}28"), count=-1)
    if product.version:
        search["version"] = product.version
    found: dict[str, list] = {}
    for granule in earthaccess.search_data(**search):
        if product.pattern and not any(product.pattern in Path(link).name for link in granule.data_links()):
            continue
        start = granule["umm"]["TemporalExtent"]["RangeDateTime"]["BeginningDateTime"][:7] + "-01"
        found.setdefault(start, []).append(granule)
    by_month = {month: _latest(granules) for month, granules in found.items()}
    print(f"{name}: the archive has {sum(d in by_month for d in dates)} of {len(dates)} months", flush=True)

    todo = [d for d in dates if d in by_month and not (months_folder / f"{d[:7]}.npy").exists()]
    started = time.time()
    for at in range(0, len(todo), BATCH):
        batch = todo[at:at + BATCH]
        files = earthaccess.download([by_month[d] for d in batch], str(raw_folder))
        for file in files:
            path = Path(file)
            if path.suffix.lower() in SIDECARS:
                path.unlink()               # a description of the data file beside it, which some archives send too
                continue
            month = _month_of(path, batch, by_month)
            values, latitudes, longitudes = product.read(path, month)
            np.save(months_folder / f"{month[:7]}.npy", regrid.to_scan_grid(values, latitudes, longitudes).astype(np.float32))
            path.unlink()
        done = at + len(batch)
        rate = (time.time() - started) / done
        print(f"{name}: {done} of {len(todo)} months, about {rate * (len(todo) - done) / 60:.0f} minutes left", flush=True)

    array = np.full((GRID["rows"], GRID["columns"], len(dates)), np.nan)
    for index, month in enumerate(dates):
        saved = months_folder / f"{month[:7]}.npy"
        if saved.exists():
            array[:, :, index] = np.load(saved)
    return _finish(name, array, product.array_path, product.limits, dates)


def fetch_gistemp() -> np.ndarray:
    """GISTEMP v4: the station and ship record, as monthly anomalies on a 2 degree grid, smoothed over 1,200 km."""
    import netCDF4

    raw_folder = RAW_FOLDER / GISTEMP
    raw_folder.mkdir(parents=True, exist_ok=True)
    plain = raw_folder / "gistemp1200_GHCNv4_ERSSTv5.nc"
    if not plain.exists():
        request = urllib.request.Request(GISTEMP_URL, headers={"User-Agent": "python-urllib"})
        with urllib.request.urlopen(request, timeout=300) as reply:
            plain.write_bytes(gzip.decompress(reply.read()))

    dates = month_dates(PERIOD_START, MONTHS)
    array = np.full((GRID["rows"], GRID["columns"], len(dates)), np.nan)
    with netCDF4.Dataset(plain) as file:
        file.set_auto_maskandscale(False)
        variable = file["tempanomaly"]
        if variable.getncattr("units") != "K":
            raise ValueError(f"GISTEMP anomalies are in {variable.getncattr('units')!r}, not K")
        attributes = {key: variable.getncattr(key) for key in variable.ncattrs()}
        latitudes, longitudes = np.asarray(file["lat"][:], dtype=float), np.asarray(file["lon"][:], dtype=float)
        times = netCDF4.num2date(file["time"][:], file["time"].units, only_use_cftime_datetimes=False)
        index_of = {f"{moment.year:04d}-{moment.month:02d}-01": i for i, moment in enumerate(times)}
        for index, month in enumerate(dates):
            if month in index_of:
                stored = np.asarray(variable[index_of[month]], dtype=float)
                values = _without_fill(stored, attributes) * float(attributes.get("scale_factor", 1.0))
                array[:, :, index] = regrid.to_scan_grid(values, latitudes, longitudes)
    print(f"{GISTEMP}: the file has {sum(d in index_of for d in dates)} of {len(dates)} months", flush=True)
    return _finish(GISTEMP, array, RETEST_FOLDER / "temperature.npy", GISTEMP_PLAUSIBLE, dates)


def _finish(name: str, array: np.ndarray, path: Path, limits: tuple[float, float], dates: list[str]) -> np.ndarray:
    check(name, array, limits)
    path.parent.mkdir(parents=True, exist_ok=True)
    np.save(path, array)
    missing = [d[:7] for i, d in enumerate(dates) if not np.isfinite(array[:, :, i]).any()]
    print(f"{name}: wrote {path}. Months with no data: {missing or 'none'}", flush=True)
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
    names = [*PRODUCTS, GISTEMP]
    if len(sys.argv) < 2 or any(name not in names for name in sys.argv[1:]):
        sys.exit(f"usage: python -m vigil.pipeline.download <{'|'.join(names)}> ...")
    for name in sys.argv[1:]:
        fetch(name)
