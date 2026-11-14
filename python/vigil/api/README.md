# api

The FastAPI service, and the step before it that turns gridded data into a results document. It serves stored findings to the desktop app, the web viewer and the AI Detective, and runs a fresh analysis on a region when asked. It hands out results and never changes them. Dan owns this folder (see `COORDINATION.md`).

Everything it sends follows the format in `contract/`.

## What is here

| File | What it is |
|---|---|
| `document.py` | From gridded data to a results document: the four analysis calls in order, the descriptive text, the series and the run record. Also the layout of the data file the pipeline hands over (`ScanData`). |
| `app.py` | The service and its calls |
| `lambda_handler.py` | Runs the service on AWS Lambda |
| `tests/` | The calls against the fixture, and the whole path from made-up data to a checked document |

It computes no statistics. Those come from `vigil/analysis/`, and the wording follows Will's note on using it.

## Running it

From the `python/` folder, with the packages in `requirements.txt` installed:

```
python -m uvicorn vigil.api.app:app --port 8000
```

With nothing set, it serves the contract's fixture, which is made up and says so. Both apps look for the service at `http://127.0.0.1:8000` on the same machine.

| Setting | What it does |
|---|---|
| `VIGIL_SNAPSHOT` | The stored scan to serve: a file path, an `s3://bucket/key` address or an `https://` one |
| `VIGIL_SCAN_DATA` | The gridded data the scan was made from. With it, `GET /series` works for any cell and `POST /analyse` runs. |
| `ANTHROPIC_API_KEY` | The Detective's key. Without it `POST /ask` answers 503 and the apps show their marked sample answers. Never in the repository. |
| `VIGIL_CORS_ORIGINS` | The web addresses allowed to call the service from a browser, separated by commas. The default allows `https://keepvigil.earth` and the viewer's local address. |

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
| `POST /ask` | The Detective's answer. Body: `question`, optional `history`, optional `finding_id`. |

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

## Tests

```
python -m pytest vigil/api -q
```
