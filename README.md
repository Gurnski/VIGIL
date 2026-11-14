# VIGIL

VIGIL (Visualising & Identifying Global Indicators of Long-term change) is the University of Plymouth Hackathon Team's entry for the 2026 NASA Space Apps Challenge, under the challenge [Be An Earth System Trend Detective!](https://www.spaceappschallenge.org/2026/challenges/earth-system-trend-detective/).

## The challenge

The brief asks for a tool that lets someone pick variables measured by Earth science missions or produced by Earth science models, see how they change over time, and work out:

- what is changing
- where it is changing
- how much it is changing
- whether the change is statistically significant

A variable can trend one way in one region and the opposite way in another, even when one process drives both, so the "where" matters as much as the "what".

## What VIGIL does

VIGIL scans the whole planet for change without being told where to look. It divides the land into grid squares, tests every square the same way, and ranks what it finds. A desktop app shows the results on a globe, and each finding opens into its evidence: the series, the trend and its uncertainty, and where every number came from.

The first scan uses four NASA products: MERRA-2 for temperature, GPM IMERG for rainfall, GLDAS for soil moisture and MODIS for vegetation. `docs/brief/Project_Brief.pdf` has the full plan, including every data source and how to retrieve it.

## Status

The main build happens over the hackathon weekend, 14 and 15 November 2026.

Before the event we built the desktop app's shell: the project structure, the splash screen, the theme, a set of interface controls and an interactive 3D globe. We also wrote the planning documents in this repository. At that point the app holds no Earth-observation data and runs no analysis, and everything it shows is demo data.

The data pipeline, the analysis, the AI assistant and the screens that present their results are built during the event.

## Running it

You need Windows and the .NET 8 SDK (Visual Studio 2022 works too).

```
cd VIGIL
dotnet build
dotnet run --project VIGIL
```

Or open `VIGIL/VIGIL.sln` in Visual Studio and press F5.

On launch you get a splash screen with the team logo, the VIGIL name, a spinner and rotating taglines. After startup work finishes (and at least 6 seconds have passed) it closes and the main window opens on the Components page, a gallery of the UI controls with demo data. The Map page has an interactive 3D globe built from NASA Blue Marble and Black Marble imagery, also showing demo data for now. Overview, Variables and Trends are placeholders until the hackathon.

The Python analysis service does not exist yet. Instructions for running it will go here once it does.

## Layout

```
├── README.md            this file
├── AGENTS.md            how to write code here, for people and coding agents
├── COORDINATION.md      who works in which part of the repository
├── CLAUDE.md            points Claude Code at the two files above
├── contract/            the result format and the fixture file
├── python/vigil/        the analysis service: pipeline, api, analysis, detective
├── infra/               cloud setup and the command that reruns the scan
├── docs/brief/          the plan for the weekend: Project_Brief.pdf and the HTML it is printed from
├── docs/submission/     demo script, presentation, written project page
├── assets/
│   ├── UoPHackathonLogo.jpg
│   ├── BlueMarbleDay.jpg       NASA Blue Marble, 8192x4096 (public domain)
│   └── BlackMarbleNight.jpg    NASA Black Marble city lights (public domain)
└── VIGIL/
    ├── VIGIL.sln
    └── VIGIL/           the WinForms project (.NET 8)
        ├── Program.cs       entry point: loader first, then main window
        ├── Theming/         colours, fonts, icons, drawing helpers, dark title bar
        ├── Controls/        custom-drawn controls: buttons, cards, chart, nav rail...
        │   └── Globe/       the 3D globe and its overlays, camera and place list
        ├── Forms/           LoaderForm (splash), MainForm (app shell), VigilForm (base)
        ├── Pages/           screens shown inside MainForm: gallery, map, placeholders
        └── VIGIL.csproj
```

`contract/`, `python/`, `infra/` and `docs/submission/` hold only a README each for now. Their contents are built during the event.

## Working on it

Read `AGENTS.md` and `COORDINATION.md` before changing anything. The first covers how the code is written. The second covers which parts are yours to edit, so that several people and their coding agents can work at once without overwriting each other.
