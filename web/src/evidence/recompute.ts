import type { Finding, ResultsDocument } from '../contract';
import { serviceRun } from '../service';
import { pValue, signed } from './numbers';

// "Recompute": the service runs a trend finding's own cell again from the gridded data
// (POST /analyse with the cell's bounds), and the fresh slope, interval and p-value are set
// beside the stored ones. The viewer compares the two and works out neither. The web twin of
// Recompute in VIGIL/VIGIL/Pages/EvidencePage.cs, with the same words and the same tolerance.

const timeoutMs = 60000;

export interface Recomputed {
  text: string;
  /** "differ" when the fresh figures are not the stored ones, which the person should not miss. */
  outcome: 'same' | 'differ' | 'none';
}

/** Whether a service answered, so there is something to ask. A page with none does not offer the button. */
export async function recomputeOffered(): Promise<boolean> {
  return typeof (await serviceRun()) === 'object';
}

/** Whether two figures agree to within the last digits a round trip through JSON can change. */
function same(a: number, b: number): boolean {
  return Math.abs(a - b) <= 1e-9 * Math.max(1, Math.abs(a), Math.abs(b));
}

/** A p-value as the evidence view writes it. One stored as exactly 0 was too small for the arithmetic, not zero. */
function p(value: number): string {
  return value === 0 ? 'below 1e-16' : pValue(value);
}

const failed = (reason: string): Recomputed => ({
  text: `Recomputing needs the analysis service with its gridded data. ${reason}`,
  outcome: 'none',
});

export async function recompute(finding: Finding): Promise<Recomputed> {
  const stored = finding.result;
  const variable = finding.variables[0]?.id;
  if (!stored || finding.kind !== 'trend' || !variable) {
    return { text: 'Only a trend can be recomputed.', outcome: 'none' };
  }
  const held = await serviceRun();
  if (typeof held !== 'object') {
    return failed(held === 'absent' ? 'This page has no service behind it.' : 'The service did not answer.');
  }
  const { cell } = finding;
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    let reply: Response;
    try {
      reply = await fetch(held.address + '/analyse', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ lat_min: cell.lat_min, lat_max: cell.lat_max, lon_min: cell.lon_min, lon_max: cell.lon_max }),
        signal: controller.signal,
      });
    } catch {
      return failed(controller.signal.aborted ? 'The service took too long to answer.' : 'The service could not be reached.');
    }
    let body: unknown;
    try {
      body = await reply.json();
    } catch {
      return failed('The service sent an answer the viewer could not read.');
    }
    if (!reply.ok) {
      const detail = typeof body === 'object' && body !== null ? (body as { detail?: unknown }).detail : undefined;
      return failed(typeof detail === 'string' ? detail : `The service answered with an error (${reply.status}).`);
    }
    const fresh = body as ResultsDocument;
    // The answer holds every variable's trend in the cell, significant or not: the one to compare is this cell's and this variable's.
    const again = (fresh.findings ?? []).find(
      (candidate) => candidate.kind === 'trend' && candidate.cell?.id === cell.id && candidate.variables?.[0]?.id === variable,
    )?.result;
    if (!again) {
      return { text: 'The service ran the cell again and did not return this finding, so there is nothing to compare.', outcome: 'none' };
    }
    const agrees =
      same(again.slope_per_decade, stored.slope_per_decade) &&
      same(again.ci_low, stored.ci_low) &&
      same(again.ci_high, stored.ci_high) &&
      same(again.p_value, stored.p_value);
    return {
      text:
        `Recomputed from the gridded data just now (run ${fresh.run?.id ?? 'unnamed'}): ${signed(again.slope_per_decade)} ${again.units}, ` +
        `interval ${signed(again.ci_low)} to ${signed(again.ci_high)}, p before correction ${p(again.p_value)}. ` +
        (agrees
          ? 'These are the stored figures, to the last digit the viewer holds.'
          : "These differ from the stored figures above, so the service's data is not the data the stored scan was made from.") +
        ' The p-value after correction is not compared: a run on one cell has no other tests to correct for.',
      outcome: agrees ? 'same' : 'differ',
    };
  } finally {
    clearTimeout(timer);
  }
}
