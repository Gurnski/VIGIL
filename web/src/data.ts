import type { AskFixture, ResultsDocument } from './contract';

// Files fetched at run time sit beside index.html, so a real snapshot.json can replace the
// fixture on the bucket without rebuilding the viewer.
const base = import.meta.env.BASE_URL;

async function fetchJson<T>(name: string): Promise<T> {
  const response = await fetch(base + name, { cache: 'no-cache' });
  if (!response.ok) {
    throw new Error(`${name} could not be loaded (${response.status}).`);
  }
  return (await response.json()) as T;
}

/** The results snapshot, with its findings put in rank order. */
export async function loadSnapshot(): Promise<ResultsDocument> {
  const document = await fetchJson<ResultsDocument>('snapshot.json');
  if (!document.contract_version) {
    throw new Error('snapshot.json is not a VIGIL results document: contract_version is missing.');
  }
  document.findings = [...(document.findings ?? [])].sort((a, b) => a.rank - b.rank);
  return document;
}

/** The fixture's example questions and canned answers, for when the service can't be reached. */
export function loadAskFixture(): Promise<AskFixture> {
  return fetchJson<AskFixture>('ask.json');
}
