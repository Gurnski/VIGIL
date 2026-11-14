// Copies the built viewer from web/dist/ to the root of the repository: index.html and the
// viewer/ folder. With those committed, any plain web server pointed at a checkout of the
// repository serves the viewer, with no Node and no build step on that machine.
//
// snapshot.json and ask.json are not copied. At the root the viewer reads results from a
// snapshot.json placed there by the scan, and falls back to contract/fixture/ when there is none.
//
// Run it through `npm run build:root`, which builds first. The copy goes stale when the viewer's
// source changes, so run it again before committing a change to web/.
import { cpSync, existsSync, rmSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const web = join(dirname(fileURLToPath(import.meta.url)), '..');
const dist = join(web, 'dist');
const root = join(web, '..');

if (!existsSync(join(dist, 'index.html'))) {
  throw new Error('web/dist/index.html is missing. Run `npm run build` first.');
}

// The built files have a hash in their names, so the old ones are cleared out, not overwritten.
rmSync(join(root, 'viewer'), { recursive: true, force: true });
cpSync(join(dist, 'viewer'), join(root, 'viewer'), { recursive: true });
cpSync(join(dist, 'index.html'), join(root, 'index.html'));
console.log('Viewer copied to the repository root: index.html and viewer/');
