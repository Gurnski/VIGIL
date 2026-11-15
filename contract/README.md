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
| `POST /zone` | One series and one trend per variable over an area drawn by hand. It has its own shape. See "A zone" below. |
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
| `standing` | object | `status`, which is `exploratory` or `retested`, `limitations`, a list of sentences, and optionally `retest`. See "Re-testing" below. |

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

### standing and re-testing

A finding starts as `exploratory`: the scan found it and nothing else has looked. The brief asks for discovery and validation to be kept apart, so a second step can test a trend again on a second product for the same variable (Aqua's NDVI for Terra's, GISTEMP for MERRA-2 and so on). That step changes nothing the scan computed. It writes what it found into `standing`:

| Field | Meaning |
|---|---|
| `status` | `retested` only when the second product confirmed the trend. Otherwise `exploratory`, whatever else the re-test found. |
| `limitations` | Gains a sentence saying what the re-test found and one saying what the two products have in common. The sentences hold no numbers. |
| `retest` | Optional. Present when a re-test was tried. |

`retest` holds:

| Field | Meaning |
|---|---|
| `outcome` | `confirmed`, `same_direction`, `opposite` or `not_possible` |
| `product` | The second product: `mission`, `product`, `field`, `units`, `source_url`, `native_resolution` |
| `direction` | `increasing` or `decreasing`: the sign of the second record's Sen slope |
| `slope_per_decade`, `ci_low`, `ci_high`, `ci_level`, `units` | The second record's trend, in its own units, which may not be the finding's |
| `p_value` | The second record's own p-value, from the scan's trend test |
| `months_present`, `months_expected` | How much of the period the second record covers in this cell |

Everything after `product` is absent when the outcome is `not_possible`.

The rule is fixed in `python/vigil/api/retest.py` and was written before any result was seen. Only direction and significance are compared, never size, because the second product may be in other units:

- `confirmed`: the second record's slope has the same sign and its own p-value is below `run.settings.threshold`.
- `same_direction`: the same sign, but not significant on its own.
- `opposite`: the other sign, significant or not.
- `not_possible`: the second record has too little data in the cell.

One test is made per finding and it is not corrected for many tests. A confirmation is evidence, not proof, and how much it is worth depends on how different the second product is. None is fully independent of the first. So an app that shows `retested` must also show which product it was re-tested on, and the limitations.

Only significant trend findings are re-tested. Change points, cascades and contradictions stay `exploratory`.

The run record gains `run.retest` when the step has run: `rule`, the rule in words, and `by_variable`, one entry per variable re-tested:

| Field | Meaning |
|---|---|
| `variable`, `product`, `in_common` | The variable, its second product, and what the two products share |
| `findings` | How many trend findings were re-tested |
| `confirmed`, `same_direction`, `opposite`, `not_possible` | How they came out. The four add up to `findings`. |
| `other_cells`, `other_cells_tested`, `other_cells_significant` | The cells the scan tested and found nothing in: how many, how many the second product could be tested in, and in how many of those it has a significant trend of its own |

The last line is the rate to hold the confirmations against. Where a variable is changing almost everywhere, as temperature is, the second product is significant in most cells whatever the scan found, and a confirmation says less.

`standing.retest` and `run.retest` were added after version 0.1 was first written. Both are optional and readers ignore fields they do not know, so the version number has not changed.

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

### Which variables a scan has

A variable id is a name in lower case with underscores, and readers must not assume a fixed set. The fixture has four: `temperature`, `precipitation`, `soil_moisture` and `vegetation`. A real scan lists its own in `run.settings.variables` and describes each in `variables`. So far the service knows eight, in `VARIABLES` in `python/vigil/api/document.py`: those four, `earthquakes`, `snow_cover`, `sea_surface_temperature` and `chlorophyll`.

The last two are measured over the sea. A land cell holds no data for them, so it is skipped with the reason `coverage`, the same word as for any cell with too little data. There is no skip reason for "land", and none has been added.

## A zone

A zone is an area a user draws on the globe. `POST /zone` takes its outline and returns, for each variable, the one series that stands for the whole zone and the result of the scan's trend test on that series. It needs the gridded data, so a service without it answers 503.

A zone result is not a finding. It has no cell, no id, no rank and no score, it is not in the stored scan, and the Detective never sees it.

`POST /zone` takes:

| Field | Meaning |
|---|---|
| `polygon` | The outline: at least three `[latitude, longitude]` points. It is closed from the last point back to the first. |

The rules for the outline, which the service, the desktop app and the web viewer all follow. Each has its own copy of the rule (`python/vigil/api/zone.py`, `VIGIL/VIGIL/Data/ZoneGeometry.cs`, `web/src/zones/geometry.ts`), and each front end refuses a zone's figures when the service's cells are not its own:

- Its edges are straight lines in latitude and longitude.
- Longitudes are continuous along it. An outline that crosses the date line runs past 180 (or below -180) and does not jump.
- A grid cell belongs to the zone when the cell's centre is inside the outline, by the even-odd rule.
- An outline that goes all the way round a pole has no inside and is refused with 422. So is one that holds no cell centre.

It returns:

| Field | Meaning |
|---|---|
| `contract_version`, `synthetic` | As everywhere else |
| `run_id` | The stored scan the service holds. The figures belong beside that scan and no other, so an app showing a different run must not show them. |
| `zone` | What the zone was taken to be: `cells` (the ids of the cells in it), `cell_count`, `area_km2` (the area of those cells), `lat_min` and `lat_max` |
| `period` | `start`, `end` and `step`, as in a finding |
| `variables` | The variables in the scan, with their sources |
| `summaries` | One entry per variable, tested or not |

Each entry of `summaries`:

| Field | Meaning |
|---|---|
| `variable`, `units` | The variable's id and the units of its series |
| `cells_used` | How many of the zone's cells went into the series |
| `cells_skipped` | The zone's cells left out, counted by the scan's skip reason. Empty for a count variable. |
| `tested` | Whether the trend test ran |
| `skip_reason` | `null` when it ran. Otherwise `no_cells`, when none of the zone's cells were scanned for this variable, or `coverage`, when the zone's series has too few months or too few events to test. |
| `significant` | Whether the p-value is below the scan's threshold |
| `result` | The same block as a trend finding's `result`. `null` when the test did not run, or ran on a flat series and has nothing to report. |
| `preprocessing`, `method` | The same blocks as a finding's, or `null` with `result` |
| `summary` | One line of text |
| `caution` | A few words to show beside the number when the result should not be taken at face value, or `null`. So far there is one: "Snow is rare here", for snow cover over a zone whose mean cover is under 1%. The reason in full is the first of `limitations`. |
| `limitations` | Caveats, as sentences |
| `series` | `units`, `dates`, `raw` and `anomaly`, as in "A series" but for the zone and with no `cell_id`. `null` when no cell was used. |

How a zone's cells become one series:

- For a measured variable, the cells used are the zone's cells that the stored scan tested, taken from that variable's globe layer. A month's value is their mean weighted by cell area. The same cells are used in every month: when the cells holding a value make up less than `min_coverage` of that area, the month is left missing.
- For a count variable (earthquakes), the counts of every cell in the zone are added up.

The series is then tested exactly as one cell's is in the scan, so a zone of a single scanned cell gives that cell's own slope, interval and p-value.

Two things about the p-value, which `limitations` states for every tested variable and whatever shows the result must not hide. One test is made per variable, so there is nothing to correct for and `p_adjusted` equals `p_value`. And the zone was chosen by eye, most likely after looking at the map, so the p-value overstates the evidence. `significant` here means "below the threshold", not "significant after correction".

`/zone` was added after version 0.1 was first written. It adds a call and changes nothing that existed, so the version number has not changed. `schema.json` and `check.py` describe a results document and do not cover it. The fixture has no zone answer: without the service, the desktop app says that a zone's figures need it.

## Series as files beside the viewer

A stored scan carries series for the top of the ranking only. So that a plain website with no service behind it can still draw every finding's chart, the series can be written out as files, one per cell and variable, and put beside `index.html`:

```
series/<variable>/<cell_id>.json      for example series/vegetation/r49c114.json
```

Each file is a small results document:

| Field | Meaning |
|---|---|
| `contract_version`, `synthetic` | As in every document |
| `run_id` | The run the series belongs to. A front end must not draw a series beside a different run. |
| `series` | A list holding the one series, as in "A series" |

`python -m vigil.api.export` writes them (see `python/vigil/api/README.md`). There is one for every cell and variable that some finding's evidence draws: a finding's own cell with each of its variables, and for a contradiction the paired cell too.

A front end looks for a finding's series in this order: the results document it has, then the file, then the service (`GET /series?cell_id=...&variable=...`). A missing file is not an error. A web server may answer a missing path with an HTML page of its own, so a reply that is not JSON counts as missing.

## Presenting

A presenter can have other people's screens follow their own. The desktop app tells the service what is on show, and a viewer opened from that service follows it. Nothing here changes a result.

`GET /present` answers:

```json
{
  "presenting": true,
  "run_id": "scan-20261115T005506Z",
  "sequence": 12,
  "open_to_network": true,
  "show": {
    "finding_id": "trend.vegetation.r49c114",
    "variable": "vegetation",
    "zone": { "name": "Southern Amazon", "polygon": [[-2.0, -62.0], [-2.0, -50.0], [-16.0, -48.0]] }
  }
}
```

| Field | Meaning |
|---|---|
| `presenting` | Whether anyone is presenting. When `false`, `show` is `null`. |
| `run_id` | The run the service holds. A viewer follows only when this is the run it has on screen. |
| `sequence` | Goes up by one each time what is on show changes. A viewer acts when it sees one it has not just acted on, whether higher or lower: the count is kept in memory, so a service that is restarted starts it again. |
| `show.finding_id` | The finding to open, or `null` for none |
| `show.variable` | The globe layer to show, or `null` to leave it alone |
| `show.zone` | A zone to draw, as a name and an outline in the form "A zone" describes, or `null` |
| `open_to_network` | Whether the service was started to listen beyond its own machine, so the presenter can be told when nobody could connect. `null` when the service cannot tell. Viewers ignore it. |

`POST /present` sets it. The body is `{"presenting": true, "finding_id": ..., "variable": ..., "zone": ...}`, each of the last three optional, or `{"presenting": false}` to stop. The service takes it only from the machine it runs on, and answers 403 to anyone else, so a member of the audience cannot move everyone's screen.

A viewer asks once when it opens. If nothing answers, it never asks again: a website with no service behind it must stay quiet. If the service answers, the viewer asks again every two seconds and shows a switch that says it is following, which the person can turn off. Something on show for less than those two seconds may never reach a viewer, and when presenting stops the viewer stays on what it last saw.

## A variable year by year

`GET /years?variable=...` gives each scanned cell's mean anomaly in each calendar year, for looking at how a change arrived. It is not a result: no test is run on it and no finding rests on it.

| Field | Meaning |
|---|---|
| `contract_version`, `synthetic`, `run_id` | As elsewhere. A front end shows it only beside the run it has on screen. |
| `variable`, `units` | The variable's id and the units of the values |
| `years` | The calendar years, in order |
| `min_months` | How many months of data a year needs to be given a value |
| `rows`, `columns`, `lat_min`, `lat_max`, `lon_min`, `lon_max` | The grid the values are on, as in "A globe layer" |
| `scale_max` | Where a colour scale centred on zero can end: the 98th percentile of the sizes of the values, over every year. For drawing only. `null` when there are no values. |
| `values` | `values[year][row][column]`, rows south to north. `null` where the scan skipped the cell, or the year has too few months. |

A month's anomaly is its value minus that calendar month's mean over the whole period, as in "A series". A year's value is the mean of its months' anomalies. Only cells the stored scan tested have values, so the picture covers the same cells as the trend layer.

For a website with no service, the same answer is written as a file beside `index.html`, `years/<variable>.json`, by `python -m vigil.api.export`. A front end looks for the file first and asks the service second, and treats a reply that is not JSON as missing.

## Findings as a file for other tools

Both front ends can save the findings on screen as a file, to be opened in QGIS, a spreadsheet or a notebook. The file is made from the results document and nothing else, so it needs no service. Both write the same columns, in this order, so a file from one can be compared with a file from the other:

| Column | What it holds |
|---|---|
| `rank`, `id`, `kind`, `significant`, `summary` | As in the finding |
| `variables` | The ids of the finding's variables, joined by `+` |
| `cell_id`, `lat_centre`, `lon_centre`, `lat_min`, `lat_max`, `lon_min`, `lon_max` | The finding's cell |
| `period_start`, `period_end` | The finding's period |
| `slope_per_decade`, `units`, `ci_low`, `ci_high`, `ci_level` | A trend's `result`. For a contradiction, its own slope and units. For a change point, `units` is the shift's. |
| `p_value`, `p_adjusted` | A trend's, or a cascade's |
| `change_date`, `shift` | A change point's |
| `lag_months`, `correlation` | A cascade's |
| `paired_cell_id` | A contradiction's |
| `standing`, `retest_outcome`, `retest_product` | `standing.status`, and `standing.retest` when there is one, with the second product named as its mission and product |
| `score` | `score.total` |
| `run_id`, `synthetic` | The run the findings came from, and whether they are made up |

A column a finding's kind does not have is left empty. Numbers are written as the document has them, with a full stop for the decimal point.

- **CSV**: UTF-8, one header row, one row per finding in rank order, fields quoted when they hold a comma, a quote or a line break.
- **GeoJSON**: a `FeatureCollection` in longitude and latitude (WGS 84). Each finding is a `Feature` whose `id` is the finding's id, whose geometry is its cell as a `Polygon` with one closed ring, counter-clockwise from the south-west corner, and whose `properties` are the columns above, with empty ones as `null`. The collection carries a member `vigil` with `contract_version`, `run_id`, `synthetic` and `period`.
- The file is named `vigil-findings-<run_id>` with `.csv` or `.geojson`. For the fixture the name says `synthetic-fixture` in place of the run, so made-up findings cannot be mistaken for real ones once they have left the app.

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
| `steps` | What the Detective looked up on the way to the answer, in order. Optional: absent when it looked nothing up. |

An `answered` response cites at least one finding. Every number in `answer` comes from a cited finding.

Each entry of `steps` is one tool call the Detective made:

| Field | Meaning |
|---|---|
| `tool` | `search_findings`, `get_finding` or `get_run_summary` |
| `input` | The tool's arguments, as the Detective gave them |
| `summary` | One plain line saying what was asked for, such as "Searched for precipitation decreasing trends in 10°S to 0°N, 70°W to 50°W" |
| `finding_ids` | The ids of the findings that call returned. Empty for the run summary. |
| `matching` | For a search: how many findings matched in all, which can be more than were returned. Optional. |
| `box` | For a search that named a place: `lat_min`, `lat_max`, `lon_min`, `lon_max`. Optional. It is there so an app can draw where the Detective looked. |
| `failed` | True when the tool refused the request. Optional. |

`steps` describes the looking up, not the answer. A finding in a step was read by the Detective. Only the findings in `citations` back what it said. An app may list the steps and draw them on the globe, and must not present a step's findings as the answer's evidence.

The steps are recorded by the service from outside the Detective (`python/vigil/api/trace.py`), so they need no change to how it answers. `steps` was added after version 0.1 was first written. It is optional, so the version number has not changed. The fixture's sample answers have none.

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
