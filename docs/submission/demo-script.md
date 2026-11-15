# Demo script

The walk-through from section 10 of the brief, matched to the screens as they are now. It serves the recorded video and the live presentation. Allow five minutes. Every number you read out is on screen: read it from there, not from memory, because a new scan changes it.

## Before you start

1. Start the service. The quick way is `python\start-service.cmd -Network`: it finds the scan and the gridded data in `data\scan`, reads the Detective's key from `ANTHROPIC_API_KEY` (save it once with `setx ANTHROPIC_API_KEY "the key"`), says what it found, and starts the service again if it stops. By hand, from `python/`, it is:

   ```
   $env:VIGIL_SNAPSHOT = "..\data\scan\snapshot.json"
   $env:VIGIL_SCAN_DATA = "..\data\scan\scan-data.npz"
   $env:ANTHROPIC_API_KEY = "the key"
   .venv\Scripts\python -m uvicorn vigil.api.app:app --host 0.0.0.0 --port 8000
   ```

   `--host 0.0.0.0` lets the audience's phones and laptops reach it. Leave it out and everything still works on this machine, but nobody can follow along. Windows asks once whether to let Python through the firewall: say yes for the network you are on. Without the key everything works except the Detective. Without the service the app opens on the made-up fixture and says so, which is not what you want on stage.
2. Check the service is up before you trust the app. Open `http://127.0.0.1:8000/health` in a browser: it should say `"scan_data": true`, and `"detective": true` if the key is set. Then start the app, which loads the service's scan and opens on Overview. The absence of a "Synthetic fixture" badge does not prove the service is running: with a `snapshot.json` beside the exe the app shows the real scan even when the service is down, and then zones, Years, Recompute, presenting and the Detective would all fail on stage. Inside the app, look at the badge beside the Present button. It asks the service every five seconds and should read "Service connected". "Service connected, no Detective" means the key is missing, "Service holds another scan" means the app was opened before the service and needs reopening, and "Service not running" means what it says.
3. On the Map page, open the Zones tab and delete any zone left from rehearsal.
4. Ask the Detective one question and wait for the answer, so the first live question is not the first call of the day.
5. If the audience is to follow along, try it once with your own phone before anyone is watching: press Present, open the address on the card, and choose a finding. A venue's Wi-Fi may stop devices seeing each other. A phone's hotspot, with the laptop joined to it, gets round that.

## The walk-through

| Step | Where | What to do | What to say |
|---|---|---|---|
| 1 | Any page | Press Present. Leave the card up while people open the address. | Open this on your phone and your screen follows mine. You can stop following and look round by yourself at any time. |
| 2 | Overview | Point at the four figures. | How many tests the scan ran, how many passed after correction, and how many of those a second NASA record confirmed. Nothing was chosen in advance. |
| 3 | Overview | Read one variable's row, then its re-test line. | Every variable went through the same test. Each trend was then tested again on a second product, and the page gives the rate to hold that against. |
| 4 | Map | Switch between two layers. Turn on "Significant only". | Each cell is one test. Coloured cells passed. Cells left clear were skipped, with a reason on record. |
| 5 | Map | Turn on "Relief". Right-drag to look along the surface. | The same layer, standing up: height is the size of the trend, colour is which way it goes. |
| 6 | Map | Turn on "Years", go to the Amazon preset with the precipitation layer, press play. | This is each year against the cell's own normal. It is for watching a change arrive. The findings do not rest on it. |
| 7 | Map | Turn "Years" and "Relief" off. Click finding 1 in the list, then "Open the evidence". | This is whatever the ranking put first this morning. |
| 8 | Evidence | Read the trend, its interval and its p-value. Scroll to the chart, the score and the method. | The size of the change, how sure we are, and how it was tested. The dashed line has the slope the service reported. |
| 9 | Evidence | Press "Recompute". | The service has just worked this cell out again from the gridded data, and the figures match the stored ones. |
| 10 | Evidence | Choose "Cascades" and open the first. | Two variables in one cell, one trailing the other by so many months. An association, not a cause. |
| 11 | Evidence | Open a trend whose badge says "Caution". The Overview's re-test line says which variable has them. | The second record disagrees here, and VIGIL says so on the finding itself. |
| 12 | Map, Zones tab | Press "Draw a zone", draw round a region, name it, save. | Any region, tested on the spot with the same method. The panel says the zone was chosen by eye, so its p-values are exploratory. |
| 13 | Detective | Ask "Where is rainfall falling fastest, and how sure is that?" | It answers only from the findings and names the ones it used. Under the answer is what it looked up. Click a lookup to see where on the globe. |
| 14 | Detective | Ask "What will the weather be like in Plymouth tomorrow?" | It says the scan cannot answer that. |
| 15 | Evidence | Scroll to "What this cannot say" on any finding. Point at "Export GeoJSON". | Every finding carries its own limits. And all of it leaves the app as a file QGIS opens. |
| 16 | Slides | The declarations slide. | What existed before the event, and where AI was used. |

If time is short, drop steps 10, 14 and 15.

## If something fails on stage

- **The service is down.** The app still opens: on the stored scan beside it when there is one, and otherwise on the fixture with its badge. The findings, relief, export and the charts for the top of the ranking still work. Zones' trends, Years, Recompute, presenting and the Detective do not, and each says so. Say what has happened and play the recorded video: `video/vigil-demo.mp4`, which is also on the third slide.
- **Nobody's phone can open the address.** The network is keeping devices apart. Carry on from your own screen: nothing else depends on it. The public site at keepvigil.earth shows the same scan without following.
- **The Detective does not answer.** The chat says why. Carry on: nothing else depends on it.
- **"Years" switches itself off.** The service did not answer. The status badge on the globe says why, and the trend layer is still there.
- **A zone shows a message in place of figures.** The service is serving a different scan from the one the app loaded. Restart the app.

## What not to claim

- That a cascade shows one thing causing another.
- That "re-tested" means proven. The second products share inputs with the first, and each finding says how.
- That a zone's p-value is a test. The zone was drawn after looking at the map.
- That a year in the "Years" view is a finding. It is one year's average against normal, with no test behind it.
- That a taller column is a surer result. Height is size, not certainty: "Significant only" is the switch for that.
- Anything about a place smaller than a 2.5° cell.
