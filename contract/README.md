# contract

The result contract: the JSON that the Python service sends to the desktop app and to the AI Detective. It is the only place where the C# and Python halves meet.

Dan owns this folder. Everyone else reads it and never edits it. If the format is missing something you need, ask Dan. See `COORDINATION.md`.

This is version 0.1. Will has not yet checked the statistics fields, so expect small changes on Saturday morning. The questions still open are at the end.

## What is here

| File | What it is |
|---|---|
| `README.md` | This file: every field and what it means |
| `schema.json` | The same thing as a JSON Schema (draft 2020-12), for checking a document by machine |
| `fixture/snapshot.json` | A complete set of fake results: a run record, 14 findings, their series and four globe layers |
| `fixture/ask.json` | Four example questions to the AI Detective, each with the request and the answer |
| `tools/make_fixture.py` | Regenerates the fixture from a fixed seed. Standard library only. |
| `tools/check.py` | Checks any results document against the contract, real or fake |
| `tools/land_mask.txt` | A rough land, ice and ocean map on the scan grid, used by the generator |

Everything in `fixture/` is made up. The series are random numbers with a trend, a step or a link added on purpose, and the place names in the generator mean nothing. The statistics are worked out from those series, so the files agree with themselves: the slope in a finding is the slope of its series. Each document carries `"synthetic": true` and each finding lists "Synthetic fixture value" under its limitations.

## Rules for every document

- Field names are `snake_case`. In C#, `JsonNamingPolicy.SnakeCaseLower` maps them to PascalCase properties with no extra code. It is built into .NET 8.
- A missing value is `null`. Never write `NaN` or `Infinity`: Python's `json.dumps` writes them by default and `System.Text.Json` refuses to read them. Call `json.dumps(..., allow_nan=False)` so a stray NaN fails on the Python side, where it can be fixed.
- A date is `YYYY-MM-DD`. A month is written as its first day, so December 2025 is `2025-12-01`. A period's `end` is the first day of its last month.
- A timestamp is UTC, `YYYY-MM-DDTHH:MM:SSZ`.
- Latitude runs from -90 to 90 and longitude from -180 to 180. Longitudes never run 0 to 360.
- Every document carries `contract_version`. Readers ignore fields they do not know, so a field can be added without breaking anyone. Renaming, removing or retyping a field is a change of contract and goes through Dan.
- Method names, parameters and limitations are free text. The app shows them and never branches on them. The values in `method.parameters` are a mix of strings, numbers and booleans, so in C# the block binds to `Dictionary<string, JsonElement>`.

## The grid and cell ids

The scan grid is described once, in `run.settings.grid`. Rows count from the south and columns from the west, both from zero:

```
lat_min of a cell = grid.lat_min + row * cell_degrees
lon_min of a cell = grid.lon_min + column * cell_degrees
```

A cell's id is `r` and the row in two digits, then `c` and the column in three: `r60c112`. Every cell object also carries its own bounds, so nothing has to work them out.

The cell size is still an open item in the brief. Nothing in the format assumes 2.5 degrees.

## A results document

Every call that returns results sends the same outer object, holding only the parts that call is for. The snapshot file shipped with the app is one of these with every part present.

| Field | Type | Meaning |
|---|---|---|
| `contract_version` | string | `"0.1"` |
| `synthetic` | boolean | True when the numbers are made up. Real results set it to false. |
| `run` | object | The run record |
| `variables` | array | The variables in the scan, with their sources |
| `findings` | array | Findings, in rank order |
| `series` | array | Monthly series for charts |
| `grids` | array | One globe layer per variable |

| Call | Parts it returns |
|---|---|
| `GET /snapshot` | Everything. This is the file the app keeps for running without the service. |
| `GET /run` | `run`, `variables` |
| `GET /findings` | `findings`. Filters: `kind`, `variable`, `significant`, `limit` for the top N by rank, and `lat_min`, `lat_max`, `lon_min`, `lon_max` for a region. |
| `GET /findings/{id}` | `findings` with that one finding, and `series` with every series it needs |
| `GET /series?cell_id=&variable=` | `series` with that one series |
| `GET /grids?variable=` | `grids`. Without `variable`, all of them. |
| `POST /analyse` | `run`, `variables`, `findings`, `series` for the region and period asked for |
| `POST /ask` | The Detective's answer, which has its own shape. See below. |

`/snapshot`, `/run` and `/grids` are not in the brief's draft API. The app needs the run counts for the first step of the demo and the layers for the globe, and neither had a call.

When a call fails, the service returns a non-2xx status with FastAPI's usual body, `{"detail": "..."}`.

## A finding

One type covers all four kinds. The fields in the first table are always present. The second table lists the blocks that depend on `kind`.

| Field | Type | Meaning |
|---|---|---|
| `id` | string | Stable across reruns on the same grid: kind, variable and cell, such as `trend.temperature.r60c112`. Lower case, digits, dots, hyphens and underscores only, so it is safe in a URL. |
| `kind` | string | `trend`, `change_point`, `cascade` or `contradiction` |
| `rank` | integer | 1 is the top of the list. Follows `score.total`, with two exceptions set out under "rank" below. |
| `summary` | string | One line for the findings list, written by the service |
| `significant` | boolean | Whether it passes `run.settings.threshold` after correction for multiple testing |
| `cell` | object | `id`, `row`, `column`, `lat_min`, `lat_max`, `lon_min`, `lon_max` |
| `period` | object | `start`, `end`, and `step`, which is always `"monthly"` for now |
| `variables` | array | One source per variable. A cascade has two: the leading variable, then the following one. |
| `preprocessing` | object | What was done to the data before the test |
| `method` | object | `test`, a name, and `parameters`, a flat object of strings, numbers and booleans |
| `links` | array | Related findings. Empty when there are none. |
| `score` | object | The ranking score and its parts |
| `standing` | object | `status`, which is `exploratory` or `retested`, and `limitations`, a list of sentences |

| Kind | Block it must carry |
|---|---|
| `trend` | `result` |
| `change_point` | `change_point` |
| `cascade` | `cascade` |
| `contradiction` | `contradiction` |

The stored scan lists findings that pass the threshold. A finding with `significant: false` still has to be handled: `POST /analyse` on a region where nothing passes returns them, and so does `GET /findings?significant=false`. The fixture has two.

### variables

| Field | Meaning |
|---|---|
| `id` | `temperature`, `precipitation`, `soil_moisture` or `vegetation`. Used wherever a variable is named. |
| `name` | For display: "Air temperature at 2 m" |
| `units` | Units of the values in the series: `K`, `mm per month`, `kg per m2`, `NDVI` |
| `mission` | "MERRA-2", "GPM IMERG", "GLDAS Noah", "Terra MODIS" |
| `product` | The product's short name, such as `M2TMNXSLV` |
| `field` | The field read from the product. Optional. |
| `source_url` | Where the product can be found |
| `native_resolution` | The product's own resolution, as text. Optional. |

### preprocessing

| Field | Meaning |
|---|---|
| `regridding` | How the product was put on the scan grid |
| `aggregation` | How it was brought to monthly steps |
| `anomaly` | How the seasonal cycle was removed |
| `missing_data_rule` | When a cell is scanned and what happens to missing months |
| `months_present` | Months with a value. For a cascade, months where both variables have one. |
| `months_expected` | Months in the period |
| `coverage` | `months_present / months_expected` |

### result (trend)

| Field | Meaning |
|---|---|
| `direction` | `increasing` or `decreasing` when significant, otherwise `none` |
| `slope_per_decade` | Sen's slope of the anomaly series, per decade |
| `units` | Units of the slope, such as `K per decade` |
| `ci_low`, `ci_high` | Confidence interval on the slope, in the same units |
| `ci_level` | The level of that interval, such as 0.95 |
| `p_value` | Before correction for multiple testing |
| `p_adjusted` | After it. This is the one compared with the threshold. |

### change_point

| Field | Meaning |
|---|---|
| `date` | The month the series changes level |
| `date_low`, `date_high` | The uncertainty range on that date |
| `level_before`, `level_after` | Mean of the anomaly series before and after |
| `shift` | `level_after - level_before` |
| `units` | Units of the levels |

### cascade

A cascade is an association in time between two variables in one cell. It never says one caused the other, and `summary` should not either.

| Field | Meaning |
|---|---|
| `leading_variable`, `following_variable` | Variable ids. The leading one moves first. |
| `lag_months` | How many months the following variable trails by, at the strongest correlation |
| `correlation` | The correlation at that lag, from -1 to 1 |
| `correlations_by_lag` | The correlation at every lag tested, starting at lag 0. Optional. |
| `p_value`, `p_adjusted` | As for a trend |

### contradiction

A contradiction is two nearby cells with significant trends of opposite sign in the same variable. The finding's own `cell` is one of them.

| Field | Meaning |
|---|---|
| `variable` | Variable id |
| `paired_cell` | The other cell, as a full cell object |
| `slope_per_decade` | The trend in this finding's cell |
| `paired_slope_per_decade` | The trend in the paired cell. Opposite in sign. |
| `units` | Units of both slopes |
| `distance_km` | Distance between the cell centres. Optional. |
| `confounders` | Plain sentences on what else could explain the difference. May be empty. |

### links

Each link names another finding. Links are written on both ends: if A links to B, B links to A.

| Field | Meaning |
|---|---|
| `finding_id` | The other finding |
| `kind` | That finding's kind, so a list can label the link without looking it up |
| `lag_months`, `correlation` | Present when the link is to or from a cascade. On a pair of change points, `lag_months` is the gap between their dates. |
| `cell_id` | Present when the link is to or from a contradiction: the cell on the other side |

### score

Every part runs from 0 to 1 and higher is better. `total` is the weighted sum of the parts, using `run.settings.score_weights`, so anyone can recompute it.

| Field | Meaning |
|---|---|
| `total` | The ranking score |
| `effect_size` | How large the change is |
| `certainty` | How tight the estimate is. The brief calls this part "uncertainty". It is stored the other way up so that every part points the same way. |
| `coverage` | How complete the data is |
| `robustness` | How well it survives a sensitivity check |
| `cross_system` | How many other variables or findings it connects to |

The weights in the fixture are the real ones, from `WEIGHTS` in `python/vigil/analysis/score.py`. A real run writes that same dictionary into `run.settings.score_weights`, so the numbers live in one place. They are fixed before the first real scan and not tuned afterwards.

The parts in the fixture come from stand-in formulas. The real ones are Will's, in `score.py`.

### rank

The list is ordered in four steps:

1. Significant findings come before non-significant ones.
2. Within each, higher `score.total` comes first.
3. Ties go to the finding whose `id` sorts first.
4. If `run.settings.rank_cap` is present, the cap is applied to the significant findings.

The cap stops one variable filling the top of the list. Temperature warmed almost everywhere, so without it the first screen would be all temperature. Each finding belongs to a group: its first variable, or `cascade` if it is a cascade. Walking down the list, a finding takes the next of the first `top_n` places unless its group already holds `max_share` of them (rounded up, so 5 of 20). A finding that is passed over keeps its order and follows straight after those places.

Three things follow:

- `score.total` is never changed by the cap. It is still the plain weighted sum.
- Findings in the same group are never reordered, so within a group `rank` always follows `score.total`.
- The cap is a rule for filling places, not a promise about the result. With few groups, or fewer than `top_n` significant findings, a group can still end up with more than its share.

When `rank_cap` is absent there is no cap, and `rank` follows steps 1 to 3 alone.

The app sorts by `rank` and never re-sorts by `score.total`. The service applies the cap once, so the screen, `GET /findings?limit=N` and the Detective all see the same order.

## A series

One cell and one variable, as parallel arrays of the same length. `dates[i]`, `raw[i]` and `anomaly[i]` belong together.

| Field | Meaning |
|---|---|
| `cell_id` | The cell |
| `variable` | Variable id |
| `units` | Units of both value arrays |
| `dates` | One date per month, in order |
| `raw` | The value on the scan grid, or `null` for a missing month |
| `anomaly` | The value minus that calendar month's mean over the period, or `null` |

A finding's evidence view needs the series for its cell and each of its variables, and for the paired cell of a contradiction. `GET /findings/{id}` returns them with the finding, and `GET /series` returns any one on request.

The fixture snapshot holds the series for all 14 of its findings. A real snapshot cannot hold one for every finding: at 7 to 8 KB a series, a scan with thousands of significant tests would run to tens of megabytes. How many it carries is an open question below. The app must cope with a finding whose series is not in the file.

## A globe layer

One per variable. The three arrays are indexed `[row][column]`, with row 0 the southernmost and column 0 the westernmost. That is the layout `GlobeControl.SetGridOverlay` takes, once `null` is turned into NaN.

| Field | Meaning |
|---|---|
| `variable` | Variable id |
| `units` | Units of the slopes |
| `rows`, `columns` | Size of the arrays |
| `lat_min`, `lat_max`, `lon_min`, `lon_max` | The area the arrays cover |
| `slope_per_decade` | The trend in each cell, or `null` where the cell was skipped |
| `p_adjusted` | The adjusted p-value in each cell, or `null` |
| `skip_reason` | `null` where the cell was scanned. Otherwise `ocean`, `ice`, or `coverage` for too few months. |

A cell is either scanned, with a slope and a p-value, or skipped, with a reason. It is never both and never neither. This is the record of every test in the scan, significant or not.

## The run record

| Field | Meaning |
|---|---|
| `id` | Names the run |
| `started`, `finished` | Timestamps |
| `duration_seconds` | How long it took |
| `code_version` | The git commit the run was made from |
| `settings` | Everything fixed before the scan: `grid`, `region` (optional), `period`, `variables`, `min_coverage`, `threshold`, `multiple_testing`, `ci_level`, `max_lag_months`, `score_weights`, `rank_cap` (optional) |
| `counts` | What the scan did. See below. |

A test is one variable in one cell.

| Count | Meaning |
|---|---|
| `cells_total` | Cells on the grid |
| `cells_scanned` | Cells where at least one variable was tested |
| `tests_total` | Cells times variables |
| `tests_run` | Tests carried out |
| `tests_significant` | Tests that passed the threshold after correction |
| `tests_not_significant` | Tests that ran and did not pass |
| `tests_skipped` | Tests not carried out |
| `skipped_by_reason` | The skipped tests, split by reason |
| `by_variable` | The same four test counts for each variable |
| `findings_total`, `findings_by_kind` | Findings in the stored list |

`tests_run + tests_skipped = tests_total`, and `tests_significant + tests_not_significant = tests_run`.

### A run over part of the grid

`POST /analyse` scans a region, not the whole grid. Its run record says so with `settings.region`:

| Field | Meaning |
|---|---|
| `row`, `column` | The south-west cell of the block, counted on the full grid in `settings.grid` |
| `rows`, `columns` | How many cells the block is tall and wide |

When `region` is present, every count relates to the region: `cells_total` is `region.rows * region.columns`, and the test counts follow from that. `settings.grid` still describes the full grid, and cell ids stay full-grid ids, so `r60c112` names the same cell in a region run as in the whole scan. Every finding's cell lies inside the region. A region does not wrap across the date line: `column + columns` is at most `grid.columns`.

When `region` is absent the run covered the whole grid, and `cells_total` is `grid.rows * grid.columns`.

`region` was added after version 0.1 was first written. It is optional and readers ignore fields they do not know, so the version number has not changed.

In the fixture the findings list is short on purpose: 14 findings, while the layers hold 1,300 or so significant tests. In a real scan every significant test is a trend finding.

## Asking the AI Detective

`POST /ask` takes:

| Field | Meaning |
|---|---|
| `question` | What the user typed |
| `history` | Earlier turns, oldest first, each with a `role` of `user` or `assistant` and its `text`. Optional. |
| `finding_id` | The finding open in the app when the question was asked. Optional. |

and returns:

| Field | Meaning |
|---|---|
| `contract_version` | As everywhere else |
| `run_id` | The run the answer was drawn from |
| `status` | `answered`, `inconclusive` when the results cannot settle the question, or `out_of_scope` when it is not about the scan |
| `answer` | Plain text for the chat panel |
| `citations` | The findings the answer rests on. Each has a `finding_id` and, optionally, `supports`: what that finding backs up. |
| `limitations` | Caveats, as sentences. May be empty. |
| `model` | The language model that wrote the answer. Optional. |

An `answered` response cites at least one finding. Every number in `answer` comes from a cited finding.

## Checking a document

```
python contract/tools/check.py                         the fixture
python contract/tools/check.py path/to/results.json    anything else
```

It reads the file strictly, then checks that the document agrees with itself: links resolve in both directions, each interval contains its slope, adjusted p is never below raw p, `significant` matches the threshold, the layers match the findings and the counts, and each anomaly series can be rebuilt from its raw values. For the fixture it also checks that every finding has its series and that every kind, direction, standing and skip reason appears. With `pip install jsonschema` it also validates against `schema.json`.

Run it on real output before handing a snapshot to the app.

To rebuild the fixture after a change to the contract:

```
python contract/tools/make_fixture.py
```

The same seed gives the same files, byte for byte.

## Open questions

For Will:

- Which confidence level the slope interval uses. The fixture assumes 0.95.
- Whether per-decade slopes suit every variable, and whether the units strings are the ones the analysis will write.
- How a change point gets its date range. `ruptures` gives a date and no interval, so the range needs another method, such as the spread between the two penalty settings or a bootstrap.
- What makes a change point `significant`.

For Dan:

- How many series the snapshot carries, so the offline demo can open its findings without the file growing too large. One option is the top 50 findings by rank, with the rest fetched from `/series` when the service is up.

For Ethan:

- Whether the three extra calls (`/snapshot`, `/run`, `/grids`) are fine to serve.
- Whether `code_version` can be the git commit of the run.
