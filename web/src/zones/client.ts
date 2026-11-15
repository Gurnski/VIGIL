import type { ZoneAnalysis } from '../contract';
import { serviceRun } from '../service';
import type { ZonePoint } from './geometry';

// A zone's series and trends come from POST /zone on the Python service and from nowhere else:
// the viewer works none of it out. When the service cannot answer, or what it answers does not
// belong beside the scan and the cells on screen, the caller gets the reason and no figures.
// A zone's figures beside the wrong scan would be wrong without looking wrong.

export type ZoneAnswer = { status: 'ready'; analysis: ZoneAnalysis } | { status: 'refused'; reason: string };

const timeoutMs = 60000;

const refused = (reason: string): ZoneAnswer => ({ status: 'refused', reason });

/**
 * Asks the service about a zone. `cellIds` are the cells the viewer takes the zone to be: an
 * answer about any other set of cells is refused, because the outline on screen would not be
 * the area the figures cover.
 */
export async function askZone(runId: string | null, points: ZonePoint[], cellIds: string[]): Promise<ZoneAnswer> {
  if (runId === null) {
    return refused('These results name no run, so there is nothing to check the service against.');
  }
  const held = await serviceRun();
  if (held === 'absent') {
    return refused("A zone's trends are worked out by the analysis service, and this page has none behind it. The zone still limits the findings and the layer.");
  }
  if (held === 'unreachable') {
    return refused("A zone's trends are worked out by the analysis service, which did not answer. The zone still limits the findings and the layer.");
  }
  if (held.runId !== runId) {
    return refused(`The analysis service holds another scan${held.runId ? ` (${held.runId})` : ''}. Its figures do not belong beside this one, so none are shown.`);
  }

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    let reply: Response;
    try {
      reply = await fetch(held.address + '/zone', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ polygon: points }),
        signal: controller.signal,
      });
    } catch {
      return refused(controller.signal.aborted ? 'The analysis service took too long to answer.' : 'The analysis service could not be reached.');
    }
    let body: unknown;
    try {
      body = await reply.json();
    } catch {
      return refused('The analysis service sent an answer the viewer could not read.');
    }
    if (!reply.ok) {
      const detail = typeof body === 'object' && body !== null ? (body as { detail?: unknown }).detail : undefined;
      if (reply.status === 503) {
        return refused('The analysis service does not hold the gridded data a zone needs, so it cannot work out the trends.');
      }
      return refused(typeof detail === 'string' ? `The analysis service would not take the zone. ${detail}` : `The analysis service answered with an error (${reply.status}).`);
    }
    const analysis = body as ZoneAnalysis | null;
    if (!analysis || typeof analysis !== 'object' || !analysis.zone || !Array.isArray(analysis.zone.cells) || !Array.isArray(analysis.summaries)) {
      return refused('The analysis service sent an answer the viewer could not read.');
    }
    if (analysis.run_id !== runId) {
      return refused(`The analysis service answered for another scan (${String(analysis.run_id)}). Its figures do not belong beside this one, so none are shown.`);
    }
    const theirs = new Set(analysis.zone.cells);
    if (theirs.size !== cellIds.length || cellIds.some((id) => !theirs.has(id))) {
      return refused('The analysis service took the zone to be different grid cells from the ones outlined here, so its figures are not shown.');
    }
    return { status: 'ready', analysis };
  } finally {
    clearTimeout(timer);
  }
}
