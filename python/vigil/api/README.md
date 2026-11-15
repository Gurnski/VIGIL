# api

The FastAPI service, and the step before it that turns gridded data into a results document. It serves stored findings to the desktop app, the web viewer and the AI Detective, and runs a fresh analysis on a region when asked. It hands out results and never changes them. Dan owns this folder (see `COORDINATION.md`).

Everything it sends follows the format in `contract/`.

## What is here

| File | What it is |
|---|---|
| `document.py` | From gridded data to a results document: the four analysis calls in order, the descriptive text, the series and the run record. Also the layout of the data file the pipeline hands over (`ScanData`). |
| `app.py` | The service and its calls |
| `zone.py` | `POST /zone`: the cells inside an outline drawn by hand, one series per variable over them, and the trend in each |
| `retest.py` | The step after the scan: tests each trend finding again on a second product for the same variable, and writes what it found into the finding's `standing` |
| `trace.py` | Notes each tool call the Detective makes, from outside it, so `POST /ask` can return them as `steps` |
| `site.py` | The service as a website of its own: the built viewer and the files it asks for, what a presenter has on show, and the limit on questions from other devices |
| `years.py` | `GET /years`: a variable year by year, from the anomalies the scan itself used |
| `export.py` | Writes the files a plain website needs to show a scan with no service behind it: the snapshot, compressed, and a series file for every finding |
| `lambda_handler.py` | Runs the service on AWS Lambda |
| `lambda_pack.py` | Packs the service and its packages into the zip Lambda runs |
| `tests/` | The calls against the fixture, and the whole path from made-up data to a checked document |

It runs no statistical test of its own. Those come from `vigil/analysis/`, and the wording follows Will's note on using it. The arithmetic it does do is plain and is named where it happens: a zone's area-weighted mean (`zone.py`), a year's mean of the anomalies the scan used (`years.py`), and a zone's mean snow cover against a 1% level for the rare-snow warning (`zone.py`).

## Running it

From the `python/` folder, with the packages in `requirements.txt` installed:

```
python -m uvicorn vigil.api.app:app --port 8000
```

With nothing set, it serves the contract's fixture, which is made up and says so. Both apps look for the service at `http://127.0.0.1:8000` on the same machine.

| Setting | What it does |
|---|---|
| `VIGIL_SNAPSHOT` | The stored scan to serve: a file path, an `s3://bucket/key` address or an `https://` one |
| `VIGIL_SCAN_DATA` | The gridded data the scan was made from: a file path, an `s3://bucket/key` address or an `https://` one. With it, `GET /series` works for any cell and `POST /analyse` and `POST /zone` run. |
| `ANTHROPIC_API_KEY` | The Detective's key. Without it `POST /ask` answers 503 and the apps show their marked sample answers. Never in the repository. |
| `VIGIL_CORS_ORIGINS` | The web addresses allowed to call the service from a browser, separated by commas. The default allows `https://keepvigil.earth` and the viewer's local address. |
| `VIGIL_SITE` | The folder holding the built viewer (`index.html` and `viewer/`), served at `/`. Left out, the repository's root when the built copy is there. |
| `VIGIL_ASK_PER_MINUTE`, `VIGIL_ASK_TOTAL_PER_MINUTE` | How many questions one other device, and all other devices together, may put to the Detective in a minute. 4 and 20 unless set. |

## The calls

| Call | Returns |
|---|---|
| `GET /health` | Whether it is up, which run it holds, and whether it has the gridded data and the Detective's key |
| `GET /snapshot` | The whole stored document |
| `GET /run` | `run`, `variables` |
| `GET /findings` | `findings`, filtered by `kind`, `variable`, `significant`, `limit` (the top N by rank) and the box `lat_min`, `lat_max`, `lon_min`, `lon_max` (a finding is in the box when its cell overlaps it) |
| `GET /findings/{id}` | That finding and the series it needs |
| `GET /series?cell_id=&variable=` | One series: stored, or built from the gridded data |
| `GET /grids?variable=` | The globe layers, one or all |
| `POST /analyse` | `run`, `variables`, `findings`, `series` for a region and period. Body: `lat_min`, `lat_max`, `lon_min`, `lon_max`, and optionally `start` and `end` as `YYYY-MM-01`. |
| `POST /zone` | One series and one trend per variable over a drawn zone. Body: `polygon`, a list of `[latitude, longitude]` points. |
| `POST /ask` | The Detective's answer, with the `steps` it took to reach it. Body: `question`, optional `history`, optional `finding_id`. |
| `GET /years?variable=` | Each scanned cell's mean anomaly in each calendar year, for watching a change arrive. Not a result: nothing is tested on it. |
| `GET /present` | What a presenter has on show: `presenting`, `run_id`, `sequence`, `show` |
| `POST /present` | Sets it, from the machine the service runs on only. Body: `presenting`, and optionally `finding_id`, `variable`, `zone`. |
| `GET /` | The built viewer, when the service can find it |
| `GET /snapshot.json`, `GET /series/{variable}/{cell_id}.json`, `GET /years/{variable}.json`, `GET /ask.json` | The files the viewer asks for beside its page, answered from what the service holds |

A failure is a non-2xx status with the body `{"detail": "..."}`.

## From data to a stored scan

The pipeline hands over one `.npz` file. Its layout is in the `ScanData` docstring in `document.py`: one array per variable shaped `(rows, columns, months)`, a `meta` entry with the grid and the first month, and a mask.

```
python -m vigil.api.document scan-data.npz snapshot.json
python ../contract/tools/check.py snapshot.json
```

The first command runs the full scan (a few minutes) and writes the document. The second must print `OK` before anyone uses the file. Then serve it with `VIGIL_SNAPSHOT`, and put a copy beside the web viewer's `index.html` to show it there.

## Region analysis

`POST /analyse` runs the same analysis on a block of cells. Cell ids stay the full grid's, and the run record says which block was covered in `settings.region`. Findings that did not pass the threshold are returned too, marked `significant: false`. A request is limited to 400 cells so that it finishes while the caller waits, and a region that crosses the date line is refused.

## Re-testing on a second product

```
python -m vigil.api.retest snapshot.json ../data/scan/retest snapshot-retested.json
python ../contract/tools/check.py snapshot-retested.json
```

The brief asks that discovery and validation be kept apart. This is the second half. It takes a stored scan and, for every significant trend finding, runs the scan's own trend test on a second product for the same variable in the same cell. The second products are downloaded by the pipeline as `retest.<variable>` (see `python/vigil/pipeline/README.md`): GISTEMP for temperature, GPCP for precipitation, MERRA-2's land model for soil moisture and Aqua MODIS for vegetation.

It changes nothing the scan computed. Each trend finding's `standing` gains a `retest` block and a sentence in its limitations, and its status becomes `retested` only when the second product confirmed it. The run record gains `run.retest`, with the counts per variable. The rule is at the top of `retest.py` and was written before any result was seen: only the direction and the significance of the second record's trend are compared, never its size.

Three things to keep in view when quoting the results:

- None of the second products is fully independent of the first. `SECOND_PRODUCTS` in `retest.py` says what each pair has in common, and that sentence travels with every re-tested finding.
- A confirmation says little where the second product is significant nearly everywhere. `run.retest` gives that rate for each variable, counted over the cells where the scan found nothing. Never quote a confirmation share without it.
- One test is made per finding, with no correction for many tests.

Serve the re-tested file as the snapshot. The service treats it like any other stored scan.

## What the Detective looked up

`POST /ask` returns `steps`: each tool call the Detective made, with a line of plain words, the findings it returned and, for a search that named a place, the box it searched. The apps list them under the answer and draw them on the globe.

The steps are recorded without touching the Detective. `ask()` takes the client it calls the model through, and `trace.TracingClient` wraps the real one: every request and reply passes through unchanged, and the wrapper reads the tool calls as they go by. A failure to read a step drops the step, never the answer. The tests run it against the Detective's real tool loop and grounding check with a scripted stand-in for the model. It has not been run against the live model from this folder: `python -m vigil.detective.live_check` is still the only live check.

## Zones

`POST /zone` answers for an area drawn by hand on the globe. The format and the rules for the outline are in `contract/README.md` under "A zone", and the module docstring of `zone.py` has the detail.

It still computes no statistics. It finds the cells whose centres the outline holds, gathers them into one series per variable (an area-weighted mean of the cells the stored scan tested, or a sum for earthquakes), and hands those series to `scan_trends` as a grid of one cell. Each variable is then tested as a cell is. The tests check this directly: a zone of one scanned cell must give that cell's own slope, interval and p-value, for a measured variable and for earthquakes.

One result gets a warning from the service itself. Where a zone's mean snow cover over the period is under 1%, the snow result carries `caution: "Snow is rare here"`, with the reason as its first limitation. A trend in almost nothing came out with a small p-value over the Amazon, which is how the rule came to be written (`RARE_SNOW_PERCENT` in `zone.py`). The result is still returned in full.

The answer carries the stored scan's `run_id`. Serve the snapshot together with the `scan-data.npz` it was made from, or the apps will refuse to show a zone's figures beside the wrong scan.

To try it on the desktop app, start the service with both settings and then start the app, which loads the service's scan when it opens:

```
$env:VIGIL_SNAPSHOT = "..\data\scan\snapshot.json"
$env:VIGIL_SCAN_DATA = "..\data\scan\scan-data.npz"
python -m uvicorn vigil.api.app:app --port 8000
```

## Starting it with one command

`python\start-service.cmd` starts the service on the scan in `data\scan`, with the gridded data beside it, and starts it again if it stops. Double-click it, or run it from a terminal:

```
python\start-service.cmd             this machine only
python\start-service.cmd -Network    every network address, which presenting needs
python\start-service.cmd -Port 8001  another port
```

It leaves alone anything already set in the terminal (`VIGIL_SNAPSHOT`, `VIGIL_SCAN_DATA`, `ANTHROPIC_API_KEY`). For the Detective's key it looks in the terminal, then at the value saved for your user (`setx ANTHROPIC_API_KEY "the key"`), then in `python\.env`, which git ignores. It says whether it found one and never shows it. It will not start a second service on a port where one already answers, and it gives up when the service stops three times as soon as it starts.

The desktop app asks `GET /health` every five seconds and shows what it heard in a badge beside its Present button, so a service that has stopped is seen before it is needed.

## The service as a website

The service hands out the built viewer at `/`, from the repository's root (`index.html` and `viewer/`) or from the folder `VIGIL_SITE` names. The page asks for `snapshot.json` beside itself and gets the scan the service holds, so the page, the Detective, a zone and a recompute are always about the same run. It gets `series/<variable>/<cell_id>.json` the same way, built from the gridded data when the stored scan has no series for that cell.

To let a phone or another laptop in, start it on every address and not only this machine's own:

```
python -m uvicorn vigil.api.app:app --host 0.0.0.0 --port 8000
```

Then anyone on the same network opens `http://<this machine's address>:8000`. Windows asks once whether to let Python through the firewall. A venue's Wi-Fi may stop devices seeing each other, in which case a phone's hotspot works.

Two things follow from opening it to a room:

- `POST /ask` spends the key. Another device may ask 4 questions a minute (`VIGIL_ASK_PER_MINUTE`), all other devices together 20 (`VIGIL_ASK_TOTAL_PER_MINUTE`), and a question over either limit gets a 429 with the reason. The machine the service runs on is never limited.
- `POST /present` moves everyone's screen, so the service takes it only from the machine it runs on.

## Presenting

`GET /present` says what the presenter has on show: a finding, a layer and a zone, each optional. `POST /present` sets it. The desktop app's Present button does that, and a viewer opened from the service follows. The format is in `contract/README.md` under "Presenting". The state is kept in memory and is gone when the service stops. It changes no result.

## A website with no service

The public site is a bucket of files. To give it every finding's chart, write the files out and put them beside `index.html`:

```
python -m vigil.api.export ..\data\scan\snapshot.json ..\data\scan\scan-data.npz ..\data\site
```

That writes `snapshot.json`, `snapshot.json.gz`, a `years/<variable>.json` for each variable and one `series/<variable>/<cell_id>.json` for every cell and variable that a finding's evidence draws: 5,836 files and 85 MB for the scan of 15 November. Each series is made by the code behind `GET /series`, so the site and the service cannot disagree.

Serve the snapshot compressed. It is 20 MB as text and 2 MB compressed, which is the difference between a phone opening the site and giving up. A bucket does not compress for you, and CloudFront only compresses files under 10 MB, so upload the compressed copy under the plain name and say what it is:

```
aws s3 cp ..\data\site\snapshot.json.gz s3://<bucket>/snapshot.json --content-encoding gzip --content-type application/json --cache-control no-cache
aws s3 sync ..\data\site\series s3://<bucket>/series --content-type application/json
aws s3 sync ..\data\site\years s3://<bucket>/years --content-type application/json
```

The deploy workflow deletes nothing outside `viewer/`, so both survive it. Upload them again after every new scan, together: a series file names its run, and the viewer will not draw one beside a different run.

## On AWS Lambda

A bucket cannot run code, so the public site's service is a Lambda of its own: `vigil-api`, in `eu-north-1`, at

```
https://nhzags36diemgymrus3raa5sdi0vkccz.lambda-url.eu-north-1.on.aws
```

The deploy workflow builds the viewer with that address as `VITE_VIGIL_API_URL`, so keepvigil.earth asks it. When a copy of the function starts it reads the scan from the site's bucket (`VIGIL_SNAPSHOT=s3://keepvigil.earth/snapshot.json`) and the gridded data from the private one (`VIGIL_SCAN_DATA=s3://vigil-deploy-630809268406/scan-data.npz`), once, and keeps them for every request after. So a new scan needs no redeploy: upload both files, then save any change to the function's settings, which starts fresh copies. A copy that is already running keeps what it started with, and a snapshot whose grid or period differs from the data stops the function starting. The desktop app uses it when `VIGIL_API_URL` is set to the address.

It answers every call but one: nobody can present from it, because `POST /present` is taken from the service's own machine only. The site's charts and years still come from the files on the bucket first, and from here when a file is missing.

A fresh copy takes about 8 seconds to start, most of it unpacking the gridded data, and Lambda allows 10 before it starts again more slowly. A copy stays for some minutes after its last request, and an open page asks every two seconds whether someone is presenting, so in practice one is waiting. The account runs at most 10 copies at once, shared with `nasa-data-fetcher`, and a question to the Detective holds one for up to 75 seconds.

The Detective's key is the function's `ANTHROPIC_API_KEY` setting, and nowhere else. Set it in the AWS console: Lambda, `vigil-api`, Configuration, Environment variables. Anyone can reach the address, and the limit on questions (`VIGIL_ASK_PER_MINUTE`) is kept by each running copy of the function separately, so put a spend limit on the key itself.

To put new code on it, from the `python/` folder. The first command fetches the Linux builds of the packages the service loads, which are not the Windows ones in `.venv`:

```
python -m pip install --target ..\data\lambda\packages --only-binary=:all: --implementation cp --python-version 3.12 --platform manylinux_2_28_x86_64 --platform manylinux_2_17_x86_64 --platform manylinux2014_x86_64 fastapi==0.163.0 mangum==0.22.0 anthropic==2.8.0 httpx==0.28.1 numpy==2.5.4 scipy==1.18.1 statsmodels==0.15.0 ruptures==1.1.9
python -m pip install --target ..\data\lambda\packages --no-deps pymannkendall==1.4.3
python -m vigil.api.lambda_pack ..\data\lambda\packages ..\data\lambda\vigil-api.zip
aws s3 cp ..\data\lambda\vigil-api.zip s3://vigil-deploy-630809268406/vigil-api.zip
aws lambda update-function-code --function-name vigil-api --s3-bucket vigil-deploy-630809268406 --s3-key vigil-api.zip
```

Keep the versions in step with `requirements.txt`. The zip is 71 MB, over the 50 MB Lambda takes directly, so it goes through `vigil-deploy-630809268406`, a private bucket that holds only this zip and the gridded data. `lambda_pack` leaves the packages' own test suites out: with them the code is over the 250 MB Lambda allows unpacked. The function runs Python 3.12 with 2048 MB and a 90 second limit, which is above the Detective's 75 second deadline.

Then check it:

```
curl.exe -s https://nhzags36diemgymrus3raa5sdi0vkccz.lambda-url.eu-north-1.on.aws/health
```

`run_id` should be the scan on the site, `scan_data` is `true` when the gridded data loaded, and `detective` is `true` once the key is set.

## Tests

```
python -m pytest vigil/api -q
```
