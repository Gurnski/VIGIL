# pipeline

Fetches the NASA products, regrids each one onto the shared grid and monthly steps, and hands the result to the scan as one file. Ethan owns this folder (see `COORDINATION.md`). Dan wrote the pieces below on the Saturday evening so that real data could go through the whole path.

Part of the discovery run. Section 07 of `Project_Brief.pdf` sets out the steps.

## What is here

| File | What it is |
|---|---|
| `grid.py` | The scan grid, the period and the land mask |
| `regrid.py` | Puts a product's own grid onto the scan grid: area-weighted mean, missing values left out |
| `download.py` | Fetches a NASA product month by month, reads the one field the scan uses, fixes units and fill values, and regrids it |
| `earthquakes.py` | Bins the USGS catalogue in `data/usgs_earthquakes/` to events per cell and month. No download needed. |
| `build.py` | Checks each variable's array and gathers them into `data/scan/scan-data.npz`, the file the scan reads |
| `tests/` | The mask, the regridder and the earthquake binning |

## Where things stand (Saturday 14 November, late evening)

| Variable | Product | Download | State |
|---|---|---|---|
| `vegetation` | Terra MODIS `MOD13C2` v061 | 29 GB | Done on Dan's laptop: all 300 months. Carried between machines as `data/scan/vegetation.npy`. |
| `precipitation` | GPM IMERG `GPM_3IMERGM` v07 | 5 GB | Not downloaded. Reader checked on one real month. |
| `soil_moisture` | GLDAS Noah `GLDAS_NOAH025_M` v2.1 | 7 GB | Not downloaded. Reader checked on one real month. |
| `temperature` | MERRA-2 `M2TMNXSLV`, field `T2M` | 18 GB | Not downloaded. Reader checked on one real month. |
| `earthquakes` | USGS catalogue, in the repository | none | Works. Scanned end to end into a checked results document. |

IMERG's monthly product stops at September 2025, so its last three months are missing. At 297 of 300 months it still clears the 90% coverage rule. `grid.PERIOD_NOTE` records this. Dan and Will have not yet said whether to end the period early for every variable instead.

## Setting up a machine

You need Git and Python 3.12. From the repository root:

```
cd python
py -3.12 -m venv .venv
.venv\Scripts\python -m pip install -r requirements.txt
.venv\Scripts\python -m pytest vigil/pipeline vigil/api -q
```

The tests should pass before anything else is run.

## The NASA login

The downloads need a free Earthdata Login, given in two environment variables. In PowerShell, for that window only:

```
$env:EARTHDATA_USERNAME = "the username"
$env:EARTHDATA_PASSWORD = 'the password'
```

Use single quotes round the password if it contains a `$`. Never put the login in a file in the repository.

The three products held by GES DISC (temperature, precipitation, soil moisture) also need "NASA GESDISC DATA ARCHIVE" approved on the account, under Applications, Authorized Apps at urs.earthdata.nasa.gov. Without it every file fails with a EULA error. Dan's account has it approved.

## Downloading

```
.venv\Scripts\python -m vigil.pipeline.download precipitation soil_moisture temperature
```

Name one variable or several. For each month it downloads that month's file, saves the regridded month as a small file under `data/scan/months/<variable>/`, and deletes the download. So it needs under 1 GB of disk at a time, and it can be stopped and started again: a month already saved is skipped. When every month has been tried it writes `data/scan/<variable>.npy`, shaped `(72, 144, 300)`.

If vegetation has not been carried over as `data/scan/vegetation.npy`, add `vegetation` to that command.

A reader stops with a clear error if a file's unit or layout is not what it expects. That is the check working. Do not edit it to get past the error without finding out why.

## From the arrays to a results document

```
.venv\Scripts\python -m vigil.pipeline.build
.venv\Scripts\python -m vigil.api.document ..\data\scan\scan-data.npz ..\data\scan\snapshot.json
.venv\Scripts\python ..\contract\tools\check.py ..\data\scan\snapshot.json
```

1. `build` takes every variable that has an array in `data/scan/`, adds the earthquakes, checks each for wrong shapes, infinities and impossible values, and writes `scan-data.npz`. Name variables after it to include only those.
2. `document` runs the whole analysis (a few minutes) and writes the results document.
3. `check.py` must print `OK`.

## Before anyone trusts the results

A file that passes `check.py` is well-formed. That does not make the science right. Look at these first (they are from Will's note on using the analysis):

- The share of tests skipped for `coverage`. A large share means missing data, not oceans.
- The share that is significant, per variable. If one variable is almost entirely significant, suspect the data (a jump where two versions were stitched, or a unit change partway), not the climate.
- Whether many change points fall in the same month. That usually means an instrument or product-version change.
- Whether the top 20 findings sit in plausible places.
- Earthquake findings should nearly all be rising, which is a warning about detection, and their limitations text says so.

## Showing the results

Put the checked file at the root of the `keepvigil.earth` bucket as `snapshot.json`. The web viewer shows it on the next page load, with no rebuild, and a deploy never overwrites it. To serve it from the API, set `VIGIL_SNAPSHOT` to it and `VIGIL_SCAN_DATA` to `scan-data.npz` (see `python/vigil/api/README.md`).

## What is not done

- Quality flags are not used for vegetation: fill values and out-of-range values are dropped, and nothing else. The brief asks for quality flags to be considered.
- The land mask is the rough one from the contract's fixture.
- Nothing runs this on a schedule or in the cloud. It is run by hand on one machine.

Downloaded data and everything under `data/scan/` and `data/raw/` never go in the repository. `.gitignore` keeps them out.
