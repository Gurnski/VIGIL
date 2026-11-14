// Copies the contract's fixture into public/, where the viewer fetches it at run time as
// snapshot.json and ask.json. The copies are not committed: contract/fixture/ stays the only
// source. To show real results, put a real snapshot.json beside index.html on the bucket.
import { copyFileSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const web = join(dirname(fileURLToPath(import.meta.url)), '..');
const fixture = join(web, '..', 'contract', 'fixture');
mkdirSync(join(web, 'public'), { recursive: true });
for (const name of ['snapshot.json', 'ask.json']) {
  copyFileSync(join(fixture, name), join(web, 'public', name));
}
