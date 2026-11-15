// Films the web viewer: relief standing up, the view leaning over, and the globe unrolling into
// the flat map and rolling up again. The page's own clock is replaced by one this script steps,
// a thirtieth of a second at a time, with a picture taken after each step. So the film is smooth
// however long a picture takes, and every frame is the viewer's own drawing of the real scan.
//
//   node film-web.cjs <address> <folder for the frames> [variable = Precipitation] [place = Amazon]
//
// Needs PW_CORE (the folder of playwright-core) and CHROME (a Chromium to run) in the environment.
const fs = require('fs');
const path = require('path');
const { chromium } = require(process.env.PW_CORE);

const FPS = 30;
const [address, folder, variable = 'Precipitation', place = 'Amazon'] = process.argv.slice(2);

// What happens when, in seconds of film. The end is the start again, so the clip loops.
const script = [
  { at: 0.7, press: 'Relief' },      // the columns stand up, and the view leans over with them
  { at: 2.6, press: 'Flat map' },    // the globe unrolls
  { at: 5.4, press: 'Flat map' },    // and rolls up again
  { at: 7.4, press: 'Relief' },
  { at: 7.4, press: 'Tilt', onlyIf: true },
  { at: 7.7, go: true },             // back to where it began, by the viewer's own place search
];
const length = 11.0;

/** Replaces the page's clocks with one that only moves when it is stepped. */
function clock() {
  const realNow = performance.now.bind(performance);
  const realInterval = window.setInterval.bind(window);
  const realTimeout = window.setTimeout.bind(window);
  const RealDate = Date;
  const epoch = RealDate.now();
  let now = 0;
  let frames = [];
  let frameId = 0;
  const timers = new Map();
  let timerId = 1;

  performance.now = () => now;
  window.Date = class extends RealDate {
    constructor(...args) { if (args.length === 0) super(epoch + now); else super(...args); }
    static now() { return epoch + now; }
  };
  window.requestAnimationFrame = (callback) => { frames.push({ id: ++frameId, callback }); return frameId; };
  window.cancelAnimationFrame = (id) => { frames = frames.filter((frame) => frame.id !== id); };
  // A timer never fires in the step that set it sooner than a millisecond on, so one that sets itself again cannot spin.
  window.setTimeout = (callback, ms = 0, ...args) => { timers.set(timerId, { at: now + Math.max(1, ms), callback, args, every: 0 }); return timerId++; };
  window.setInterval = (callback, ms = 0, ...args) => { timers.set(timerId, { at: now + Math.max(1, ms), callback, args, every: Math.max(1, ms) }); return timerId++; };
  window.clearTimeout = window.clearInterval = (id) => { timers.delete(id); };

  const step = (ms) => {
    const target = now + ms;
    for (;;) {
      let next = null;
      for (const [id, timer] of timers) {
        if (timer.at <= target && (next === null || timer.at < next.timer.at)) next = { id, timer };
      }
      if (next === null) break;
      now = Math.max(now, next.timer.at);
      if (next.timer.every > 0) next.timer.at += next.timer.every; else timers.delete(next.id);
      try { if (typeof next.timer.callback === 'function') next.timer.callback(...next.timer.args); } catch (error) { console.error(error); }
    }
    now = target;
    const due = frames;
    frames = [];
    for (const frame of due) {
      try { frame.callback(now); } catch (error) { console.error(error); }
    }
  };

  const state = { free: true, step, settle: () => new Promise((done) => realTimeout(done, 0)) };
  window.__clock = state;
  // Until the filming starts the clock keeps real time, so the page loads and opens as it would.
  let last = realNow();
  realInterval(() => {
    const real = realNow();
    if (state.free) step(Math.min(50, real - last));
    last = real;
  }, 8);
}

(async () => {
  fs.rmSync(folder, { recursive: true, force: true });
  fs.mkdirSync(folder, { recursive: true });
  const browser = await chromium.launch({
    executablePath: process.env.CHROME,
    headless: true,
    args: ['--use-angle=d3d11', '--enable-gpu', '--ignore-gpu-blocklist', '--enable-webgl'],
  });
  // The film's frame is 1920 by 1080 with a 108 pixel band for the caption, so the page gets the rest.
  const context = await browser.newContext({ viewport: { width: 1920, height: 972 }, deviceScaleFactor: 1, reducedMotion: 'no-preference', colorScheme: 'dark' });
  await context.addInitScript(clock);
  const page = await context.newPage();
  await page.goto(address, { waitUntil: 'networkidle', timeout: 90000 });
  await page.waitForTimeout(5000);

  const badge = await page.evaluate(() => document.body.innerText.includes('Synthetic fixture'));
  if (badge) throw new Error('the page is showing the synthetic fixture, not a real scan');

  const isOn = (label) => page.locator('label.switch', { hasText: label }).locator('input').isChecked();
  const press = (label) => page.locator('label.switch', { hasText: label }).click();

  // Setting the scene, off the film.
  if (await isOn('Satellites')) await press('Satellites');   // they are not drawn on the flat map
  await page.getByRole('radio', { name: variable, exact: true }).first().click();   // the layer's picker comes before the list's filter
  const search = page.getByPlaceholder('Go to a place');
  await search.fill(place);
  await page.waitForTimeout(400);
  await search.press('Enter');
  await page.waitForTimeout(5000);
  await page.mouse.move(960, 965);
  await page.evaluate(() => document.activeElement && document.activeElement.blur());
  await page.waitForTimeout(500);

  await page.evaluate(() => { window.__clock.free = false; });
  const total = Math.round(length * FPS);
  const pending = script.map((entry) => ({ ...entry }));
  for (let frame = 0; frame < total; frame++) {
    const time = frame / FPS;
    for (const entry of pending.filter((e) => !e.done && e.at <= time)) {
      entry.done = true;
      if (entry.go) {
        await search.fill(place);
        await page.waitForTimeout(400);
        await search.press('Enter');
        await page.evaluate(() => document.activeElement && document.activeElement.blur());
        await page.mouse.move(960, 965);
        console.log(`${time.toFixed(2)} s: go to ${place}`);
      } else if (entry.onlyIf === undefined || (await isOn(entry.press)) === entry.onlyIf) {
        await press(entry.press);
        await page.mouse.move(960, 965);
        console.log(`${time.toFixed(2)} s: ${entry.press}`);
      }
    }
    await page.evaluate(async (ms) => { window.__clock.step(ms); await window.__clock.settle(); }, 1000 / FPS);
    await page.screenshot({ path: path.join(folder, 'w' + String(frame).padStart(5, '0') + '.png') });
  }
  console.log(`${total} frames, ${length} seconds`);
  await browser.close();
})().catch((error) => { console.error(error); process.exit(1); });
