# web

The web viewer: a static React site that shows VIGIL's results in a browser. It has the globe with a variable's trend layer, the ranked findings list, the evidence view each finding opens into, and the Detective chat. It reads the same result contract as the desktop app (`contract/`).

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
| `src/App.tsx`, `src/app.css` | The shell: header, toolbar, layout and the shared state |
| `src/contract.ts` | The result contract as TypeScript types |
| `src/data.ts` | Loads `snapshot.json` and `ask.json` from beside `index.html` |
| `src/theme.css` | Colours, spacing and type sizes, copied from the app's `Theme.cs` |
| `src/format.ts` | Labels and place names shared by the parts |
| `src/findings/` | The ranked findings list |
| `src/evidence/` | The evidence view a finding opens into: its series chart, numbers, score and sources |
| `src/globe/` | The 3D globe, its trend layer, markers, legend and the satellites on their orbits |
| `src/detective/` | The Detective chat and its call to `POST /ask` |
| `scripts/copy-fixture.mjs` | Copies the fixture from `contract/fixture/` into `public/` before a dev run or build |

## Results

Until real results exist the viewer shows the synthetic fixture and says so. To show a real scan, put its `snapshot.json` beside `index.html` on the server or in the bucket. Nothing needs rebuilding, and a deploy does not overwrite it. Without one the viewer reads `contract/fixture/snapshot.json`.

The Detective needs the Python service. If the same server hosts the page and the service (`POST /ask`), nothing needs setting. If the service is somewhere else, set `VITE_VIGIL_API_URL` to its address when building, and the service must allow the page's address through CORS. Without a service it gives the fixture's sample answers, marked as such.
