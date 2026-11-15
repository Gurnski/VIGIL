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

## Where things stand (Sunday 15 November, early morning)

Everything below has been downloaded and regridded on Dan's home PC, and scanned end to end into a checked results document.

The scan's variables:

| Variable | Product | Download | Months |
|---|---|---|---|
| `temperature` | MERRA-2 `M2TMNXSLV`, field `T2M` | 18 GB | 300 of 300 |
| `precipitation` | GPM IMERG `GPM_3IMERGM` v07 | 5 GB | 297. The archive stops at September 2025. |
| `soil_moisture` | GLDAS Noah `GLDAS_NOAH025_M` v2.1 | 7 GB | 300 |
| `vegetation` | Terra MODIS `MOD13C2` v061, good and marginal pixels only | 29 GB | 300 |
| `snow_cover` | Terra MODIS `MOD10CM` v61 | 0.4 GB | 295. Five months are not in the archive. |
| `sea_surface_temperature` | Aqua MODIS `MODISA_L3m_SST`, monthly, 9 km, quality 0 and 1 | 2 GB | 281. The record starts in August 2002. |
| `chlorophyll` | Aqua MODIS `MODISA_L3m_CHL`, monthly, 9 km | 5 GB | 282. The record starts in July 2002. |
| `earthquakes` | USGS catalogue, in the repository | none | 300 |

The second products, which never enter the scan (see "Second products" below):

| For | Product | Download |
|---|---|---|
| `temperature` and `sea_surface_temperature` | GISTEMP v4, `gistemp1200_GHCNv4_ERSSTv5`. One file, no login. | 60 MB |
| `precipitation` | GPCP `GPCPMON` v3.3, field `sat_gauge_precip` | 2 GB |
| `soil_moisture` | MERRA-2 land `M2TMNXLND`, field `GWETTOP` | 13 GB |
| `vegetation` | Aqua MODIS `MYD13C2` v061, good and marginal pixels only | 27 GB |

IMERG's last three months are missing. At 297 of 300 months it still clears the 90% coverage rule, and `grid.PERIOD_NOTE` records it. Dan and Will have not yet said whether to end the period early for every variable instead. The two Aqua ocean records start in mid 2002, so they have 94% of the months at most, which also clears the rule.

The first scan, of five variables with vegetation read without quality flags, is kept under `data/scan/v1/` on that machine.

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

The products held by GES DISC (MERRA-2, IMERG, GLDAS and GPCP) also need "NASA GESDISC DATA ARCHIVE" approved on the account, under Applications, Authorized Apps at urs.earthdata.nasa.gov. Without it every file fails with a EULA error. Dan's account has it approved. The MODIS land, snow and ocean products came through on the same login with nothing more to approve. GISTEMP needs no login.

## Downloading

```
.venv\Scripts\python -m vigil.pipeline.download precipitation soil_moisture temperature
```

Name one product or several. Run it with no name to see the list. For each month it downloads that month's file, saves the regridded month as a small file under `data/scan/months/<name>/`, and deletes the download. So it needs under 1 GB of disk at a time, and it can be stopped and started again: a month already saved is skipped. When every month has been tried it writes the array, shaped `(72, 144, 300)`, as `data/scan/<variable>.npy`.

Run each product as its own process. Four at once shared one home connection without errors, at about 45 GB an hour between them.

A reader stops with a clear error if a file's unit or layout is not what it expects. That is the check working. Do not edit it to get past the error without finding out why.

Three things the readers do that are worth knowing:

- **Vegetation keeps only the pixels MODIS ranks as good or marginal.** Pixels ranked as snow, ice or cloud are dropped (`USABLE_RELIABILITY` in `download.py`). The first scan did not do this. It costs coverage where winters are snowy: 1,355 cells have enough months to test, against 2,133 without the filter, and most of the cells lost are north of 30°N.
- **A cell needs half its area to hold a value.** A 2.5° cell with valid pixels over less than half of it is missing that month (`regrid.MIN_VALID_SHARE`). For the ocean products this also leaves out cells that are mostly land.
- **Where an archive lists a month twice, the file produced last is used**, so the choice never varies between runs.

## Second products

```
.venv\Scripts\python -m vigil.pipeline.download retest.temperature retest.precipitation retest.soil_moisture retest.vegetation
```

A product named `retest.<variable>` is a second record of a scan variable. It is regridded the same way and saved apart, as `data/scan/retest/<variable>.npy`, so `build` never picks it up. The re-test step (`python -m vigil.api.retest`, see `python/vigil/api/README.md`) uses it to ask whether a trend the scan found shows up in a second record. Its units can differ from the scan variable's, because only the direction and the significance of its trend are compared.

Each has been checked against the scan's own record, month by month, in every cell the scan tested. The median correlation of their monthly anomalies is 0.97 for precipitation, 0.92 for vegetation, 0.89 for temperature, 0.77 for sea surface temperature and 0.50 for soil moisture. So each is reading the same thing as the record it re-tests, and the two soil moisture models agree least. `docs/submission/checks/track.py` is the script, and `final-run.log` beside it has what it printed.

## From the arrays to a results document

```
.venv\Scripts\python -m vigil.pipeline.build
.venv\Scripts\python -m vigil.api.document ..\data\scan\scan-data.npz ..\data\scan\scan.json
.venv\Scripts\python -m vigil.api.retest ..\data\scan\scan.json ..\data\scan\retest ..\data\scan\snapshot.json
.venv\Scripts\python ..\contract\tools\check.py ..\data\scan\snapshot.json
```

1. `build` takes every variable that has an array in `data/scan/`, adds the earthquakes, checks each for wrong shapes, infinities and impossible values, and writes `scan-data.npz`. Name variables after it to include only those. The land variables get the land mask. The ocean variables and the earthquakes get none.
2. `document` runs the whole analysis and writes the results document. Eight variables take about four minutes.
3. `retest` tests each trend again on its variable's second product and writes the document back with what it found. It takes under two minutes and changes nothing the scan computed.
4. `check.py` must print `OK`.

`snapshot.json` and `scan-data.npz` are a pair. The service must be given both from the same run, or a zone and a recompute will be about different data from the findings on screen.

`docs/submission/checks/final-run.sh` runs all four steps and then the checks in that folder.

## Before anyone trusts the results

A file that passes `check.py` is well-formed. That does not make the science right. Look at these first (they are from Will's note on using the analysis):

- The share of tests skipped for `coverage`. A large share means missing data, not oceans.
- The share that is significant, per variable. If one variable is almost entirely significant, suspect the data (a jump where two versions were stitched, or a unit change partway), not the climate.
- Whether many change points fall in the same month. That usually means an instrument or product-version change.
- Whether the top 20 findings sit in plausible places.
- Earthquake findings should nearly all be rising, which is a warning about detection, and their limitations text says so.
- What the re-test says, variable by variable. A variable whose trends its second product often contradicts is the one to trust least, whatever its p-values.

## Showing the results

The public site is a bucket of files with no service behind it. Write out what it needs, then upload it:

```
.venv\Scripts\python -m vigil.api.export ..\data\scan\snapshot.json ..\data\scan\scan-data.npz ..\data\site
```

That makes `data/site/` with the snapshot (and a compressed copy), a series file for every finding's chart and each variable year by year. "A website with no service" in `python/vigil/api/README.md` has the three upload commands, and says why the snapshot must go up compressed. The web viewer shows the new scan on the next page load, with no rebuild, and a deploy never overwrites it. The next deploy also puts the snapshot into the app's download, so the exe opens on the real findings.

To serve it from the API, set `VIGIL_SNAPSHOT` to it and `VIGIL_SCAN_DATA` to `scan-data.npz` (see `python/vigil/api/README.md`).

## What is not done

- The land mask is the rough one from the contract's fixture.
- Snow cover, sea surface temperature and chlorophyll were added on the Saturday night, after the first scan's results had been seen. They went through the same rules as the first five, but they were not part of the scan as first declared.
- Chlorophyll and snow cover have no second product, so their findings cannot be re-tested.
- Nothing runs this on a schedule or in the cloud. It is run by hand on one machine.

Downloaded data and everything under `data/scan/` and `data/raw/` never go in the repository. `.gitignore` keeps them out.
