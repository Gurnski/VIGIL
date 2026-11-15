# web

The web viewer: a static React site that shows VIGIL's results in a browser. It has the globe with a variable's trend layer, the ranked findings list, the evidence view each finding opens into, zones drawn on the globe, the Detective chat, and an "About this scan" panel. It reads the same result contract as the desktop app (`contract/`).

Dan owns this folder. See `COORDINATION.md`, and "The web viewer" in `AGENTS.md` for how the code is written.

## Running it

You need Node 22.

```
cd web
npm install
npm run dev
```

`npm run build` type-checks and writes the site to `dist/`. Pushing to `main` builds it and puts it on the S3 bucket (`.github/workflows/deploy.yaml`).

`npm run build:root` builds, then copies the result to the root of the repository as `index.html` and `viewer/`. Those are committed, so a plain web server pointed at a checkout of the repository serves the viewer with nothing to install. Run it and commit the result with every change to the viewer.

## Where things are

| Path | What it is |
|---|---|
| `src/App.tsx`, `src/app.css` | The shell: header, the pages and the menus that choose between them, the Map page's toolbar and the shared state |
| `src/overview/` | The Overview page: four figures, the top of the ranking and the scan variable by variable |
| `src/contract.ts` | The result contract as TypeScript types |
| `src/data.ts` | Loads `snapshot.json` and `ask.json` from beside `index.html` |
| `src/service.ts` | Works out, once, which analysis service the page talks to |
| `src/present.ts` | Follows a presenter: asks the service what is on show and passes it to the shell |
| `src/theme.css` | Colours, spacing and type sizes, copied from the app's `Theme.cs` |
| `src/format.ts` | Labels and place names shared by the parts |
| `src/findings/` | The ranked findings list. Only the rows in and near the view are in the page. |
| `src/evidence/` | The evidence view a finding opens into: its series chart, numbers, score and sources, and what a re-test on a second product found. `series.ts` finds a finding's series, `report.ts` writes a finding out as text for "Copy as text", `recompute.ts` asks the service to run a trend's cell again, and `worldview.ts` makes the link that opens the finding's cell in NASA Worldview. |
| `src/globe/` | The 3D globe, its trend layer, markers, legend and the satellites on their orbits, and the rings and dashed outline for what the Detective points out. `field.ts` is what the layer shows, cell by cell: the trend, or one calendar year. `relief.ts` draws it as columns. `stage.ts` is where a place is and where the view stands, for the tilt and the flat map, and `outlines.ts` draws the lines over the surface. `years.ts` and `YearBar.tsx` are the year-by-year view. `places.ts` and `PlaceSearch.tsx` take the globe to a place by name. |
| `src/detective/` | The Detective chat, its call to `POST /ask`, and the list of what it looked up |
| `src/scan/` | The "About this scan" panel: the run's settings and counts, the variables and their sources, and how the re-tests came out. `export.ts` writes the findings as CSV and GeoJSON. |
| `src/zones/` | Zones: areas drawn on the globe, named and saved. `geometry.ts` works out a zone's grid cells, `store.ts` keeps the zones in the browser, `client.ts` asks the service for a zone's trends, and `ZonesPanel.tsx` and `ZoneSummaryView.tsx` show them. |
| `scripts/copy-fixture.mjs` | Copies the fixture from `contract/fixture/` into `public/` before a dev run or build |

## Results

Until real results exist the viewer shows the synthetic fixture and says so. To show a real scan, put its `snapshot.json` beside `index.html` on the server or in the bucket. Nothing needs rebuilding, and a deploy does not overwrite it. Without one the viewer reads `contract/fixture/snapshot.json`.

A scan's snapshot is large: 21 MB for the eight-variable scan, 2.3 MB gzipped. Serve it compressed. On a throttled phone profile the first findings appeared after 4 seconds with gzip and 22 seconds without, and nearly all of that is the download.

### A chart for every finding

A snapshot holds the series for the top of the ranking only. For any other finding the evidence view looks in this order, as the contract says under "Series as files beside the viewer":

1. the snapshot's own `series`
2. the file `series/<variable>/<cell_id>.json` beside `index.html`
3. the service, `GET /series?cell_id=...&variable=...`

So a site with no service draws every chart once the `series/` folder that `python -m vigil.api.export` writes is uploaded beside `index.html`. A missing file, or a reply that is not JSON, counts as "not here" and the next source is tried. A file whose `run_id` is not the run on screen is refused. The service's reply to `/series` does not name its run, so the viewer asks `GET /health` first and uses the service only when it holds the same run. When no source has the series, the view says so where the chart would be. Answers are remembered until the page is reloaded.

### Which service

The Detective, a zone's trends, recomputing a finding, a series the files do not hold, a variable's years the files do not hold, and following a presenter all need the Python service. The viewer works out which service to use once, in `src/service.ts`:

1. It asks the page's own address, `GET /health`. A JSON reply with `"status": "ok"` means the page was served by the service, and that is the one it uses, whatever the port. So opening the service's own address in a browser needs no setting.
2. Otherwise, the address in `VITE_VIGIL_API_URL`, set when building. The service must allow the page's address through CORS.
3. Otherwise, `http://127.0.0.1:8000` when the page is opened on localhost.
4. Otherwise there is no service. A plain website gets that one `/health` request, which its host answers with a 404 or a page of HTML, and nothing more is asked on the off-chance.

### The Detective

Without a service, and with the fixture on screen, the Detective gives the fixture's sample answers, marked as such. Over a real scan it gives none, and says there is no answer to show.

An answer's findings are matched to the list and the globe only when the answer's `run_id` is the run on screen. An answer that names another run keeps its citations as plain ids.

## Recompute

A trend finding's evidence has a "Recompute" button under its numbers when a service answered. It asks the service to run the finding's own cell again from the gridded data (`POST /analyse` with the cell's bounds) and sets the fresh slope, interval and p-value beside the stored ones, in the desktop app's words. The viewer compares the two and works out neither. Figures count as the same when they agree to within one part in a thousand million, the room a round trip through JSON needs. The adjusted p-value is not compared, because a run on one cell has nothing to correct for. The answer holds every variable's trend in the cell, so the one compared is matched on cell id and variable. A failure says why in place of the comparison. With no service the button is not there.

## Going to a place

The toolbar has a small "Go to a place" field over the desktop app's list of 41 Earth-science regions (`places.ts`, a copy of `Places.cs`). Matches appear as you type, and Enter or a tap flies the globe to one. Before anything is typed it offers the four places the desktop keeps as buttons. The matches hang below the toolbar and take no room in the page.

## Marker labels

A finding's marker label gives way when it would sit on one already placed, or when its dot would cover one. The chosen marker's label is placed first, then the rest in rank order. A label that would run off the right-hand edge goes to the left of its dot, and a marker whose label is on show is drawn above every other dot. This is done in the loop that already places the markers each frame the camera moves. A label's width is measured once. A label that gave way still shows under the pointer.

## Relief

The "Relief" switch draws the chosen layer as columns. Each cell the layer would paint stands up from the globe, as tall as its value is large against the colour scale's range, whichever way it goes. The top of a column has the colour the layer gives the cell, and the legend says what height means. The flat layer and the columns are drawn from one description of the layer (`field.ts`), so "Significant only" and the years apply to both alike. With "Layer" off there is nothing to raise, so the switch is disabled.

The columns are one mesh in the globe's scene (`relief.ts`), not react-globe.gl's points layer. That layer draws cylinders, which with four sides come out as diamonds turned 45 degrees to the grid and do not tile. The tallest column is 0.06 globe radii. The desktop app's is 0.09. Here it stops just under the lowest orbit drawn, the GPM Core Observatory at 0.064, so no column stands through a satellite's ring. A finding's marker and the chosen cell's outline are lifted to the top of the column. The dashed box for a Detective search stays on the surface.

## Tilt and the flat map

Two switches change how the globe is looked at, and nothing else. Neither changes a value, a colour or a finding.

- "Tilt" leans the view over by up to 50 degrees, as the desktop app does, so that a column of the relief is seen from its side and not as a lid. Switching "Relief" on switches it on too. On the globe the view swings down about the place beneath it, and less far when it is a long way out, where a full tilt would drop the globe half out of the picture.
- "Flat map" unrolls the globe into a flat map, 180° W to 180° E with north up, and rolls it up again. Drag the map to move it, and scroll or pinch to zoom. The layer, the columns, the markers, outlines and zones all go with it, and a zone can be drawn on it. The satellites are not shown on it, because their orbits are drawn round the globe, and their switch is disabled while it is on.

Both are animated, and neither is when the browser asks for reduced motion.

How it is built (`stage.ts`):

- react-globe.gl draws a sphere and turns a camera about its centre. It has no tilt and no flat map. So the rest of the viewer asks the stage, not the library, for four things: a place's position (`coords`), its spot on screen (`screen`), the place under the pointer (`pick`) and a flight to somewhere (`flyTo`). Anything new on the globe must do the same, or it will stay behind when the globe unrolls.
- `coords` takes the fold into account: 0 is the globe, 1 is the flat map. Between the two the sphere's radius grows, so the surface unrolls from the far side and distances along it stay what they were. The Earth's own mesh and the layer's are moved point by point, and the columns and outlines are placed again, each frame of the fold.
- The library keeps its own camera and controls, and they still turn the globe. The scene is drawn through the stage's camera, which follows the library's and adds the tilt. On the flat map the library's controls are off and the stage moves its own camera from the pointer.
- The outlines are the viewer's own lines (`outlines.ts`), not the library's paths, so that they fold too. Their thickness is in pixels.
- On the flat map everything stands 2.5 times as far off the surface as on the globe, columns included. A column there is seen from much further away, and there is no orbit for it to stand through.

The globe opens by growing and turning, which is the library's doing. The layer, the columns and the satellites' orbits are in the group it animates, so they open with it. The layer fades in over the same moment, the columns stand up from the surface when "Relief" is switched on, and the markers fade in once the globe has opened.

## Years

The "Years" switch shows one calendar year at a time in place of the trend: each scanned cell's mean for that year against its own normal (the contract's "A variable year by year"). A bar over the bottom of the globe chooses the year. It can be dragged, clicked or moved with the arrow keys, and its play button steps a year every 650 ms and stops at the last. The desktop app does the same, with the same words on the legend.

- The values come from `years/<variable>.json` beside `index.html`, then from the service's `GET /years`. A file is about 1.4 MB, so it is fetched only when the switch is on for that variable, and kept. A reply that is not JSON counts as missing, and one for another run is refused.
- Every year shares one colour scale, centred on zero and ending at the document's `scale_max` rounded up to two significant figures.
- A year's values are not a finding and no test is run on them. "Significant only" is disabled while the years are on, and the legend says "Each year against the cell's own normal".
- When neither source has the years, the switch goes back off and a line under the toolbar says why. The fixture has none, so that is what it shows.

## Following a presenter

When the page has a service, it asks `GET /present` once as it opens (the contract's "Presenting"). With no answer in the contract's shape it never asks again. With one, it asks every two seconds. While someone is presenting the run on screen, a "Following the presenter" switch appears in the header, on. Each new `sequence` then opens the finding on show, switches to its layer and draws its zone as an outline with its name. A value left null leaves that part of the screen alone. Turning the switch off stops all of it, and turning it back on catches up. Opening something by hand does not turn it off. When presenting stops, the switch goes and the page stays as it is.

A presenter never takes a zone down again, so a line under the toolbar names the zone and has a button to remove it. The zone is not saved. While it is on the globe it takes the place of the person's own chosen zone there, and deletes nothing: theirs still limits what is shown, and is drawn again when the presenter's is removed.

## Zones

A zone is an area drawn on the globe, named and saved. The "Zones" tab beside "Findings" has everything about them. It follows the desktop app's zones and the "Zones" section of `AGENTS.md`.

Drawing: "Draw a zone" puts the globe in a drawing state. A click or tap that does not drag lays a point, and dragging still turns the globe. A bar over the globe has Undo, Finish and Cancel. Clicking the first point again also closes the outline, and Escape abandons it. The desktop app draws with a pen stroke; the viewer lays points, which works with a finger.

Saving: a closed outline waits for a name ("Zone 1", "Zone 2" and so on by default), with the number of grid cells it takes in shown before it is saved. An outline that holds no cell centre, or that goes all the way round a pole, cannot be saved, and the bar says which. Zones are kept in the browser's localStorage under `vigil.zones`, as a name and an outline and nothing else. A zone belongs to no scan.

Cells: a zone is the grid cells whose centres its outline holds, by the contract's rule under "A zone". `geometry.ts` is a port of `ZoneGeometry.cs`, and the service follows the same rule. Change the rule in all three or in none.

Choosing a zone limits the findings list, the markers and the layer (flat, relief and years alike) to its cells. The outline is drawn with the stepped edge of its cells, dashed, so the area the numbers cover is on screen beside the outline that was drawn. The colour scale still comes from every cell. "Whole globe" lets go of the zone.

Trends: a zone's series and trends come from `POST /zone` on the service, and the viewer works none of it out. It shows one row per variable, then the chosen variable's series and every limitation the service sent.

- A zone's p-value is one test on an area chosen by eye. No row is called "significant after correction", and a zone's numbers are never attached to a question for the Detective.
- A row with the service's `caution` shows that text in the warning colour in place of the interval, and its trend is not given a significant trend's weight.
- With no service, a service holding another run, or an answer about other cells than the ones outlined, the panel gives the reason and no figures. The limiting still works.

## Files for other tools

"About this scan" has two downloads, made in the browser from the results on screen: the findings as CSV and as GeoJSON. The columns, their order and the file names are the contract's ("Findings as a file for other tools"), the same as the desktop app writes. Each GeoJSON feature is a finding's cell as a polygon in longitude and latitude. Figures are written as the document holds them, so a p-value stored as 0 is written as 0. The desktop app writes very small numbers as `1E-07` where the viewer writes `1e-7`. Both read as the same number.

## What the desktop app has that the viewer does not

The desktop app is the main one and gets new features first. This table is the state of the viewer on 15 November 2026. "Needs the service" is about the feature as built: "no" means it works on a plain website.

| Feature | Desktop | Web | Needs the service |
|---|---|---|---|
| The four pages: Overview, Map, Evidence, Detective | Yes, in a rail on the left | Yes, in the header, and in a menu along the bottom on a phone | No |
| Overview: four figures, the top of the ranking, the scan variable by variable | Yes | Yes | No |
| Globe with a trend layer per variable, and its legend | Yes | Yes | No |
| "Significant only" | Yes | Yes | No |
| Relief: the layer as columns | Yes | Yes | No |
| Tilt: the view leaned over to show the columns' height | Yes, by right-drag, and when Relief is switched on | Yes, by a switch, and when Relief is switched on | No |
| Flat map: the globe unrolled, with everything on it | Yes, with day and night on the map and columns twice as tall | Yes, with columns 2.5 times as tall | No |
| Satellites on their orbits, the card about each, the switch | Yes | Yes | No |
| A marker per cell with a finding | Every cell | The cells of the top 200 findings | No |
| Ranked findings list | Yes | Yes, with only the rows near the view in the page | No |
| Kind filter on the list (All, Trends, Change points, Cascades, Contradictions) | Yes | Yes | No |
| Variable filter on the list (a finding is listed under each variable it involves, so a cascade under both) | No | Yes | No |
| Markers on the globe follow the list's kind and variable filters | No | Yes | No |
| Evidence: figures, score, re-test, method, sources, limitations, related findings | Yes | Yes | No |
| A link from a finding to its cell in NASA Worldview | Yes | Yes | No |
| A chart for every finding | Yes, from the service | Yes, from the snapshot, then a file, then the service | No, with the `series/` files beside the page |
| Recompute a trend finding from the gridded data | Yes | Yes | Yes (`POST /analyse`) |
| Copy a finding as text | Yes | Yes | No |
| Findings as CSV and GeoJSON | Yes | Yes | No |
| Overview: counts, variables and sources, re-tests, settings | A page | The "About this scan" panel | No |
| Overview: the top of the ranking as its own block | Yes | No. The list is always on screen. | No |
| The Detective: chat, citations, the lookups it made, showing them on the globe | Yes | Yes | Yes. Without it, sample answers on the fixture only. |
| Saved conversations | Yes, on disk | No. One conversation, gone on reload. | No |
| Zones: draw, name, save, delete | Yes, with a pen stroke | Yes, by laying points | No |
| A chosen zone limits the findings, the markers and the layer | Yes | Yes | No |
| A zone's trends, series and limitations | Yes | Yes | Yes (`POST /zone`) |
| Following a presenter (`GET /present`) | Presents | Follows | Yes |
| A badge saying whether the service is there, asked every five seconds | Yes | No. The page asks once when it opens, and a static site is asked nothing more. | Yes |
| A variable year by year, with a year bar | Yes | Yes | No, with the `years/` files beside the page |
| Fly to a place by name, from the list of 41 | Yes, with four presets as buttons | Yes. The four presets are offered before anything is typed. | No |
| Grid lines, the rotate switch | Yes | No | No |
| Selecting a box on the globe | Yes | No | No |
| Sunlight from the real sun, night lights | Yes | No. The globe is evenly lit. | No |
| Components gallery | Yes | No. It is a developer's page. | No |

## Pages

The viewer has the desktop app's four pages, in its order, and opens on the first as the app does.

- Overview: what the scan covered and found in four figures, the top eight of the ranking, and the scan variable by variable with its settings and how the re-tests came out. Choosing a finding opens its evidence. Every figure is read from the results document.
- Map: the globe with its toolbar, and on a wide screen the findings and the zones beside it.
- Evidence: on a wide screen the findings on the left and the chosen one's evidence beside them, with "Show on the map". On a phone it is the list, which opens into the evidence.
- Detective: the chat as a page. On a wide screen "Ask the Detective" also opens it as a panel over another page. Both are the one conversation.

On a wide screen the pages are a row in the header. On a phone they are a menu along the bottom, each with its mark.

- The globe is made the first time the Map is opened and kept after that, as the desktop app makes its pages. A phone that only reads the Overview never fetches the Earth's image.
- Something put on the globe from elsewhere goes to the Map. A chosen finding stays on the Map where the findings are beside it, and otherwise goes to Evidence. Leaving the Detective's page that way on a wide screen brings the conversation along in its panel.
- The page that is away is moved off the side of the screen and marked `inert`. It is not removed and not hidden, for the reasons under "On a phone".
- Two findings lists can be on the page at once on a wide screen, of different widths. Each keeps its own measured row heights, by the `place` it is given. One shared between them would have each undo the other's measuring without end.

## On a phone

Below 900 pixels one page fills the screen at a time, chosen from the menu along the bottom: Overview, Map, Evidence and Detective. Only the Overview scrolls. From 900 pixels up the menu is not shown and the pages are in the header.

- The header is one line: the name, the fixture badge, "Following the presenter" when someone presents, and "About this scan". "Download for Windows" and "Ask the Detective" are hidden: the first is no use on a phone, and the Detective has its page.
- On the Map the layer row and the place field stay on screen. The switches (Layer, Significant only, Relief, Tilt, Flat map, Years, Satellites) are in a panel that "Map options" opens above the tabs.
- The legend sits along the bottom of the globe's area, clear of the sphere, and moves to the top while the year bar has the bottom.
- Choosing a finding, in a list or by its dot on the globe, opens its evidence on the Evidence page. Something the Detective shows on the globe switches to the Map.
- The part that is away is moved off the side of the screen, not removed: the globe keeps its size and the list keeps its place. Do not hide either with `display: none` or `visibility: hidden`: a list with no height loses its scroll position, and the globe's markers set their own visibility and show through.
- Buttons, switches, layer and filter choices are at least 40 pixels tall, and a dot on the globe has a 40 pixel target.

The list holds thousands of findings, so it draws only the rows in and near the view, about a dozen at a time. Rows are as tall as their text. Each is measured when it is first drawn and the rest are taken to be the average until then. The arrow keys, Page Up, Page Down, Home and End still move the selection, and each row tells a screen reader its place in the whole list.
