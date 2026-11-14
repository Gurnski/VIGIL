# web

The web viewer: a static React site that shows VIGIL's results in a browser. It has the globe with a variable's trend layer, the ranked findings list and the Detective chat. It reads the same result contract as the desktop app (`contract/`).

Dan owns this folder. See `COORDINATION.md`, and "The web viewer" in `AGENTS.md` for how the code is written.

## Running it

You need Node 22.

```
cd web
npm install
npm run dev
```

`npm run build` type-checks and writes the site to `dist/`. Pushing to `main` builds it and puts it on the S3 bucket (`.github/workflows/deploy.yaml`).

## Where things are

| Path | What it is |
|---|---|
| `src/App.tsx`, `src/app.css` | The shell: header, toolbar, layout and the shared state |
| `src/contract.ts` | The result contract as TypeScript types |
| `src/data.ts` | Loads `snapshot.json` and `ask.json` from beside `index.html` |
| `src/theme.css` | Colours, spacing and type sizes, copied from the app's `Theme.cs` |
| `src/format.ts` | Labels and place names shared by the parts |
| `src/findings/` | The ranked findings list |
| `src/globe/` | The 3D globe, its trend layer, markers and legend |
| `src/detective/` | The Detective chat and its call to `POST /ask` |
| `scripts/copy-fixture.mjs` | Copies the fixture from `contract/fixture/` into `public/` before a dev run or build |

## Results

Until real results exist the viewer shows the synthetic fixture and says so. To show a real scan, put its `snapshot.json` beside `index.html` in the bucket. Nothing needs rebuilding.

The Detective needs the Python service. Set `VITE_VIGIL_API_URL` to its address when building. Without a service it gives the fixture's sample answers, marked as such.
