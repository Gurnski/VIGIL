# Project page: draft text

Draft text for the written project page on the Space Apps website. Every figure in it is from the scan frozen on Sunday morning, run `scan-20261115T005506Z`. If the scan is rerun, the figures change: take them again from the app's Overview page and from `run.retest` in the results document.

Dan to check before publishing: the team names, the wording of the two declarations against the Space Apps rules, and the link to the video.

## High-level summary

VIGIL is an Earth system trend detective. It scans twenty-five years of NASA records, one grid cell at a time and every cell the same way, to find where the planet is changing, by how much and how surely. Nothing is chosen in advance: the findings rank themselves, and the app opens on whatever the scan put first. Each finding opens into its evidence, with its uncertainty, its method, its sources and its limits. Where a second NASA record of the same variable exists, VIGIL tests each trend again on it and writes the outcome on the finding, including when the second record disagrees. An AI Detective answers questions from the findings alone, shows what it looked up, and says when the results cannot settle a question.

## What it does

- **Scans without being told where to look.** Eight variables on one 2.5° grid, monthly from January 2001 to December 2025: air temperature, precipitation, soil moisture, vegetation, snow cover, sea surface temperature, chlorophyll at the sea surface, and earthquakes. The scan ran 19,001 tests. 5,450 were significant after correction for the number of tests.
- **Looks across systems.** Beyond trends it finds change points (a level that shifts at a date), cascades (one variable trailing another in the same cell, reported as an association) and contradictions (neighbouring cells trending opposite ways). The scan holds 6,122 findings in all: 5,450 trends, 346 cascades, 301 change points and 25 contradictions.
- **Shows its evidence.** Every finding has its series, its slope with a confidence interval, its p-value before and after correction, the test and its settings, the product it came from, and the limitations the analysis wrote for it. "Recompute" reruns a finding's cell from the gridded data and sets the fresh figures beside the stored ones.
- **Tests its own findings again.** Trends in five variables are re-tested on a second product. The results are in the table below.
- **Lets you draw a region.** Draw any outline on the globe, in the desktop app or in a browser, and VIGIL works out each variable's trend over it with the scan's own test, and says that a region chosen by eye gives exploratory p-values.
- **Shows size as height.** With "Relief" on, each cell rises from the globe by the size of its trend, so a region changing fast stands out from one changing slowly without reading a legend. The colour still says which way.
- **Plays the years.** "Years" swaps the trend for one year at a time, each cell against its own normal, and plays from 2001 to 2025. The Amazon's dry years and the North Pacific's warm ones arrive on screen as they happened. This view is for looking: no test is run on it and no finding rests on it.
- **Takes a room with it.** The service can hand the web viewer to any phone or laptop on the same network. Press Present in the desktop app and those screens follow the finding, layer and zone the presenter chooses, and anyone can stop following and explore alone.
- **Hands its results on.** Every finding exports as CSV and as GeoJSON, with its cell as a polygon, its uncertainty and its re-test outcome, ready for QGIS or a notebook. Each finding also links to its cell in NASA Worldview, to look at the place itself.
- **Answers questions from the findings.** The Detective searches the findings with tools, and every number in its answer must appear in a finding it cites, or the answer is not shown. The lookups it made are listed under the answer and drawn on the globe.

## How it works

1. **One grid.** Each NASA product is read month by month, its fill values and quality flags applied, and averaged onto 2.5° cells by area.
2. **One test for every cell.** Each cell's series has its seasonal cycle removed. The trend test is Mann-Kendall with the Hamed-Rao correction for autocorrelation, and the size of the trend is Sen's slope with a 95% interval. A second test (trend-free pre-whitening) is run beside it as a robustness check.
3. **Correction for many tests.** Benjamini-Hochberg false discovery rate control across each variable's tests. A cell is skipped, with the reason recorded, when under 90% of its months hold a value.
4. **A ranking anyone can recompute.** The score combines effect size, certainty, coverage, robustness and links to other systems, with the weights written in the run record.
5. **A re-test.** Each trend is tested again, with the same test, on a second product for the same variable. Only the direction and the significance are compared. A trend is called re-tested only when the second record agrees and is significant on its own.

The desktop app (C#, .NET 8, Windows Forms) and the web viewer (React) only display what the Python service returns. No statistic is computed in either, and none by a language model. The web viewer has nearly everything the desktop app has, and the public site can run from files alone once they are uploaded beside it: the scan, a chart for every finding and every variable year by year. A zone's trends, Recompute and the Detective are the parts that need the service.

## What the re-test found

| Variable | Second product | Trends tested | Confirmed | Same direction, not significant | Opposite | Could not be tested | Second product significant where the scan found nothing |
|---|---|---|---|---|---|---|---|
| Air temperature | GISTEMP | 888 | 828 | 6 | 54 | 0 | 1,092 of 1,448 cells |
| Precipitation | GPCP | 269 | 229 | 40 | 0 | 0 | 374 of 2,067 cells |
| Soil moisture | MERRA-2 land model | 784 | 227 | 272 | 284 | 1 | 504 of 1,508 cells |
| Vegetation | Aqua MODIS | 632 | 520 | 70 | 25 | 17 | 179 of 706 cells |
| Sea surface temperature | GISTEMP (ERSST) | 2,474 | 2,222 | 247 | 5 | 0 | 619 of 2,460 cells |

How to read it:

- The last column is the rate to hold the confirmations against. For air temperature the second record is significant in three quarters of the cells where the scan found nothing, so a confirmation says little there. For precipitation, vegetation and sea surface temperature it is significant in about a quarter of them or fewer, so a confirmation says much more.
- Soil moisture is the variable to trust least. The two products are both models, and they disagree about the direction of the trend in 284 of the 784 cells where the scan found one.
- The 54 cooling trends the scan found in air temperature are all contradicted by GISTEMP. GISTEMP is smoothed over 1,200 km and warming almost everywhere, so it would contradict nearly any local cooling: each of those findings carries a caution, not a verdict.
- None of the second products is fully independent of the first, and each finding says what the two have in common.

## Known-answer check

We drew boxes round six places with well-documented change and asked VIGIL for the trend of each variable over each box, the same way it does for a zone a user draws. The boxes were fixed before the first was looked at. The figures are per decade, with the 95% interval in brackets.

| Place | What is documented | What VIGIL reports for the box |
|---|---|---|
| Loess Plateau, China | Greening after large-scale replanting | Vegetation rising at +0.036 NDVI (+0.031 to +0.040). The scan's own top-ranked findings are here too. |
| Southern Amazon | A longer dry season and warming along the arc of deforestation | Air temperature +0.60 K (+0.29 to +0.92), precipitation -11.8 mm per month (-17.3 to -6.4), soil moisture falling. No significant change in vegetation over the whole box. |
| Sahel | Re-greening since the droughts of the 1980s | Vegetation rising at +0.0079 NDVI (+0.0044 to +0.0122). |
| Himalaya and Tibetan Plateau | Warming faster than the global mean | Air temperature +0.20 K (+0.02 to +0.37), and vegetation rising. |
| California | Multi-year drought and warming | Air temperature +0.43 K (+0.17 to +0.69). No significant trend in precipitation or soil moisture. |
| Murray-Darling basin | The Millennium drought and its end | Vegetation rising at +0.022 NDVI (+0.003 to +0.041). No significant trend in precipitation or soil moisture. |

Three of the six come out as documented: the Loess Plateau, the southern Amazon and the Sahel. The Himalaya box is warming, but its interval is wide and it does not show warming faster than the global mean. California and the Murray-Darling show what a trend test cannot do: both droughts were real, but neither is a steady change over 25 years, so VIGIL reports no trend in rainfall there, and it would be wrong to. Two results in these boxes should not be trusted. MERRA-2 air temperature falls over the Sahel box, and every cooling trend the scan found in MERRA-2 is contradicted by GISTEMP. Snow cover over the Murray-Darling shows a tiny significant rise, in a place where the satellite sees almost no snow: a few misread pixels can make a trend out of almost nothing. A box chosen by us gives exploratory p-values, like any zone.

## A cluster we distrusted, and checked

The scan found 130 change points in sea surface temperature, and 56 of them fall between March and September 2013. Our own checklist says that many change points in one month usually means an instrument or a processing change, not the planet. So we looked for the same step in the second record, which is built from ships and buoys.

Measured across 36 months either side of each stored date, the median step is +0.99 K in the satellite record and +0.78 K in the second. Four years earlier and four years later, the same measure is within 0.1 K of zero in both records, so this is not just a warming sea showing up at any date. In all 56 cells the second record's step is larger at the stored date than at either of the other two.

The cells make three patches: the Gulf of Alaska and Bering Sea, the western North Atlantic, and the sea off Baja California. The second record is smoothed over 1,200 km, so that is three agreements and not 56. In the Gulf of Alaska the model's own range for the date runs from August 2012 to November 2014, which takes in late 2013, when the North Pacific marine heatwave known as "the Blob" was first reported there. VIGIL was not told about it.

This comparison is a description, not a second test, and the change points themselves stay exploratory.

## What VIGIL does not claim

- A cascade is a lagged correlation in one cell. It is an association, not a cause.
- A significant trend is not necessarily a large one. The size and its interval are shown beside every p-value.
- "Re-tested" means a second record agreed. It does not mean proven.
- A finding describes a 2.5° cell, not a place inside it.
- A rising count of earthquakes can be better detection. The catalogue grows more complete with time.
- Snow cover and the two ocean variables were added on the Saturday night, after the first scan's results had been seen. They went through the same rules, but they were not part of the scan as first declared.
- Vegetation was read a second time that night, keeping only the pixels MODIS marks as good or marginal. That is the better reading, but it was made after the first results had been seen, and the top five findings are vegetation. It left 1,355 cells with enough months to test, where the first reading had 2,133. Most of the cells lost are north of 30°N, where winter snow makes the index unusable. They are untested, not refuted.
- The second record for sea surface temperature was chosen after the scan's sea temperature results had been seen.

## Data

NASA: MERRA-2 (`M2TMNXSLV`, `M2TMNXLND`), GPM IMERG (`GPM_3IMERGM`), GLDAS Noah (`GLDAS_NOAH025_M`), Terra MODIS (`MOD13C2`, `MOD10CM`), Aqua MODIS (`MYD13C2`, `MODISA_L3m_SST`, `MODISA_L3m_CHL`), GISTEMP v4, GPCP (`GPCPMON`), and Blue Marble and Black Marble imagery for the globe. All were fetched through NASA Earthdata except GISTEMP, which is from NASA GISS.

Other: the USGS earthquake catalogue.

## Declarations

**Work done before the event.** The desktop app's shell: the project structure, a splash screen, a theme, a set of interface controls and a 3D globe drawn from NASA's public-domain imagery. It held no Earth-observation data and ran no analysis. It was pushed as one labelled commit before the event. Everything that reads data, analyses it or presents a result was built during the weekend.

**Use of AI.** AI coding assistants (Claude) wrote much of the code under the team's direction and review, and drafted documentation. The AI Detective inside the product uses a language model to choose which findings to look up and to word its answers. No statistic in VIGIL is computed or altered by a language model, and every result can be reproduced without one.
