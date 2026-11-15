import type { AskFixture, ResultsDocument } from './contract';

// Files fetched at run time sit beside index.html, so a real snapshot.json can replace the
// fixture on the server without rebuilding the viewer.
export const base = import.meta.env.BASE_URL;

// Where the fixture is when the page is served from the root of the repository, as it is on
// the bucket and on the cloud machine. Used when there is no file beside index.html.
const fixtureFolder = 'contract/fixture/';

async function fetchJson<T>(path: string): Promise<T> {
  const response = await fetch(base + path, { cache: 'no-cache' });
  if (!response.ok) {
    throw new Error(`${path} could not be loaded (${response.status}).`);
  }
  // A server may answer a missing file with its own HTML page, which fails here and counts as missing.
  return (await response.json()) as T;
}

/** The file beside index.html if there is one, otherwise the contract's fixture. */
async function fetchResultsFile<T>(name: string): Promise<T> {
  try {
    return await fetchJson<T>(name);
  } catch (missing) {
    try {
      return await fetchJson<T>(fixtureFolder + name);
    } catch {
      throw missing;
    }
  }
}

/** The results snapshot, with its findings put in rank order. */
export async function loadSnapshot(): Promise<ResultsDocument> {
  const document = await fetchResultsFile<ResultsDocument>('snapshot.json');
  if (!document.contract_version) {
    throw new Error('snapshot.json is not a VIGIL results document: contract_version is missing.');
  }
  document.findings = [...(document.findings ?? [])].sort((a, b) => a.rank - b.rank);
  return document;
}

/** The fixture's example questions and canned answers, for when the service can't be reached. */
export function loadAskFixture(): Promise<AskFixture> {
  return fetchResultsFile<AskFixture>('ask.json');
}
