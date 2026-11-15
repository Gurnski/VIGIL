# Voiceover for the demo video

`vigil-demo.mp4` has no sound. This is a script for one voice, timed to the film's shots, about 70 words for 29.6 seconds. Numbers are written as words and the name as "Vigil" so a text-to-speech voice reads them properly.

## The script

| Film time | On screen | Line |
|---|---|---|
| 0.0 to 4.2 | Title card, then the Overview page | Vigil scans twenty-five years of NASA records, cell by cell. |
| 4.2 to 6.9 | The globe, layer after layer | Nineteen thousand tests. Nothing chosen in advance. |
| 6.9 to 9.2 | The top-ranked finding | The findings rank themselves. |
| 9.2 to 12.4 | Its evidence, scrolled through | Each opens into its evidence and its limits. |
| 12.4 to 14.7 | A trend the second record contradicted | Most trends are tested again on a second record. |
| 14.7 to 19.7 | Relief, then the years playing | Relief shows the size of a change. The years show it arriving. |
| 19.7 to 25.0 | A zone drawn, named and analysed | Draw any region, and Vigil tests it on the spot. |
| 25.0 to 29.6 | The Detective's panel, then the end card | Ask the Detective: it answers only from the findings. Vigil. Discovery first. |

As one block, to paste:

> Vigil scans twenty-five years of NASA records, cell by cell. Nineteen thousand tests. Nothing chosen in advance. The findings rank themselves. Each opens into its evidence and its limits. Most trends are tested again on a second record. Relief shows the size of a change. The years show it arriving. Draw any region, and Vigil tests it on the spot. Ask the Detective: it answers only from the findings. Vigil. Discovery first.

## What each line rests on

- "Nineteen thousand tests": the scan ran 19,001.
- "Most trends are tested again": 5,047 of the 5,450 significant trends are in the five variables that have a second product. Do not change this to "every trend".
- "Tests it on the spot": a zone's trend is worked out by the service with the scan's own test. Its p-value is exploratory, which the app says on screen.

If a line is changed to quote another figure, take the figure from the app's Overview page.

## Recording it

- Aim for 28 seconds or a little under. The film cannot be stretched without re-shooting it, so a take that runs long has to be sped up or cut.
- A calm, even voice at a normal pace fits. If the first take is over 30 seconds, raise the speed setting slightly before cutting words.
- Save the voice as `audio\voice.mp3` (or `.wav`) in this folder. A music bed, if wanted, goes beside it as `audio\music.mp3`.

## Putting it under the film

Voice only:

```
ffmpeg -i vigil-demo.mp4 -i audio\voice.mp3 -map 0:v -map 1:a -c:v copy -c:a aac -b:a 192k -af "apad" -shortest vigil-demo-voiced.mp4
```

Voice over quiet music:

```
ffmpeg -i vigil-demo.mp4 -i audio\voice.mp3 -i audio\music.mp3 -filter_complex "[2:a]volume=0.18,afade=t=out:st=27.6:d=2[m];[1:a][m]amix=inputs=2:duration=longest:normalize=0[a]" -map 0:v -map "[a]" -c:v copy -c:a aac -b:a 192k -t 29.6 vigil-demo-voiced.mp4
```

Neither re-encodes the picture.

## Before publishing

- A generated voice or generated music is a use of AI. Add it to the "Use of AI" declaration in `../project-page.md`.
- Check the licence that comes with the plan the audio was made on, and whether it asks for a credit.
