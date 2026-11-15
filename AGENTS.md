# Working on VIGIL

Read this before changing code. It applies to people and to LLM coding agents alike. For what the project is and how to run it, see `README.md`.

## Before you edit anything

Read `COORDINATION.md` first. Several people and their agents work in this repo at once, and that file says which paths are yours to edit and what to do when a change falls outside them. This file covers how to write the code; that one covers which code is yours to write.

The plan for the hackathon weekend (architecture, scope, schedule) is in `docs/brief/Project_Brief.pdf`.

## Stack

- C# on .NET 8, Windows Forms (`net8.0-windows`)
- Nullable reference types and implicit usings are on (see `VIGIL/VIGIL/VIGIL.csproj`)
- One project, one solution: `VIGIL/VIGIL.sln`
- No NuGet packages yet. Ask before adding one, and say why it beats writing the code ourselves.
- A Python analysis service (FastAPI) under `python/`, which the app calls over HTTP on localhost. It can also serve the built web viewer itself, to any device on the same network (`python/vigil/api/site.py`). The service itself is `python/vigil/api/`: its README lists the calls, the settings and how a stored scan is made from the pipeline's data. All data handling and statistics live there, not in C#. The app talks to it with `HttpClient` and `System.Text.Json`, which need no packages.
- The JSON format the two halves exchange is the result contract in `contract/`. It is read-only for everyone except Dan.
- A web viewer under `web/`: React 18 and TypeScript, built with Vite into static files. It reads the same contract. See "The web viewer" below.

## Language: UK English

Use UK spelling everywhere we control: identifiers, comments, UI text, docs and commit messages. That means colour, initialise, visualise, analyse, centre, behaviour, catalogue, licence (noun).

The exception is names that come from .NET or a library, which we can't rename: `Color`, `ForeColor`, `BackColor`, `InitializeComponent()`, `ApplicationConfiguration.Initialize()`, `CenterScreen`, `DimGray` and so on. Wrap them as they are. Don't create aliases just to change the spelling.

Example from `LoaderForm.cs`: the field is `taglineColour` even though its type is `Color`.

## How startup works

1. `Program.Main` creates `LoaderForm` and calls `ShowDialog()`.
2. `LoaderForm.OnShown` starts the tagline rotation and awaits `InitialiseAppAsync()`, plus a minimum display time (`MinimumDisplayTime`, currently 6 seconds).
3. On success the loader sets `DialogResult.OK` and closes. `Program` takes the loaded results from `LoaderForm.Results` and runs `MainForm` with them. `MainForm` is the app shell: a nav rail on the left and the selected page on the right. It opens on the Overview page.
4. If `InitialiseAppAsync()` throws, the loader shows an error box and returns `DialogResult.Abort`, and the app exits.

Anything the app needs before the main window opens (config, dataset catalogue, API checks, caches) goes in `InitialiseAppAsync()`. It runs on a background thread through `Task.Run`, so it must not touch controls directly. Hand results to `MainForm` through its constructor or a shared service rather than through static state. So far it loads one thing, the results. It asks the Python service for its stored scan (`GET /snapshot`, through `Data/ResultsClient.cs`) and gives up after a few seconds when nothing is listening. Then it looks for a stored scan beside the exe, `snapshot.json` (`ResultsReader.ReadBeside`), which the download from the site carries so that it opens on the real findings with no service. Last, it falls back on the snapshot embedded in the exe, `contract/fixture/snapshot.json`. That one is made up and the app says so. Zones' trends, Recompute, Years, presenting and the Detective need the service whichever scan is on screen.

## Where code goes

Everything lives under `VIGIL/VIGIL/`, in folders with matching namespaces (`VIGIL.Controls`, `VIGIL.Pages` and so on). `Program.cs` is the only file at the top level.

| Folder | What goes there |
|---|---|
| `Theming/` | Colours, fonts, icon glyphs, drawing helpers, dark window chrome. No controls. |
| `Controls/` | Reusable custom-drawn controls (buttons, cards, chart, nav rail and so on) |
| `Forms/` | Windows (`Form` subclasses) and their `.Designer.cs` and `.resx` files |
| `Pages/` | Screens shown inside `MainForm`, one `UserControl` per nav item |
| `Data/` | The clients for the Python service: they call the API and turn its JSON into `Models/` types. `DetectiveClient` asks the Detective, and `ResultsClient` fetches the stored scan and a zone's trends. No dataset parsing and no statistics. Also `ResultsReader`, which reads a results document and the embedded snapshot, the saved chats (`ChatService`), the saved zones (`ZoneService`), the geometry of a zone's outline (`ZoneGeometry`) and the embedded fixture (`Fixture`). |
| `Models/` | Plain data types that mirror the result contract: the results document, findings, series, globe layers and the run record, plus the Detective's question and answer, a finding's summary, a saved conversation, a saved zone and the service's answer about a zone. Property names follow the contract through `JsonNamingPolicy.SnakeCaseLower`, so `p_adjusted` is `PAdjusted`. |

There is no C# `Analysis/` folder. Trends, significance, change points and every other statistic are computed by the Python service, and the app only displays what the service returns.

Keep `Data/` and `Models/` free of WinForms references so they can be tested without a window. Pages call into them, not the other way round.

## UI framework

The look is flat and dark: neutral graphite surfaces, an off-white accent (`Theme.Accent`), and colour kept for data and for meaning. There are no see-through panels, no glows and no gradient fills. Open the app and click **Components** in the nav rail to see every control working with demo data. That page is `Pages/GalleryPage.cs`, and it's the quickest way to see how each control is used.

### Rules

- Never hard-code a colour, font or spacing. Use `Theme` tokens: colours (`Theme.Surface`, `Theme.TextSecondary`), fonts (`Theme.Body`, `Theme.Caption`, `Theme.Display`), and the spacing scale `Theme.Space1` to `Space10` (4px base) with `Gutter`, `CardPadding`, `PagePadding` and `ControlHeight`. Add new tokens to `Theme.cs`.
- Surfaces are separated by tone, not outlines. A card is `Surface` on `Background`, and an input is `SurfaceRaised` on `Surface`. Draw a `Border` only on interactive inputs, on badges, and on panels that sit over the globe. Use `Divider` for hairlines and `GridLine` in charts.
- Status colours (`Positive`, `Negative`, `Warning`) carry meaning, not direction, and go on the one element that carries it. A rising temperature isn't "positive". `TextSecondary` means "not significant".
- No decoration: no translucent panels, glows, gradient fills, tinted pills or uppercase labels, and no middle dots (`·`) joining text. Corners are `Theme.RadiusSmall` (3px) or `Theme.Radius` (4px).
- `Theme.Accent` marks what can be acted on and what is selected. A measured series in a chart is `Theme.Series`. Don't use one for the other.
- Small labels (card titles, tile captions) go through `Draw.Caption`, in sentence case. Figures that must line up (axis labels, read-outs, tables) use `Theme.Numeric`, `NumericStrong` or `NumericSmall`. Classic Segoe UI has tabular digits and the Variable faces don't.
- Pixel convention after `Draw.Smooth`: fill at whole-number coordinates, and put 1px strokes on x + 0.5 (`OutlineBounds` does this) or use `Draw.HorizontalLine`/`VerticalLine`.
- Icons are Segoe Fluent Icons glyphs from `Theming/Icons.cs`. Add new ones there.
- New windows inherit `VigilForm`, not `Form`. Use `AutoScaleMode.Dpi` with `AutoScaleDimensions` of 96, as `MainForm.Designer.cs` does. Font-based scaling would resize everything because the theme font differs from the designer default.
- Sizes in custom-drawn controls are written at 100% scale and passed through `Px(...)`.
- `MainForm.Designer.cs` repeats three Theme values as literals: the `navRail.Size` width (`RailWidth`), `contentHost.Padding` (`PagePadding`, `PagePaddingTop`) and `contentHost.BackColor` (`Background`). Change them together.
- Animations go through `Controls/Transition.cs`. The globe is the exception: it has one clock of its own for all its motion.
- The chart's dashed trend line is only a visual guide. Left alone it is ordinary least squares. Give the chart `ReportedSlopePerDecade` and it draws the service's slope instead, placed through the middle of the series. Either way, the trend and significance numbers we report must come from the Python analysis service.
- Figures from the service are written out through `Controls/Numbers.cs` (`Signed`, `Plain`, `P`, `Count`, `Percent`), so a slope or a p-value reads the same on every page. It only formats.
- Every new control gets a row in the gallery.

### Controls

| Control | Use it for |
|---|---|
| `VigilButton` | Actions. `Variant` is `Primary` (one per screen), `Secondary` or `Ghost`. Optional `IconGlyph`. |
| `Card` | Grouping content on a surface. `Title` draws a small heading, and `TitleBounds` lets you put a badge on the same line. |
| `StatTile` | A headline number, a coloured change line (`Delta`, `Arrow`, `DeltaColour`) and an optional sparkline |
| `TimeSeriesChart` | One time series. `SetData(dates, values)`. Hover read-out, trend line toggle, reveal animation. `ReportedSlopePerDecade` draws the service's slope, and `SetChangePoint(...)` marks a change point with the level either side. |
| `NavRail` | The left-hand navigation in `MainForm`. `Collapsed` shrinks it to icons, and it resizes itself. |
| `PageHeader` | Title and subtitle at the top of a page |
| `ToggleSwitch` | On/off settings |
| `SegmentedControl` | One choice from a short row, such as Monthly / Seasonal / Annual. `SetItems`, `SelectedIndex`. |
| `Badge` | Small outlined status labels. `Tone` colours the text and outline, and `ShowDot` adds a status dot. Sizes itself to its text, and draws nothing when the text is empty. |
| `Divider` | Hairline separator. With `Label` it's a section heading inside a card. |
| `SearchBox` | Text search with an icon and placeholder |
| `SpinnerControl` | Loading wheel |
| `FindingsList` | The ranked list of findings. `SetFindings`, `SelectedFinding`, `SelectFinding`. Rows are as tall as their text. Only the rows near the view are measured and drawn, because a real scan has thousands: measuring every row up front froze the window for half a minute. Keep it that way. |
| `GlobeControl` | The 3D globe on the Map page. See "The globe" below. |
| `SatelliteCard` | The panel about a clicked satellite, in `Controls/Globe/`: picture, name, facts and a note. `SetContent(...)`, and it sets its own height. |
| `ChatView` | A whole chat with the Detective: scrolling messages above an input. Set `Conversation`, and call `AskAbout(finding)` to attach a finding to the next question. See "The Detective" below. |
| `ChatPanel` | `ChatView` in a floating panel with a title bar. `MainForm` owns the only one. |
| `ChatTranscript`, `ChatInput`, `ContextChip`, `ConversationList` | The parts of the chat, in `Controls/Chat/`. Use `ChatView` unless you need one alone. |
| `EvidenceView` | Everything the results say about one finding, in `Controls/Evidence/`: figures, series, score, re-test, method, sources, limitations and links. It sets its own height. See "Evidence" below. |
| `ScanSummaryView` | What a scan covered and found, variable by variable, with each variable's re-test, then the scan's settings. It sets its own height. |
| `StillPanel` | A scrolling panel that does not jump when something inside it takes the focus. Use it wherever a control that sets its own height needs scrolling. |
| `ZonePanel` | Everything about zones that sits beside the globe: the button that starts the pen, the saved zones and the chosen zone's trends. It only raises events. See "Zones" below. |
| `YearBar` | Steps through years: a play button, the chosen year and a track with a mark per year. `SetYears`, `SelectedIndex`, `Playing`. Sits over the globe on the Map page. |
| `PresentPanel` | The card shown while presenting: the address the audience opens, what following means, and a way to stop. `SetAddresses`, `SetProblem`. `MainForm` owns the only one. |
| `ZoneList`, `ZoneNameBar`, `ZoneSummaryView` | The parts of the zones, in `Controls/Zones/`: the saved zones, the bar that names a new outline, and one zone's trend per variable with a chart. Use `ZonePanel` unless you need one alone. |

`VigilControl` is the base class for custom-drawn controls. Inherit from it for new ones.

### The globe

`GlobeControl` lives in `Controls/Globe/` (namespace `VIGIL.Controls.Globe`) and is hosted by `Pages/MapPage.cs`. It's software-rendered with no extra packages: each pixel ray-casts a sphere textured from NASA Blue Marble (day, 8192x4096) and Black Marble (night lights) imagery, embedded from `assets/`, and lit from the real sun position. Both textures are public domain NASA imagery.

Mouse and keyboard: left-drag grabs and flings the Earth, the wheel zooms on the cursor, right-drag (or Ctrl+drag) tilts and turns, double-click flies in, and Shift+drag selects a box and raises `RegionSelected`. Arrow keys pan, `+`/`-` zoom, `N` resets north, `R` or `Home` resets the view, and `Escape` clears the selection. The globe opens on the whole planet centred on Plymouth, and `R` or `Home` returns there. On the Map page it opens still: the "Rotate" switch starts it turning by itself, which keeps a processor core busy for as long as it is on. Auto-rotation pauses for 4 seconds after any input.

Pen mode: while `PenMode` is on, left-drag draws an outline on the surface instead of turning the globe, and the globe does not rotate by itself. Right-drag, the wheel and the keys still move the view. Lifting the pen raises `ZoneDrawn` with the outline, which stays on the globe, dashed, until `ClearStroke()`. `Escape` removes it and switches the pen off.

Lighting follows the real sun from the current UTC time, refreshed every 30 seconds, and the read-out in the bottom-left says so ("Live sunlight, 17:12 UTC"). `SunTimeUtc` freezes it at an instant, and `UseRealSunPosition = false` uses `SunLatitude`/`SunLongitude` instead.

Moving the camera: `FlyTo(lat, lon, zoom)` flies anywhere along the great circle, pulling out on long hops. Overloads take a marker, a region, a `Place`, or a name such as `FlyTo("Amazon")` from the built-in `Places` list of 41 Earth-science regions. `FlyToComplete` fires on landing.

Flat map: `globe.Flat = true` unrolls the globe into a flat map, 180° W to 180° E with north up, and `false` rolls it back up, animated on the globe's own clock. `Fold` reads how far it has got, from 0 to 1, and `FlatChanged` fires when `Flat` changes. The geometry is the web viewer's (`web/src/globe/stage.ts`): at fold f the surface is part of a sphere of radius 1 / (1 − f), with every latitude and longitude scaled by (1 − f), touching the place at 0° N 0° E. `GlobeCamera` takes the fold and does the placing (`Place`, `Project`, `Unproject`), so whatever is drawn through it goes with the surface: the layer, the relief, markers, regions, zones, the pen and the box selection. The renderer fires its rays at that sphere, or at the map's plane, and lights each place as it stands on the real globe, so the flat map shows day and night. With the fold at 0 the renderer runs the loop it always did and draws the same picture, pixel for pixel. Satellites and ground tracks are drawn round the globe only and are left out while it is unrolled, the halo fades out over the first quarter of the fold, and auto-rotation stops. On the flat map a drag moves the map, the wheel zooms about the cursor, a right-drag leans the view over, north stays at the top, the view stops at 180° and can pull back to the whole map, flights go in a straight line, and everything off the surface stands twice as tall (`GlobeCamera.FlatHeightScale`). A line that crosses 180° is drawn in two pieces, one at each edge.

Adding data:

- `AddMarker(lat, lon, label, colour, detail)` for sites
- `AddRegion(latMin, latMax, lonMin, lonMax, colour, label)` for study areas
- `AddZone(new GlobeZone(outline, colour))` for an area of any shape, drawn as an outline with no fill. Set `Emphasised` and `CellEdges` to show the stepped edge of the grid cells it takes in. Clicking inside one raises `ZoneClicked`.
- `AddSatellite(new GlobeSatellite(label, inclination, altitudeKm, colour))` for a satellite on a circular orbit, placed from the real clock. The globe draws its orbit as a ring at the right altitude, hidden where the Earth is in the way, plus a dotted line down to the point beneath it. Set `AscendingNodeLocalTime` for a sun-synchronous orbit (Terra is 22.5), `TimeScale` to speed up motion along the orbit only, and `ShowGroundTrack` for the last half orbit of ground track.
- `SetGroundTrack(points, colour, label)` for a measured ground track drawn on the surface
- `SetGridOverlay(values, latMin, latMax, lonMin, lonMax, scale, min, max, opacity)` for gridded results. `values[row, column]` runs south to north from `latMin` and west to east from `lonMin`, with NaN for missing cells.

`ColourScaleLegend.Follow(globe)` keeps a legend in step with the overlay. Its `Note` is a line under the scale for what the scale does not say, such as what a column's height means.

Relief: `globe.Relief = true` draws the grid overlay as columns standing on the globe (`Controls/Globe/GlobeRelief.cs`). A column's height is the size of its cell's value against the colour scale's own range, and its colour is the layer's. It shows nothing the colour does not, and the legend must say what height means whenever it is on. Columns are drawn over the rendered frame, far ones first, on the side of the planet that faces the camera, and markers stand on top of them. `globe.TiltTo(degrees)` leans the view over so the heights can be seen. It costs about 7 ms a frame with the whole globe in view.

Performance: one timer drives all animation. Frames render at reduced resolution while moving and at full resolution when idle. Slow movement (auto-rotation, satellites, the pulsing sunlight dot) only redraws once something on screen has moved a quarter of a pixel, so nothing renders that would look the same. Each row of a frame only shades the span the globe and its halo cover, and copies the star field for the rest. It holds about 250 MB of memory once both textures and their mipmaps are loaded: the day texture keeps its water mask in the alpha byte rather than a separate array. Above roughly 4x zoom the 8K texture starts to soften. Set the environment variable `VIGIL_GLOBE_STATS=1` to show frame timings. The Map page shows the results document it is given: a layer per variable from `grids`, and a marker per cell that has a finding. When the app was started without the service, that document is the synthetic fixture, and the page says so. The satellites are described below.

### The satellites

Both front ends draw the same six satellites, from two lists that must be kept in step: `Controls/Globe/MissionSatellites.cs` and `web/src/globe/satellite.ts`.

- Which satellites are "in the scan" is worked out from the results on screen, not written into the lists. A satellite is in the scan when one of the scan's variables names it as its mission, and it is marked as re-testing when a second product does (`MissionSatellites.Create(scanMissions, retestMissions)`, `satellitesFor(...)`). With the fixture that is Terra and the GPM Core Observatory. With the eight-variable scan Aqua joins them.
- In the scan: the stronger colour (`Positive`). The rest: the quieter colour (`TextSecondary`), and their card and hover text say they are not in the scan.
- MERRA-2 and GLDAS are models, not single spacecraft, so they have no orbit to draw.

Each orbit's height, tilt and period are the mission's own, and a sun-synchronous plane sits where it really is today. The place along each orbit is not tracked from live orbit data. That caveat (`MissionSatellites.Caveat`, `satelliteCaveat`) is the tooltip of the "Satellites" switch in both apps, and it must stay wherever the satellites are explained. No result depends on the satellites.

Clicking a satellite opens a small card about it in both apps: its picture, whether its data is in the scan, what VIGIL uses it for, who runs it, when it was launched, what it carries and its orbit. On the desktop the globe raises `SatelliteClicked` and the facts travel in the satellite's `Tag` as a `MissionInfo`; on the web the card is `web/src/globe/SatelliteCard.tsx`. The pictures are public domain NASA images in `assets/satellites/`, with their credits in the README there. Add a satellite to both lists, with a picture and a credit, or to neither.

Each app has a "Satellites" switch in the toolbar, beside the other switches, to hide them.

### The Detective

The chat is the C# side of the AI Detective. It only ever calls `POST /ask` on the Python service, in the shape the contract defines. The app never calls a language model itself and never holds an API key.

- `Data/DetectiveClient` makes the call. The address is the environment variable `VIGIL_API_URL`, or `http://127.0.0.1:8000` when that isn't set.
- `Data/ChatService` holds the conversations and is shared by the floating panel and the Detective page, so both show the same chats. It saves them to `%LOCALAPPDATA%\VIGIL\chats.json`, outside the repo.
- When the service can't be reached, the four example questions from `contract/fixture/ask.json` get their canned answers, marked "Sample answer". Any other question gets a note that there is no answer. Never show a fixture answer without that mark.
- That is only while the fixture is on screen. Over a real scan the fixture's answers are about cells that do not exist, so none is given (`ChatService.AllowSamples`), and the questions on offer are ones a real scan can speak to (`ChatService.Suggestions`, set in `MainForm`). The web viewer does the same, from the same four questions: change them in both.
- Only a finding can be attached to a question, as the contract's `finding_id`. Don't put other text or numbers into the question for the user: the service rejects answers that use a number no finding backs.
- An answer may carry `steps`: the lookups the Detective made on the way, recorded by the service. The chat lists them under the answer ("It looked 2 things up"), and clicking one shows on the map the box it searched and the findings it read. A step's findings were read, not relied on: only the citations back the answer, and the chat says so under the list. Keep that line.
- When an answer arrives while the Map page is on screen, the findings it cites are outlined and the globe goes to the first (`ChatService.Answered`, `MapPage.ShowCited`). On any other page nothing moves.
- The Map page's markers are the fixture's findings until the service returns real ones. Clicking one opens the panel with that finding attached.

### Overview and Evidence

The Overview page (`Pages/OverviewPage.cs`) is where the app opens. It shows the run record's headline counts, each variable with its source, its counts and its re-test (`ScanSummaryView`), and the top of the ranking. Everything on it is read from the results document: the only arithmetic is counting findings and the share one count is of another.

The Evidence page (`Pages/EvidencePage.cs`) lists the findings on the left and shows the chosen one in an `EvidenceView`. It is the screen the brief's demo walks through.

- A stored scan carries series for the top of the ranking only. For any other finding the page fetches them from the service (`GET /findings/{id}`). When it cannot, the view says so where the chart would be.
- "Recompute" asks the service to run the finding's own cell again from the gridded data (`POST /analyse`) and sets the fresh slope, interval and p-value beside the stored ones. The app compares the two and works out neither. The adjusted p-value is not compared, because a one-cell run has nothing to correct for.
- A finding's `standing` decides its badge. "Re-tested" is shown only for a confirmed re-test, and never without the product it was re-tested on. A re-test that came out the other way is a caution, in the warning colour. See "standing and re-testing" in the contract.
- A p-value the analysis stored as exactly 0 is shown as "below 1e-16": it was too small for the arithmetic, not zero.
- Every limitation the service wrote is shown. Do not trim the list to make the page shorter.
- Under the sources is a link that opens the finding's cell in NASA Worldview (`Data/Worldview.cs`), for looking at the place itself in satellite imagery. The page says it is no part of the evidence. The address format is Worldview's documented one: change it only against their documentation.

### Relief and the years

Two switches on the Map page's toolbar change how the layer is drawn. Neither changes a result.

A third, "Flat map", changes only how the globe is looked at: it sets `globe.Flat`. While it is on, the "Satellites" and "Rotate" switches are disabled, and the "Flat map" switch's own tooltip says why: a disabled switch shows no tooltip. Home goes to the whole map, and a flight or a tilt asked for while the globe is folding takes effect when the fold ends.

- "Relief" raises each cell by the size of its trend, and leans the view over the first time so the heights read. The legend gains the line "Height: the size of the trend, either way".
- "Years" replaces the trend with one year at a time: each scanned cell's mean anomaly in that year, against the cell's own normal, from `GET /years` on the Python service (`ResultsClient.YearsAsync`, `Models/YearsField`). A `YearBar` over the globe steps or plays through them. One colour scale serves every year, and it comes from the service, so a colour means the same in 2003 as in 2023.
- The years are for looking at how a change arrived. No test is run on them, nothing in the findings rests on them, and the app never calls a year's value a finding or attaches it to a question for the Detective.
- "Significant only" is switched off while the years are on, because a year's anomaly has no significance to filter by. A chosen zone still limits the cells.
- When the service cannot answer, or holds a different scan, the switch turns itself off and the status badge says why. The trend stays on the globe.

### The service's heartbeat

A badge beside the Present button says whether the analysis service is there. `MainForm` asks `GET /health` every five seconds (`Data/ServiceHealthClient`, `Models/ServiceHealth`), one call at a time, with a three-second limit so a service that has stopped is noticed quickly.

- "Service connected": it answers, holds the scan on screen and has the Detective's key.
- "Service connected, no Detective": it has no key, so questions will not be answered.
- "Service has no gridded data": zones, Recompute and the years cannot be worked out.
- "Service holds another scan": its run is not the one on screen. This is what shows when the app was opened before the service was started.
- "Service not running": nothing answered.

The badge's tooltip says what to do about each. The badge only reports. Every page still asks the service for itself and says so where it cannot, so nothing may rely on the badge to switch a feature on or off. It is hidden when the window is too narrow for it to sit beside the page's title.

`python\start-service.cmd` starts the service on the scan in `data\scan` and starts it again if it stops. See "Starting it with one command" in `python/vigil/api/README.md`.

The web viewer has no such badge. It asks its own address for the service once and a static site is asked nothing more, which is the rule under "The web viewer" below.

### Presenting

The Present button, beside "Ask the Detective", lets other people's screens follow the presenter's. It needs the Python service.

- The app tells the service what is on show (`Data/PresenterClient`, `POST /present`): the finding chosen on the Map or Evidence page, the layer's variable and the chosen zone. The service takes this only from the machine it runs on. A web viewer opened from that service follows. See "Presenting" in the contract.
- What is on show is sent one call at a time and always ends with the latest, so quick clicks down a list do not reach the audience out of order.
- The card (`PresentPanel`) gives the address to open: this machine's network address with the service's port. Other devices can only reach it when the service was started with `--host 0.0.0.0`. The service says whether it was (`open_to_network`), and when it was not the card says what to do in place of an address that would not answer.
- Presenting stops when the presenter says, when the app closes, or when the service stops answering. The card then says why.
- Nothing about presenting changes a result, and a viewer can always stop following.

### Zones

A zone is an area the user draws on the globe with the pen, names and saves. The Zones tab beside the findings on the Map page holds the pen button and the saved zones.

- Zones are saved to `%LOCALAPPDATA%\VIGIL\zones.json`, outside the repo, by `Data/ZoneService`. A zone is only an outline and a name. It belongs to no scan.
- A zone is really a set of grid cells: those whose centres its outline holds. `Data/ZoneGeometry` works that out, by the rule in the contract under "A zone", which the Python service and the web viewer (`web/src/zones/geometry.ts`) follow too. Change the rule in all three or in none. An outline that holds no cell centre, or that goes round a pole, is not saved, and the page says why.
- Choosing a zone limits the findings list, the markers and the layer to its cells. The chosen zone is drawn in the accent with the stepped edge of its cells, so the area the numbers cover is always on screen beside the outline that was drawn. The layer's colour scale still comes from every cell, so it does not shift when a zone is chosen.
- A zone's own series and trends come from `POST /zone` on the Python service, through `Data/ResultsClient`. The app works none of it out. `ZoneSummaryView` shows what the service sent, with every limitation it sent.
- The service can put a `caution` on a zone's result: a few words such as "Snow is rare here". The row shows it in the warning colour where the interval would be, and a cautioned result is never drawn with the weight of a significant one. The app does not decide what deserves a caution.
- When the service cannot answer, or its `run_id` is not the run on screen, show the reason and no figures. A zone's figures beside the wrong scan would be wrong without looking wrong.
- A zone's p-value is not corrected for many tests and the zone was chosen by eye. The service says so in `limitations`. Never label a zone result "significant after correction", and never attach a zone's numbers to a question for the Detective.
- The web viewer has zones too (`web/src/zones/`). There an outline is laid by clicking points on the globe, and saved zones are kept in the browser, not in `zones.json`. The cells follow the same rule, and the same refusals apply: no figures without the service, for another run, or when the service's cells differ.

### Adding a page

1. Create a `UserControl` in `Pages/`, with a `PageHeader` docked to the top.
2. Register it in the `MainForm` constructor with `AddPage(key, glyph, title, factory)`. Pages are created the first time they're opened and kept after that.
3. Replace the matching `PlaceholderPage` entry if there is one.

Real pages should use the designer (a `.Designer.cs` file). `GalleryPage`, `PlaceholderPage`, `MapPage`, `OverviewPage` and `EvidencePage` are built in code for now: the first two are a showcase and a stand-in, `MapPage` is mostly one custom control plus a toolbar, and the last two are each a list beside a control that draws itself. Move `MapPage` to the designer if its layout grows. These three carry `[DesignerCategory("Code")]` so Visual Studio opens them as code. The designer has nothing to load for them, and opening one there can end in a "Value cannot be null" error. Remove the attribute when you give a page a `.Designer.cs`.

## The web viewer

`web/` is a second, smaller front end: a static React site that shows the same results document in a browser, so VIGIL can be seen from a link as well as run in person. Dan owns it.

It began as a viewer of one screen and now carries most of what the desktop app shows: the globe with a variable's trend layer, flat or in relief, and the same variable year by year; the ranked findings list with its kind filter; the evidence view a finding opens into; the Detective chat; an "About this scan" panel with the run's counts, the variables' sources and the re-test table; and the findings as CSV and GeoJSON. Zones are in it too: drawn by clicking points on the globe, kept in the browser, and analysed by the service. `web/README.md` has a table of what each front end has and which parts need the service. Keep that table true: when a feature lands in one front end, either port it or add its row.

- Run it with `cd web`, `npm install`, `npm run dev`. `npm run build` type-checks and writes the site to `web/dist/`. You need Node 22.
- It loads `snapshot.json` and `ask.json` from beside `index.html` when the page opens, and falls back to `contract/fixture/` when they are not there. Two more kinds of file sit beside the page and are fetched only when wanted: `series/<variable>/<cell_id>.json` for a finding's chart, and `years/<variable>.json` for the years. `python -m vigil.api.export` writes all of them (see "A website with no service" in `python/vigil/api/README.md`). To show real results, put them on the server or the bucket: no rebuild is needed, and a deploy never overwrites them. A reply that is not JSON counts as a missing file, and a file that names another run is refused. For local development `scripts/copy-fixture.mjs` copies the fixture into `web/public/`, and those copies are not committed.
- A built copy of the viewer is committed at the root of the repository: `index.html` and `viewer/`. Any plain web server pointed at a checkout serves it, with no Node and no build on that machine. It goes stale when `web/src/` changes, so run `npm run build:root` and commit the result with every change to the viewer. Never edit those two by hand.
- `src/contract.ts` is the contract as TypeScript types, with the JSON's own snake_case names. It only describes the format. Change it when Dan changes the contract, and never to suit the viewer.
- `src/theme.css` holds the colours, spacing and type sizes as CSS variables, copied from `Theme.cs`. The UI rules above apply here too: tokens only, surfaces told apart by tone, status colours for meaning, no decoration, sentence case, UK English.
- Each part keeps to its own folder under `src/` (`findings/`, `evidence/`, `globe/`, `detective/`) with its own stylesheet and a class-name prefix to match. `src/App.tsx` is the shell that holds the shared state (the chosen finding, the layer, whether the chat is open) and passes it down.
- The globe marks the cells of the top 200 findings. A label is drawn only where it does not sit on one already placed: the chosen marker's first, then in rank order, as the desktop does it. A label that would run off the right-hand edge goes to the left of its dot, and labels are drawn above every dot. The rest are dots that name their rank under the pointer. So few labels show on the whole globe and more appear as you zoom in.
- The globe draws the satellites behind the data on their orbits (`src/globe/satellite.ts`, a port of `GlobeSatellite.cs`), from the same list as the desktop app. See "The satellites" under "The globe" above for which they are and what must be said about them. The "Satellites" switch in the toolbar hides them.
- The "About this scan" panel (`src/scan/`) never prints a share of re-tests confirmed without the base-rate line beside it: how often the second product is significant where the scan found nothing.
- The Detective chat lists an answer's `steps` under it and can show each on the globe, with a note that they were read, not relied on. An answer's findings are matched to the list and the globe only when its `run_id` is the run on screen.
- The evidence view (`src/evidence/`) shows a finding's series, its numbers with their uncertainty, its score, its sources, preprocessing, test and limitations, all read from the results document. A re-tested finding shows the product it was re-tested on directly under its badge. The chart's dashed line uses the slope the service reported and is placed on the series only as a guide for the eye, and the view says so. A series comes from the snapshot, then from its file beside the page, then from the service (`src/evidence/series.ts`). Only when all three have nothing does the view say the series is not in these results. Under the sources is the same link to the cell in NASA Worldview as on the desktop (`src/evidence/worldview.ts`), with the same words that it is no part of the evidence. "Copy as text" writes what the desktop's does.
- The same honesty rules as the app: made-up results carry the "Synthetic fixture" badge, a fixture answer from the Detective is marked "Sample answer" and is only given while the fixture is on screen, and the viewer never calls a language model or holds a key. The Detective's address is the build-time variable `VITE_VIGIL_API_URL`. Without it, the viewer uses `http://127.0.0.1:8000` when the page is opened on localhost, and otherwise the address the page itself came from, so one server hosting both the page and the service needs no configuration.
- All statistics still come from the Python service. The viewer displays what the document says and computes nothing but layout, the colour scale's range, a column's height in relief, and a cell's centre in the exports.
- "Tilt" and "Flat map" change how the globe is looked at and nothing else: the view leaned over, and the globe unrolled into a flat map, both animated. They are built in `src/globe/stage.ts`, because react-globe.gl has neither. Ask the stage, not the library, for a place's position (`coords`), its spot on screen (`screen`), the place under the pointer (`pick`) and a flight (`flyTo`), and add meshes to `stage.group`, not to the scene. Anything that asks the library instead stays behind when the globe unrolls, and does not open with it. The satellites are drawn round the globe only, so they are hidden on the flat map and their switch is disabled, with the caveat still in its tooltip. `web/README.md` has how it works under "Tilt and the flat map". The desktop globe has the same flat map (`GlobeControl.Flat`), unrolled by the same rule.
- "Relief" and "Years" are the desktop's (see "Relief and the years" above) and follow the same rules: the legend says what height means, one colour scale serves every year and comes from the data's `scale_max`, "Significant only" is off while the years are on, and a year is never shown as a finding. The columns are one merged mesh (`src/globe/relief.ts`), at most 0.06 planet radii tall so they stay under the lowest satellite's ring, and 2.5 times that on the flat map, where no orbit is drawn. The flat layer, the relief and the years all read one description of the layer (`src/globe/field.ts`).
- The findings list draws only the rows near the view (`src/findings/FindingsList.tsx`): a real scan has thousands. Keep it that way, and keep the arrow keys and the chosen row coming into view working.
- The viewer has the desktop app's four pages, Overview, Map, Evidence and Detective, and opens on the Overview as the app does (`src/App.tsx`, `src/overview/`). On a wide screen they are a row in the header. Below 900 px one fills the screen at a time, chosen from a menu along the bottom. A page that is away is moved off the side of the screen and marked `inert`, never removed or hidden: the globe would lose its size, and its markers set their own visibility. The globe is made the first time the Map is opened. `web/README.md` has the rest under "Pages" and "On a phone".
- The exports (`src/scan/export.ts`) write the contract's columns in the contract's order ("Findings as a file for other tools"), the same as `Data/FindingsExport.cs`. Change both or neither. `docs/submission/checks/check_export.py` checks a pair of files against the results document.
- Which service: the page asks its own address for `GET /health` once (`src/service.ts`). If that answers, the page was served by the service and uses it for everything. Otherwise the rule is the build-time variable `VITE_VIGIL_API_URL`, then `http://127.0.0.1:8000` on localhost, then the page's own address. A static site answers that one request with a 404 and is asked nothing more.
- Following a presenter (`src/present.ts`, "Presenting" in the contract): one `GET /present` when the page opens, and no more unless something answered. While someone presents the same run, a "Following the presenter" switch is shown, on, and each new thing on show opens on the page. A different `sequence` is new, whether higher or lower, because a restarted service counts from the start again. The person can switch following off, and nothing is applied while it is off.
- Packages so far: `react`, `react-dom`, `react-globe.gl` and `three`. Ask before adding one.
- `.github/workflows/deploy.yaml` builds the viewer and the desktop app on every push to `main` and puts both on the S3 bucket: the viewer at the root, and the app as `download/VIGIL-win-x64.zip`, which the viewer links to. The bucket is the public website, so only the site goes on it: the viewer, the fixture it falls back on, and the app download. The repository itself is never uploaded. The deploy deletes nothing outside `viewer/`, so a real `snapshot.json` and anything else another job puts in the bucket survive it. It builds the viewer with `VITE_VIGIL_API_URL` set to the `vigil-api` Lambda, which is the service behind the public site and holds the Detective's key: see "On AWS Lambda" in `python/vigil/api/README.md`.

Before you finish a change in `web/`: `npm run build:root` must pass, and open the page at phone width as well as desktop width.

## Windows Forms rules

- Layout lives in `*.Designer.cs` and is edited through the Visual Studio designer. If you hand-edit a designer file, keep to the style the designer writes (`this.` prefixes, fully qualified type names, everything inside `InitializeComponent`) so the designer can still load it.
- Behaviour lives in the matching `*.cs` file. Don't put logic in designer files.
- Anything the designer can't express goes in the constructor after `InitializeComponent()`. Loading the logo is one example.
- Long-running work stays off the UI thread. Use `async`/`await` and `Task.Run`, and update controls only from the UI thread.
- New forms need a `<Compile Update="...">` entry with `<SubType>Form</SubType>` in `VIGIL.csproj.user` so Visual Studio opens them in the designer.

## Assets

Images live in `assets/` at the repo root, not inside the project. They are embedded into the exe from `VIGIL.csproj`:

```xml
<EmbeddedResource Include="..\..\assets\UoPHackathonLogo.jpg" LogicalName="VIGIL.Assets.UoPHackathonLogo.jpg" Link="Assets\UoPHackathonLogo.jpg" />
```

Load them with `GetManifestResourceStream` using the `LogicalName`. Follow the same pattern for new assets so the app never depends on files sitting next to the exe.

The logo is a JPG with an off-white background. `LoaderForm.WhitenBackground` pushes that background to pure white so it doesn't show as a box on white forms. If we get a PNG with transparency, that function can go.

## Code style

Match what's already there:

- `using` directives at the top, then a block-scoped `namespace VIGIL { ... }` (not file-scoped)
- PascalCase for types, methods and properties. camelCase for fields, with no underscore prefix.
- `/// <summary>` on classes, and on methods whose purpose isn't obvious from the name. Inline comments only where the reason isn't clear from the code.
- `using var` for disposables
- Event handlers named `ControlName_Event`, for example `TaglineTimer_Tick`

## Before you finish a change

1. `dotnet build` from `VIGIL/` must pass with no errors and no new warnings.
2. Run the app and check the loader still hands over to the main window. If you touched a control, check it on the Components page too.
3. Search your diff for American spellings in names, comments and UI text.

## Open questions

Answered so far:

- Datasets: MERRA-2 (temperature), GPM IMERG (precipitation), GLDAS (soil moisture) and MODIS (vegetation). See sections 05 and 06 of `Project_Brief.pdf`.
- Methods: Mann-Kendall with Sen's slope on deseasonalised monthly anomalies, with an autocorrelation correction and false-discovery-rate control. This is the plan in section 02 of the brief, to be checked with a mentor on the Saturday.
- Analysis runs in Python, not C#.
- Who owns which work area: see the table in `COORDINATION.md`.

Still open:

- How the main window should be laid out.
- The grid cell size and common date range for the scan.
- Whether agents commit and push, or leave that to their person. See `COORDINATION.md`.

Update this section as these get answered.
