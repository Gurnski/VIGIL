# Who works where

Several people are building VIGIL at once, each with an AI coding agent. Agents rewrite files freely, so two of them in the same file means a merge conflict, or one silently undoing the other. This file keeps everyone in separate parts of the repo.

Read it before every session. It applies to people and agents alike. `AGENTS.md` covers how to write the code; this file covers which code is yours to write.

Nothing enforces these rules. They work because every agent reads them and every person holds their agent to them.

## Before you edit anything

If you are an agent, do these in order:

1. Find your person in the table below. Their rows list the paths you may edit. If you don't know whose agent you are, ask.
2. Run `git pull --rebase` so you start from everyone's latest work.

## Work areas

Each area has one owner. The split matches section 08 of `docs/brief/Project_Brief.pdf`.

| Area | Owner | Paths | What it is |
|---|---|---|---|
| `contract` | Dan | `contract/` | The JSON format of a finding, and the fixture file of fake results |
| `data` | Ethan | `python/vigil/pipeline/`, `infra/` | The discovery run: the cloud machine and bucket, downloading and regridding NASA data onto the scan grid, and handing it over as the `.npz` file that `ScanData` in `python/vigil/api/document.py` describes |
| `api` | Dan | `python/vigil/api/` | The FastAPI service and the step before it: running the analysis on the gridded data, adding the descriptive text and the run record, and writing the results document |
| `analysis` | Will | `python/vigil/analysis/` | Trends, significance, change points, cascades, contradictions, ranking |
| `detective` | Will | `python/vigil/detective/` | The AI Detective: tool calls over the API, grounded answers |
| `interface` | Dan | `VIGIL/` | The whole C# project: pages, controls, API client, models. This includes the chat panel, which is the C# side of the Detective. |
| `web` | Dan | `web/`, `index.html`, `viewer/`, `.github/workflows/deploy.yaml` | The web viewer: a React site that shows the same results in a browser (globe layer, findings list, Detective chat), and the workflow that builds it and the desktop app and puts both on the S3 bucket |
| `submission` | Open | `docs/submission/`, `README.md` | Demo video script, presentation, written project page. Shared out on the day. |
| `project-docs` | Dan | `AGENTS.md`, `COORDINATION.md`, `CLAUDE.md`, `docs/brief/` | The rules and the plan |

The folders exist and each holds a short README saying what belongs there. They are otherwise empty until the event.

Tests live beside the code they test and belong to the same owner.

If an area changes hands, Dan updates the table and tells everyone.

## Staying in your area

- Edit only files under your person's paths.
- Read anything you like. Reading never conflicts.
- If a task needs a change outside your paths, stop. Tell your person what change is needed, in which file, and why. They ask the owner.
- Don't work around a missing change by copying someone else's code into your area, or by writing a second version of it.
- Before you finish, run `git status`. Every changed file must sit under your paths. If one doesn't, and you changed it this session, undo that change and report it.

## The contract

`contract/` is read-only for everyone except Dan. Everything depends on it: the Python side writes findings in that format, and the C# app and the web viewer both read them. The viewer's copy of the format is `web/src/contract.ts`, and it changes only when the contract does.

- Never rename, remove or retype a field, on either side, to make your own code simpler.
- If the format is missing something you need, stop and ask Dan. He changes the contract and the fixture together, then tells everyone.
- Build against the fixture until real results exist.

## Files that several areas touch

Folder ownership doesn't cover these. Each has its own rule.

| File | Rule |
|---|---|
| `python/requirements.txt` | Add your own line and nothing else. Keep one package per line, in alphabetical order. Don't change a version someone else pinned. |
| `.gitignore` | Add lines only. |
| `VIGIL/VIGIL/Theming/Theme.cs`, `Icons.cs` | Add new tokens and glyphs at the end of the relevant group. Never rename, reorder or change an existing one. |
| `VIGIL/VIGIL/Pages/GalleryPage.cs` | Add the row for your new control at the end. Leave other rows alone. |
| `VIGIL/VIGIL/Forms/MainForm.cs` | Add one `AddPage(...)` line for your page. Don't reorder the others. |
| `VIGIL/VIGIL/VIGIL.csproj`, `VIGIL.csproj.user` | Add your own entry only. |
| `*.Designer.cs`, `*.resx` | Only the person who owns that form or page touches them. |
| `web/package.json`, `web/package-lock.json` | Ask Dan before adding a package. Commit both files together, and never edit the lock file by hand. |
| `index.html`, `viewer/` (at the root) | The built viewer. Written only by `npm run build:root` in `web/`. Never edit them by hand, and never point `index.html` at source files: a browser cannot run them. |
| `web/src/theme.css` | Mirrors `Theme.cs`. Add new variables at the end of the relevant group. Never rename or change an existing one without changing `Theme.cs` to match. |
| `web/src/App.tsx`, `web/src/app.css` | The viewer's shell. Each part of the viewer keeps to its own folder under `web/src/` and is wired in here with one import. |

The C# and `web/` rows only matter when two people, or two agents, share that area. With one owner working alone, they are that owner's files.

## Git

- Commit small and often, and push after each commit that builds. A broken build should trace back to one change.
- Stage files by path. Never `git add -A` or `git add .`, which sweep in files that aren't yours.
- Run `git pull --rebase` before you push. If the rebase conflicts in a file outside your paths, stop and tell your person. Don't resolve someone else's file.
- Never force-push. Never rewrite history that has been pushed.
- Don't run a formatter, a mass rename or a "tidy up" across the repo. Every line it touches is a conflict for someone.
- Don't commit build output (`bin/`, `obj/`, `.vs/`), downloaded data or the results cache.

Whether agents commit and push themselves, or leave changes for their person to commit, is not decided. Until Dan says otherwise, agents make the changes and their person commits.

## Handover notes

Anyone who stops while others carry on, at the 21:00 venue close or after working from home, tells the others before they go:

- what works now
- what is broken or half-done, and in which file
- what should be picked up next
