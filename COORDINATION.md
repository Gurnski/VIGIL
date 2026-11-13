# Who works where

Several people are building VIGIL at once, each with an AI coding agent. Agents rewrite files freely, so two of them in the same file means a merge conflict, or one silently undoing the other. This file keeps everyone in separate parts of the repo.

Read it before every session. It applies to people and agents alike. `AGENTS.md` covers how to write the code; this file covers which code is yours to write.

Nothing enforces these rules. They work because every agent reads them and every person holds their agent to them.

## Before you edit anything

If you are an agent, do these in order:

1. Run `git config user.name` to find out whose agent you are.
2. Open `claims/` and find the file whose `Git name` matches. That file lists the areas and paths you may edit.
3. If no claim matches, or the claim is empty, stop and ask your person which area they have taken. Don't guess from the task and don't pick an unclaimed area yourself.
4. Run `git pull --rebase` so you start from everyone's latest work.

## Work areas

Each area has one owner. The owner is whoever names it in their claim file. The areas match section 08 of `Project_Brief.pdf`.

| Area | Paths | What it is |
|---|---|---|
| `contract` | `contract/` | The JSON format of a finding, and the fixture file of fake results. Dan owns this whatever else he takes. |
| `data-api` | `python/vigil/pipeline/`, `python/vigil/api/`, `infra/` | The discovery run: the cloud machine and bucket, downloading and regridding NASA data, the catalogue and results cache, the run record, and the FastAPI service |
| `analysis` | `python/vigil/analysis/` | Trends, significance, change points, cascades, contradictions, ranking |
| `detective` | `python/vigil/detective/` | The AI Detective: tool calls over the API, grounded answers |
| `interface` | `VIGIL/` | The whole C# project: pages, controls, API client, models |
| `submission` | `docs/submission/`, `README.md` | Demo video script, presentation, written project page |
| `project-docs` | `AGENTS.md`, `COORDINATION.md`, `CLAUDE.md`, `Project_Brief.*` | Dan owns these |

The folders exist and each holds a short README saying what belongs there. They are otherwise empty until the event.

Tests live beside the code they test and belong to the same owner.

## Claiming an area

Each person has one file, `claims/<name>.md`, and only that person or their agent edits it. Copy `claims/_template.md` to start.

1. Check the other files in `claims/` to see what is taken.
2. Write the areas you are taking into your own file.
3. Commit and push that file on its own, before any other work. The push is what makes the claim real. If the push is rejected because someone else pushed first, pull, check their claim, and pick again if you clash.

To hand an area over, remove it from your file and push. The next person then adds it to theirs.

If two people share an area, each lists the exact folders or files they hold, so no path appears in two claims.

## Staying in your area

- Edit only files under the paths in your claim.
- Read anything you like. Reading never conflicts.
- If a task needs a change outside your paths, stop. Tell your person what change is needed, in which file, and why. They ask the owner. Add a line under `Waiting on` in your own claim file so the request isn't lost.
- Don't work around a missing change by copying someone else's code into your area, or by writing a second version of it.
- Before you finish, run `git status`. Every changed file must sit under your paths. If one doesn't, and you changed it this session, undo that change and report it.

## The contract

`contract/` is read-only for everyone except Dan. Both halves of the system depend on it: the Python side writes findings in that format and the C# side reads them.

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

The C# rows only matter when two people share the `interface` area. With one owner, they are that owner's files.

## Git

- Commit small and often, and push after each commit that builds. A broken build should trace back to one change.
- Stage files by path. Never `git add -A` or `git add .`, which sweep in files that aren't yours.
- Run `git pull --rebase` before you push. If the rebase conflicts in a file outside your paths, stop and tell your person. Don't resolve someone else's file.
- Never force-push. Never rewrite history that has been pushed.
- Don't run a formatter, a mass rename or a "tidy up" across the repo. Every line it touches is a conflict for someone.
- Don't commit build output (`bin/`, `obj/`, `.vs/`), downloaded data or the results cache.

Whether agents commit and push themselves, or leave changes for their person to commit, is not decided. Until Dan says otherwise, agents make the changes and their person commits.

## Handover notes

Anyone who stops while others carry on, at the 21:00 venue close or after working from home, writes a `Handover` section in their own claim file:

- what works now
- what is broken or half-done, and in which file
- what should be picked up next

Whoever starts a session reads every claim file's handover first.
