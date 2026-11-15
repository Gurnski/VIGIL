# The demo video

`vigil-demo.mp4` is 29.8 seconds long, 1920 by 1080, with no sound. Every frame of it is VIGIL's own drawing of the real scan: the desktop app's window, and for one shot the web viewer's page. Only the caption under the picture, the dot that marks the pen, and the title and end cards are added.

It is made in two steps. `film/` films the desktop app: it opens the app's main window off screen, walks it through a storyboard and saves a picture of it about fourteen times a second. `web/` films the web viewer and puts 3.3 seconds of it into the desktop film (see "The web viewer's shot" below).

`vigil-views.mp4` is a second, shorter film for the presentation: the web viewer's globe alone for 9.6 seconds, with the relief standing up, the view leaning over, and the globe unrolling into the flat map and rolling up again. It ends where it starts, so the slide can loop it.

## What the film shows

1. The title card.
2. The Overview page: what the scan covered and found.
3. The globe turning, one variable's layer after another.
4. The top-ranked finding, whichever the scan put first.
5. Its evidence, scrolled through.
6. A trend the second product contradicted.
7. The web viewer: precipitation over the Amazon in relief with the view leaned over, then the globe unrolling into the flat map.
8. The desktop app again: the same layer in relief, played year by year from 2001 to 2025.
9. A zone drawn on the globe, named, saved and analysed.
10. The Detective's panel.
11. The end card, with the data credits.

The desktop app's storyboard is the `Storyboard` class at the bottom of `film/Film.cs`. Its captions are there too. Run alone it makes a film with no shot 7 and with the desktop app's own 1.3 seconds of relief in its place.

## The web viewer's shot

The flat map is in the web viewer only, so that shot is filmed in a browser.

`web/film-web.cjs` opens the viewer in headless Chromium at 1920 by 972, the size of the desktop app's window in the film. It switches the satellites off, chooses precipitation and goes to the Amazon, then films 11 seconds: Relief on, Flat map on, Flat map off, Relief and Tilt off, and back to the Amazon by the place search. The page's clock is replaced by one the script steps a thirtieth of a second at a time, with a picture taken after each step, so the film is smooth however long a picture takes. It was filmed from keepvigil.earth, and it stops if the page shows the synthetic fixture.

```
$env:PW_CORE = "<the folder of playwright-core>"
$env:CHROME = "<a Chromium's chrome.exe>"
node web\film-web.cjs https://keepvigil.earth/ <folder for the frames>
web\splice.ps1 -Ffmpeg <ffmpeg.exe> -Frames <folder for the frames> -Desktop <the desktop film> -Out <folder>
```

`web/splice.ps1` writes both films. For `vigil-demo.mp4` it draws the caption band under 3.3 seconds of the frames as `Film.cs` draws it (`web/dress.ps1`), draws the end card again with a last line that names the web viewer, and joins the pieces. To keep the film under 30 seconds it takes time from the desktop film: 0.6 seconds of the title, the last 0.4 seconds of the layers, the desktop's 1.3 seconds of relief, the last 0.4 seconds of the zone's trends and 0.4 seconds of the end card. The frame numbers it cuts at are in the script, and they are for the desktop film as it stood at commit `7074dc2`. Film the desktop app again and they move.

The satellites are off in the web shot because they are not drawn on the flat map, and would vanish as the globe unrolls.

## Making it again

Start the analysis service on a real scan first (see `../demo-script.md`), then from this folder:

```
dotnet build ..\..\..\VIGIL
dotnet run --project film -- <folder for the frames> http://127.0.0.1:8000
ffmpeg -f concat -safe 0 -i <folder for the frames>\frames.txt -vf "fps=30,format=yuv420p" -t <seconds> -c:v libx264 -preset slow -crf 17 vigil-demo.mp4
```

The film prints its length when it finishes ("29.6 seconds on film"). Give that to `-t`, or the encoder holds the end card twice as long. The frames folder is emptied first, so give it one of its own. The film also saves a full-size still of each screen in `<folder>\stills\`: the slides use those.

## Things to know

- The app runs at two fifths of its speed while it is filmed, and each frame is saved with the time it was taken. The encoder divides the times back, which is how a slow off-screen capture comes out smooth. Only the time inside a shot counts: the pauses between shots, while a page opens or the service answers, are not on the film.
- The globe's sun is set so the region being filmed is in daylight, through the globe's own `SunTimeUtc`. The read-out in the corner of the globe then says "Sunlight at" that time, not "Live sunlight". The copy committed here was filmed at one in the morning UTC, when Africa and South America are both dark.
- The film drives the app the way a hand would where it can: the zone is drawn by sending the globe mouse messages. To reach the controls it reads the app's private fields by name, so renaming one in the app breaks the film, with an error that names the field.
- The years in shot 8 come from the service (`GET /years`). Without it that shot is the relief alone, and the film is about four seconds shorter.
- The Detective is filmed answering only when the service says it has a key (`"detective": true` in `GET /health`). The question is put to the real service and whatever comes back is what is filmed. Without a key the film shows the panel open and no answer. An answer is never staged. The copy committed here was filmed without a key. Filmed with one, the Detective's part runs about four seconds longer, so shorten another shot in the storyboard if the film has to stay at 30 seconds.
- The chats and zones the film makes go in a scratch folder (`VIGIL_DATA_DIR`), not in yours.
- The captions quote the scan's own counts, read from the run record when the film is made. If a caption states anything else about the results, check it against the scan before publishing.
