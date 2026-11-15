# Checks on the final scan

The scripts that were run on the scan before it was frozen, and what they printed. They read a results document and the gridded data it was made from. None of them changes either.

| File | What it does |
|---|---|
| `final-run.sh` | Runs everything in order: build, scan, re-test, the contract checker, then the four scripts below. About five minutes. |
| `accept.py` | For one finding of every variable, a zone of that one cell and a fresh analysis of that one cell must both give the stored slope, interval and p-value |
| `sanity.py` | The counts per variable, the skipped tests by reason, where change points cluster, the top of the ranking, and how the document compares with an earlier one |
| `track.py` | Whether each second product follows the scan's own record month by month. A product read wrongly would correlate near zero. |
| `known_answers.py` | The trend of every variable over six boxes round places with documented change |
| `sst_2013.py` | Whether the sea surface temperature change points of mid-2013 show in the second record too. `sst-2013.log` is what it printed. |
| `grounding_selfcheck.py` | Whether the Detective's grounding check would accept a correct answer about each real trend, written from the finding's own figures. No model is involved. |
| `check_export.py` | Checks an exported CSV and GeoJSON, from either app, against the results document they came from |
| `final-run.log` | What all of that printed for the frozen scan, run `scan-20261115T005506Z` |
| `known-answers.json` | The known-answer results in full |

Run them from `python/` with `PYTHONPATH=.`, for example:

```
.venv\Scripts\python ..\docs\submission\checks\accept.py ..\data\scan\snapshot.json ..\data\scan\scan-data.npz
```

## What the log shows

- The contract checker passes and the acceptance check says "ALL SAME" for all eight variables.
- Seven variables' findings are identical to the scan before it. Vegetation differs because its Terra record was read again keeping only pixels the product marks as good or marginal. That left 1,355 cells with enough months to test where there had been 2,133, most of the loss being north of 30°N where winter snow makes the index unusable. 620 of the 632 vegetation trends in the frozen scan were also in the earlier one.
- Month by month, the second products follow the scan's records with a median correlation of 0.97 (precipitation), 0.92 (vegetation), 0.89 (air temperature), 0.77 (sea surface temperature) and 0.50 (soil moisture). Soil moisture is low because the two are different land models, which is why it is the variable to trust least.
- Change points in sea surface temperature cluster in mid-2013: 56 of the 130 fall between March and September of that year. Many change points in one month usually means a sensor or a processing change, so this was checked against the second record, which is made from ships and buoys. Measured across 36 months either side of the stored date, the median step is +0.99 K in the scan's record and +0.78 K in the second. Four years earlier and four years later, the same measure is within 0.1 K of zero in both. In all 56 cells the second record's step is larger at the stored date than at either of the other two. So the step belongs to 2013 and is in the sea, not in the sensor. The cells make three patches: the Gulf of Alaska and Bering Sea (24), the western North Atlantic (27), and off Baja California and Hawaii (5). The second record is smoothed over 1,200 km, so these are three agreements and not 56. `sst_2013.py` describes and does not test.
- The grounding self-check found a fault, now fixed: a count written with commas between its thousands ("19,001 tests") was read as two numbers and the answer thrown away. After the fix, of 10,900 correct answers built from the real findings, 43 are still refused, all for the same reason and by design: a figure rounded to a whole number ("fell by 30" for 29.7) must match exactly, so that the model cannot round its way to a number that is not there. Written to three significant figures, as the prompt asks, they pass.
- The Detective itself has not been run against a model on this scan: the machine the scan was made on has no key. `python/vigil/detective/live_check.py` is the script for that.
- Following a presenter was run end to end on one machine, with nothing stubbed: a copy of the service, the built viewer opened from it in headless Chromium, and the real desktop app presenting. The page opened each finding the app chose, ended on the last after six quick clicks down the list, stayed where it was when presenting stopped, and carried on following after the service was restarted and its sequence began again. A viewer asks every two seconds, so something shown for less than that can be missed. It has not been tried from a second device, with `--host 0.0.0.0`, or through a firewall or a venue's network.
- The deploy workflow's new step, which puts the bucket's real scan into the app's download, was run against a stand-in for `aws` with five kinds of object: a compressed scan and a plain one both end up in the zip, and a missing object, the made-up fixture and a page of HTML all leave the zip as the exe alone, with the step succeeding each time. The workflow itself has not run on GitHub since the change.
- The run record gives the code version as commit `db6a689`, but the scan was made from a working tree with uncommitted changes on top of it. Run `final-run.sh` again after committing if the record must name the exact code. It was run once more after the freeze, from this folder, and gave the same findings, layers and re-test counts to the last digit: only the run's id and time differ.
