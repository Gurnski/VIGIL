import { useEffect, useState } from 'react';
import type { YearsDocument } from '../contract';
import { base } from '../data';
import { fetchDocument, serviceRun } from '../service';

// A variable year by year (the contract's "A variable year by year"): the file
// years/<variable>.json beside index.html first, then the service's GET /years. A file is
// about 1.4 MB, so one is fetched only when the years are switched on for its variable, and kept.

export type YearsState =
  | { status: 'off' }
  | { status: 'loading' }
  | { status: 'ready'; years: YearsDocument }
  /** Neither source has it for the run on screen. */
  | { status: 'missing' };

const timeoutMs = 20000;

/** A years document for this run and variable, in a shape the globe can draw. Anything else is refused. */
function read(document: Record<string, unknown>, runId: string, variable: string): YearsDocument | null {
  const years = document as Partial<YearsDocument>;
  const usable =
    years.run_id === runId &&
    years.variable === variable &&
    typeof years.units === 'string' &&
    Array.isArray(years.years) &&
    years.years.length > 0 &&
    Array.isArray(years.values) &&
    years.values.length === years.years.length &&
    typeof years.rows === 'number' &&
    typeof years.columns === 'number' &&
    [years.lat_min, years.lat_max, years.lon_min, years.lon_max].every((edge) => typeof edge === 'number');
  return usable ? (document as unknown as YearsDocument) : null;
}

async function fetchYears(runId: string, variable: string): Promise<YearsDocument | null> {
  const file = await fetchDocument(`${base}years/${encodeURIComponent(variable)}.json`, timeoutMs);
  const fromFile = typeof file === 'object' ? read(file, runId, variable) : null;
  if (fromFile) {
    return fromFile;
  }
  const held = await serviceRun();
  if (typeof held === 'string' || held.runId !== runId) {
    return null;
  }
  const reply = await fetchDocument(`${held.address}/years?variable=${encodeURIComponent(variable)}`, timeoutMs);
  return typeof reply === 'object' ? read(reply, runId, variable) : null;
}

/** What has been fetched this session, by run and variable. A miss is not kept, so switching on again asks again. */
const fetched = new Map<string, YearsDocument>();
const fetching = new Map<string, Promise<YearsDocument | null>>();

/**
 * The years of one variable, while `wanted` is true. "loading" until the file or the service
 * has answered, then "ready" or "missing".
 */
export function useYears(runId: string | null, variable: string | null, wanted: boolean): YearsState {
  const key = `${runId}\n${variable}`;
  const [missing, setMissing] = useState<string | null>(null);
  const [, setArrived] = useState(0);

  useEffect(() => {
    if (!wanted) {
      // Switched off: a miss is forgotten, so switching on again looks again.
      setMissing(null);
      return;
    }
    if (variable === null || runId === null || fetched.has(key)) {
      return;
    }
    let current = true;
    setMissing(null);
    let running = fetching.get(key);
    if (!running) {
      running = fetchYears(runId, variable).finally(() => fetching.delete(key));
      fetching.set(key, running);
    }
    void running.then((years) => {
      if (years) {
        fetched.set(key, years);
      }
      if (current) {
        years ? setArrived((count) => count + 1) : setMissing(key);
      }
    });
    return () => {
      current = false;
    };
  }, [wanted, variable, runId, key]);

  if (!wanted || variable === null) {
    return { status: 'off' };
  }
  const years = fetched.get(key);
  if (years) {
    return { status: 'ready', years };
  }
  // Without a run id there is nothing to check a file or the service against.
  return runId === null || missing === key ? { status: 'missing' } : { status: 'loading' };
}
