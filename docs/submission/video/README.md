# The demo video

`vigil-demo.mp4` is about 30 seconds long, 1920 by 1080, with no sound. Every frame of it is the desktop app's own window, drawn to a picture while the app shows the real scan. Only the caption under the window, the dot that marks the pen, and the title and end cards are added.

`film/` is the program that makes it. It opens the app's main window off screen, walks it through a storyboard and saves a picture of it about fourteen times a second.

## What the film shows

1. The title card.
2. The Overview page: what the scan covered and found.
3. The globe turning, one variable's layer after another.
4. The top-ranked finding, whichever the scan put first.
5. Its evidence, scrolled through.
6. A trend the second product contradicted.
7. Precipitation over the Amazon in relief, then played year by year from 2001 to 2025.
8. A zone drawn on the globe, named, saved and analysed.
9. The Detective's panel.
10. The end card, with the data credits.

The storyboard is the `Storyboard` class at the bottom of `film/Film.cs`. The captions are there too.

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
- The years in shot 7 come from the service (`GET /years`). Without it that shot is the relief alone, and the film is about four seconds shorter.
- The Detective is filmed answering only when the service says it has a key (`"detective": true` in `GET /health`). The question is put to the real service and whatever comes back is what is filmed. Without a key the film shows the panel open and no answer. An answer is never staged. The copy committed here was filmed without a key. Filmed with one, the Detective's part runs about four seconds longer, so shorten another shot in the storyboard if the film has to stay at 30 seconds.
- The chats and zones the film makes go in a scratch folder (`VIGIL_DATA_DIR`), not in yours.
- The captions quote the scan's own counts, read from the run record when the film is made. If a caption states anything else about the results, check it against the scan before publishing.
