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
- A Python analysis service (FastAPI) under `python/`, which the app calls over HTTP on localhost. All data handling and statistics live there, not in C#. The app talks to it with `HttpClient` and `System.Text.Json`, which need no packages.
- The JSON format the two halves exchange is the result contract in `contract/`. It is read-only for everyone except Dan.

## Language: UK English

Use UK spelling everywhere we control: identifiers, comments, UI text, docs and commit messages. That means colour, initialise, visualise, analyse, centre, behaviour, catalogue, licence (noun).

The exception is names that come from .NET or a library, which we can't rename: `Color`, `ForeColor`, `BackColor`, `InitializeComponent()`, `ApplicationConfiguration.Initialize()`, `CenterScreen`, `DimGray` and so on. Wrap them as they are. Don't create aliases just to change the spelling.

Example from `LoaderForm.cs`: the field is `taglineColour` even though its type is `Color`.

## How startup works

1. `Program.Main` creates `LoaderForm` and calls `ShowDialog()`.
2. `LoaderForm.OnShown` starts the tagline rotation and awaits `InitialiseAppAsync()`, plus a minimum display time (`MinimumDisplayTime`, currently 6 seconds).
3. On success the loader sets `DialogResult.OK` and closes. `Program` then runs `MainForm`, which is the app shell: a nav rail on the left and the selected page on the right.
4. If `InitialiseAppAsync()` throws, the loader shows an error box and returns `DialogResult.Abort`, and the app exits.

Anything the app needs before the main window opens (config, dataset catalogue, API checks, caches) goes in `InitialiseAppAsync()`. It runs on a background thread through `Task.Run`, so it must not touch controls directly. Hand results to `MainForm` through its constructor or a shared service rather than through static state.

## Where code goes

Everything lives under `VIGIL/VIGIL/`, in folders with matching namespaces (`VIGIL.Controls`, `VIGIL.Pages` and so on). `Program.cs` is the only file at the top level.

| Folder | What goes there |
|---|---|
| `Theming/` | Colours, fonts, icon glyphs, drawing helpers, dark window chrome. No controls. |
| `Controls/` | Reusable custom-drawn controls (buttons, cards, chart, nav rail and so on) |
| `Forms/` | Windows (`Form` subclasses) and their `.Designer.cs` and `.resx` files |
| `Pages/` | Screens shown inside `MainForm`, one `UserControl` per nav item |
| `Data/` | The client for the Python service: calls the API and turns its JSON into `Models/` types. No dataset parsing and no statistics. Also the saved chats (`ChatService`) and the embedded fixture (`Fixture`). |
| `Models/` | Plain data types that mirror the result contract: the Detective's question and answer, a finding's summary, a saved conversation |

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
- The chart's dashed trend line is only a visual guide (ordinary least squares). The trend and significance numbers we report must come from the Python analysis service.
- Every new control gets a row in the gallery.

### Controls

| Control | Use it for |
|---|---|
| `VigilButton` | Actions. `Variant` is `Primary` (one per screen), `Secondary` or `Ghost`. Optional `IconGlyph`. |
| `Card` | Grouping content on a surface. `Title` draws a small heading, and `TitleBounds` lets you put a badge on the same line. |
| `StatTile` | A headline number, a coloured change line (`Delta`, `Arrow`, `DeltaColour`) and an optional sparkline |
| `TimeSeriesChart` | One time series. `SetData(dates, values)`. Hover read-out, trend line toggle, reveal animation. |
| `NavRail` | The left-hand navigation in `MainForm`. `Collapsed` shrinks it to icons, and it resizes itself. |
| `PageHeader` | Title and subtitle at the top of a page |
| `ToggleSwitch` | On/off settings |
| `SegmentedControl` | One choice from a short row, such as Monthly / Seasonal / Annual. `SetItems`, `SelectedIndex`. |
| `Badge` | Small outlined status labels. `Tone` colours the text and outline, and `ShowDot` adds a status dot. Sizes itself to its text, and draws nothing when the text is empty. |
| `Divider` | Hairline separator. With `Label` it's a section heading inside a card. |
| `SearchBox` | Text search with an icon and placeholder |
| `SpinnerControl` | Loading wheel |
| `GlobeControl` | The 3D globe on the Map page. See "The globe" below. |
| `ChatView` | A whole chat with the Detective: scrolling messages above an input. Set `Conversation`, and call `AskAbout(finding)` to attach a finding to the next question. See "The Detective" below. |
| `ChatPanel` | `ChatView` in a floating panel with a title bar. `MainForm` owns the only one. |
| `ChatTranscript`, `ChatInput`, `ContextChip`, `ConversationList` | The parts of the chat, in `Controls/Chat/`. Use `ChatView` unless you need one alone. |

`VigilControl` is the base class for custom-drawn controls. Inherit from it for new ones.

### The globe

`GlobeControl` lives in `Controls/Globe/` (namespace `VIGIL.Controls.Globe`) and is hosted by `Pages/MapPage.cs`. It's software-rendered with no extra packages: each pixel ray-casts a sphere textured from NASA Blue Marble (day, 8192x4096) and Black Marble (night lights) imagery, embedded from `assets/`, and lit from the real sun position. Both textures are public domain NASA imagery.

Mouse and keyboard: left-drag grabs and flings the Earth, the wheel zooms on the cursor, right-drag (or Ctrl+drag) tilts and turns, double-click flies in, and Shift+drag selects a box and raises `RegionSelected`. Arrow keys pan, `+`/`-` zoom, `N` resets north, `R` or `Home` resets the view, and `Escape` clears the selection. Auto-rotation pauses for 4 seconds after any input.

Lighting follows the real sun from the current UTC time, refreshed every 30 seconds, and the read-out in the bottom-left says so ("Live sunlight, 17:12 UTC"). `SunTimeUtc` freezes it at an instant, and `UseRealSunPosition = false` uses `SunLatitude`/`SunLongitude` instead.

Moving the camera: `FlyTo(lat, lon, zoom)` flies anywhere along the great circle, pulling out on long hops. Overloads take a marker, a region, a `Place`, or a name such as `FlyTo("Amazon")` from the built-in `Places` list of 41 Earth-science regions. `FlyToComplete` fires on landing.

Adding data:

- `AddMarker(lat, lon, label, colour, detail)` for sites
- `AddRegion(latMin, latMax, lonMin, lonMax, colour, label)` for study areas
- `AddSatellite(new GlobeSatellite(label, inclination, altitudeKm, colour))` for a satellite on a circular orbit, placed from the real clock. The globe draws its orbit as a ring at the right altitude, hidden where the Earth is in the way, plus a dotted line down to the point beneath it. Set `AscendingNodeLocalTime` for a sun-synchronous orbit (Terra is 22.5), `TimeScale` to speed up motion along the orbit only, and `ShowGroundTrack` for the last half orbit of ground track.
- `SetGroundTrack(points, colour, label)` for a measured ground track drawn on the surface
- `SetGridOverlay(values, latMin, latMax, lonMin, lonMax, scale, min, max, opacity)` for gridded results. `values[row, column]` runs south to north from `latMin` and west to east from `lonMin`, with NaN for missing cells.

`ColourScaleLegend.Follow(globe)` keeps a legend in step with the overlay.

Performance: one timer drives all animation. Frames render at reduced resolution while moving and at full resolution when idle. Slow movement (auto-rotation, satellites, the pulsing sunlight dot) only redraws once something on screen has moved a quarter of a pixel, so nothing renders that would look the same. Each row of a frame only shades the span the globe and its halo cover, and copies the star field for the rest. It holds about 250 MB of memory once both textures and their mipmaps are loaded: the day texture keeps its water mask in the alpha byte rather than a separate array. Above roughly 4x zoom the 8K texture starts to soften. Set the environment variable `VIGIL_GLOBE_STATS=1` to show frame timings. Everything on the Map page is demo data until real datasets are wired in.

### The Detective

The chat is the C# side of the AI Detective. It only ever calls `POST /ask` on the Python service, in the shape the contract defines. The app never calls a language model itself and never holds an API key.

- `Data/DetectiveClient` makes the call. The address is the environment variable `VIGIL_API_URL`, or `http://127.0.0.1:8000` when that isn't set.
- `Data/ChatService` holds the conversations and is shared by the floating panel and the Detective page, so both show the same chats. It saves them to `%LOCALAPPDATA%\VIGIL\chats.json`, outside the repo.
- When the service can't be reached, the four example questions from `contract/fixture/ask.json` get their canned answers, marked "Sample answer". Any other question gets a note that there is no answer. Never show a fixture answer without that mark.
- Only a finding can be attached to a question, as the contract's `finding_id`. Don't put other text or numbers into the question for the user: the service rejects answers that use a number no finding backs.
- The Map page's markers are the fixture's findings until the service returns real ones. Clicking one opens the panel with that finding attached.

### Adding a page

1. Create a `UserControl` in `Pages/`, with a `PageHeader` docked to the top.
2. Register it in the `MainForm` constructor with `AddPage(key, glyph, title, factory)`. Pages are created the first time they're opened and kept after that.
3. Replace the matching `PlaceholderPage` entry if there is one.

Real pages should use the designer (a `.Designer.cs` file). `GalleryPage`, `PlaceholderPage` and `MapPage` are built in code for now: the first two are a showcase and a stand-in, and `MapPage` is mostly one custom control plus a toolbar. Move `MapPage` to the designer if its layout grows. These three carry `[DesignerCategory("Code")]` so Visual Studio opens them as code. The designer has nothing to load for them, and opening one there can end in a "Value cannot be null" error. Remove the attribute when you give a page a `.Designer.cs`.

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
