# submission

The demo script, the presentation and the text of the written project page. All of it is due at 15:00 on Sunday 15 November. Section 10 of `Project_Brief.pdf` has the walk-through and the checklist.

| File | What it is |
|---|---|
| `demo-script.md` | The live walk-through, screen by screen, with how to start the service and what to do when something fails |
| `project-page.md` | Draft text for the written project page, with the re-test table, the known-answer check and the declarations |
| `video/` | The 30-second demo video (`vigil-demo.mp4`) and the program that films it from the app |
| `slides/` | The source of the presentation: one HTML file per slide, with `deck.json` giving their order |
| `stills/` | Pictures of the app on the frozen scan, taken while the video was filmed. The slides use them. |
| `checks/` | The scripts that were run on the scan before it was frozen, and what they printed |

The presentation itself is a Claude artifact, and Dan has the link. `slides/` is a copy of what was published to it, kept here so the words are under version control. The pictures and the video on the slides are stored with the artifact, so the HTML files do not show them when opened alone.

Every figure in these files is from run `scan-20261115T005506Z`. A new scan changes them: take them again from the app's Overview page and from `checks/final-run.sh`.
