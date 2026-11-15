import { useEffect, useState } from 'react';
import type { Finding, ResultsDocument, Series } from '../contract';
import { base } from '../data';
import { fetchDocument, serviceRun } from '../service';

// Where a finding's series comes from, in the order the contract gives under "Series as files
// beside the viewer": the results document on screen, then the file series/<variable>/<cell_id>.json
// beside index.html, then the service's GET /series. A site with no service behind it stops at
// the file. Nothing here changes a value.

/** One series a finding's evidence draws: a cell and a variable. */
export interface SeriesNeed {
  cellId: string;
  variable: string;
}

export type SeriesState = { status: 'ready'; series: Series } | { status: 'loading' } | { status: 'missing' };

const fileTimeoutMs = 8000;
const serviceTimeoutMs = 8000;
/** A source that could not be reached is asked again after this long. One that answered "no" is not. */
const retryAfterMs = 30000;

/**
 * Every (cell, variable) the evidence view draws for a finding: its own cell with each of its
 * variables, and for a contradiction the paired cell too. The same rule as cells_and_variables
 * in python/vigil/api/document.py, which decides which series files are written.
 */
export function seriesNeeded(finding: Finding): SeriesNeed[] {
  const needed = finding.variables.map((variable) => ({ cellId: finding.cell.id, variable: variable.id }));
  if (finding.contradiction) {
    needed.push({ cellId: finding.contradiction.paired_cell.id, variable: finding.contradiction.variable });
  }
  return needed;
}

/** What one source said: the series, a firm "not here", or no answer at all. */
type Answer = { series: Series } | 'absent' | 'unreachable';

function isSeries(value: unknown, need: SeriesNeed): value is Series {
  if (typeof value !== 'object' || value === null) {
    return false;
  }
  const series = value as Partial<Series>;
  return (
    series.cell_id === need.cellId &&
    series.variable === need.variable &&
    typeof series.units === 'string' &&
    Array.isArray(series.dates) &&
    Array.isArray(series.raw) &&
    Array.isArray(series.anomaly) &&
    series.dates.length > 0 &&
    series.raw.length === series.dates.length &&
    series.anomaly.length === series.dates.length
  );
}

function seriesIn(document: Record<string, unknown>, need: SeriesNeed): Answer {
  const list = Array.isArray(document.series) ? document.series : [];
  const found = list.find((item) => isSeries(item, need));
  return found ? { series: found as Series } : 'absent';
}

/** The file beside index.html. One written for another run is refused: it would be drawn beside the wrong scan. */
async function fromFile(runId: string, need: SeriesNeed): Promise<Answer> {
  const url = `${base}series/${encodeURIComponent(need.variable)}/${encodeURIComponent(need.cellId)}.json`;
  const document = await fetchDocument(url, fileTimeoutMs);
  if (typeof document === 'string') {
    return document;
  }
  return document.run_id === runId ? seriesIn(document, need) : 'absent';
}

/**
 * The service, when there is one and it holds the run on screen. Its replies to GET /series do
 * not name their run, so the run it holds is asked for first (see serviceRun).
 */
async function fromService(runId: string, need: SeriesNeed): Promise<Answer> {
  const held = await serviceRun();
  if (typeof held === 'string') {
    return held;
  }
  if (held.runId !== runId) {
    return 'absent';
  }
  const url = `${held.address}/series?cell_id=${encodeURIComponent(need.cellId)}&variable=${encodeURIComponent(need.variable)}`;
  const document = await fetchDocument(url, serviceTimeoutMs);
  if (typeof document === 'string') {
    return document;
  }
  // The reply names no run today. If it ever does, it must be this one.
  return document.run_id === undefined || document.run_id === runId ? seriesIn(document, need) : 'absent';
}

/** What has been fetched this session, by run, cell and variable. `until` is set on an answer that may change. */
const fetched = new Map<string, { series: Series | null; until?: number }>();
const fetching = new Map<string, Promise<void>>();

function cacheKey(runId: string, need: SeriesNeed): string {
  return `${runId}\n${need.variable}\n${need.cellId}`;
}

/** True when there is no answer yet, or the one held was "no reply" and is old enough to ask again. */
function shouldFetch(key: string): boolean {
  const entry = fetched.get(key);
  return !entry || (entry.until !== undefined && entry.until < Date.now());
}

/** Looks in the file, then the service, and remembers the answer. Several callers share one fetch. */
function fetchSeries(runId: string, need: SeriesNeed): Promise<void> {
  const key = cacheKey(runId, need);
  let running = fetching.get(key);
  if (!running) {
    running = (async () => {
      const file = await fromFile(runId, need);
      const answer = typeof file === 'object' ? file : await fromService(runId, need);
      if (typeof answer === 'object') {
        fetched.set(key, { series: answer.series });
      } else if (file === 'unreachable' || answer === 'unreachable') {
        fetched.set(key, { series: null, until: Date.now() + retryAfterMs });
      } else {
        fetched.set(key, { series: null });
      }
    })().finally(() => fetching.delete(key));
    fetching.set(key, running);
  }
  return running;
}

/**
 * The series for each need, in the same order. One the results document holds is ready at once.
 * The rest are "loading" until the file or the service has answered, then "ready" or "missing".
 */
export function useSeries(results: ResultsDocument, needs: SeriesNeed[]): SeriesState[] {
  const runId = results.run?.id ?? null;
  const wanted = needs.map((need) => `${need.variable}/${need.cellId}`).join(' ');
  // Counts answers as they arrive, so the states below are read again.
  const [, setArrived] = useState(0);

  const own = (need: SeriesNeed) =>
    results.series?.find((series) => series.cell_id === need.cellId && series.variable === need.variable);

  useEffect(() => {
    if (runId === null) {
      return;
    }
    let current = true;
    for (const need of needs) {
      const key = cacheKey(runId, need);
      if (!own(need) && shouldFetch(key)) {
        fetched.delete(key);
        void fetchSeries(runId, need).then(() => current && setArrived((count) => count + 1));
      }
    }
    // An answer for a finding that is no longer on screen stays in the cache and redraws nothing.
    return () => {
      current = false;
    };
    // `wanted` stands for `needs`, which is a new array on every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [results, runId, wanted]);

  return needs.map((need) => {
    const stored = own(need);
    if (stored) {
      return { status: 'ready', series: stored };
    }
    // Without a run id there is nothing to check a file or the service against.
    if (runId === null) {
      return { status: 'missing' };
    }
    const entry = fetched.get(cacheKey(runId, need));
    if (!entry) {
      return { status: 'loading' };
    }
    return entry.series ? { status: 'ready', series: entry.series } : { status: 'missing' };
  });
}
