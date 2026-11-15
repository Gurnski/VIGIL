# submission

The demo script, the presentation and the text of the written project page. All of it is due at 15:00 on Sunday 15 November. Section 10 of `Project_Brief.pdf` has the walk-through and the checklist.

| File | What it is |
|---|---|
| `demo-script.md` | The live walk-through, screen by screen, with how to start the service and what to do when something fails |
| `project-page.md` | Draft text for the written project page, with the re-test table, the known-answer check and the declarations |
| `video/` | The 30-second demo video (`vigil-demo.mp4`), the looping clip of relief, tilt and the flat map that one slide plays (`vigil-views.mp4`), and the programs that film them from the app and the web viewer |
| `slides/` | The source of the presentation: one HTML file per slide, with `deck.json` giving their order |
| `stills/` | Pictures of the app on the frozen scan, taken while the video was filmed. The slides use the `slide-*.png` files, which are four of those stills cut down to the app's panels, and `slide-views.png`, a frame of `video/vigil-views.mp4`. `cover-earth.png` is the cover's Earth, drawn separately (see below). |
| `checks/` | The scripts that were run on the scan before it was frozen, and what they printed |
| `poster/` | The poster at the top of the repository's README: `poster.png`, and `poster.html`, the page it is a picture of |

The presentation itself is a Claude artifact, and Dan has the link. `slides/` is a copy of what was published to it, kept here so the words are under version control. The pictures and the video on the slides are stored with the artifact, so the HTML files do not show them when opened alone.

The pictures of the app on the slides have no frame. The app's background colour is the slides' (`Theme.Background`, `#131415`), so a still cut down to the app's own panels sits on the slide with no edge. The cuts are: the three Map stills from (280, 200), 1624 by 772, and the Evidence still from (680, 148), 1224 by 808.

The poster is the cover slide with a row of figures under it: the cover's Earth (`stills/cover-earth.png`), its title in the deck's own face and size, and the deck's colours. Its four figures are the frozen scan's. To make it again, open `poster/poster.html` in a browser with the window 1920 pixels wide, wait for the fonts, and save a picture of the whole page as `poster/poster.png`. Edge and Chrome do that from the developer tools with "Capture full size screenshot".

The Earth on the cover is the app's own globe, drawn by `slides/earth/`. To make it again, build the app and then, from `slides/`:

```
dotnet run --project earth -- <folder> cover-earth 4700 -5 8 2026-06-21T18:15:00Z
```

The numbers are the globe's width in pixels, the latitude and longitude at the centre of the view, and the time the sun is set to. That evening in June puts western Europe in daylight and the east in the dark.

Every figure in these files is from run `scan-20261115T005506Z`. A new scan changes them: take them again from the app's Overview page and from `checks/final-run.sh`.
