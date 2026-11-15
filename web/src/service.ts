// Which analysis service this page talks to. Worked out once, when it is first needed.
//
// A page served by the service itself must use that service, whatever its port. So the page's
// own address is asked first: GET /health, and a JSON reply saying status "ok" means it is the
// service. A plain web server or a bucket answers with a 404 or a page of HTML, which counts as
// no. Then the older rules apply: the address built in, then port 8000 on this machine while
// developing, then the page's own address.

export interface Service {
  /** The service's address, without a trailing slash. */
  address: string;
  /** True when the page's own address answered as the service. */
  own: boolean;
  /** The run the service said it holds, when the page's own address answered. */
  runId: string | null;
  /**
   * True when nothing suggests a service at all: the page's own address said no and there is
   * no other address to try. Calls that would only be made on the off-chance are skipped.
   */
  none: boolean;
}

const localService = 'http://127.0.0.1:8000';
const probeTimeoutMs = 4000;

function trimmed(address: string): string {
  return address.replace(/\/+$/, '');
}

/** Asks the page's own address whether it is the service. */
async function probeOwnOrigin(): Promise<{ runId: string | null } | null> {
  if (!/^https?:$/.test(window.location.protocol)) {
    return null;
  }
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), probeTimeoutMs);
  try {
    const reply = await fetch(window.location.origin + '/health', { signal: controller.signal });
    // The body decides, not the status: some hosts answer a missing path with 200 and a page of their own.
    const body: unknown = await reply.json();
    if (typeof body === 'object' && body !== null && (body as { status?: unknown }).status === 'ok') {
      const runId = (body as { run_id?: unknown }).run_id;
      return { runId: typeof runId === 'string' ? runId : null };
    }
    return null;
  } catch {
    return null;
  } finally {
    clearTimeout(timer);
  }
}

async function find(): Promise<Service> {
  const own = await probeOwnOrigin();
  if (own) {
    return { address: trimmed(window.location.origin), own: true, runId: own.runId, none: false };
  }
  const built = import.meta.env.VITE_VIGIL_API_URL as string | undefined;
  if (built) {
    return { address: trimmed(built), own: false, runId: null, none: false };
  }
  const host = window.location.hostname;
  if (host === '' || host === 'localhost' || host === '127.0.0.1') {
    return { address: localService, own: false, runId: null, none: false };
  }
  // The page's own address is all that is left, and it has just said it is not the service.
  return { address: trimmed(window.location.origin), own: false, runId: null, none: true };
}

let found: Promise<Service> | null = null;

/** The service this page uses. The page's own address is asked once and the answer kept. */
export function service(): Promise<Service> {
  found ??= find();
  return found;
}

// ----- Small results documents: series, years, what a presenter shows -----

/** A document, a firm "nothing here", or no answer at all. */
export type Fetched = Record<string, unknown> | 'absent' | 'unreachable';

/**
 * Fetches a small JSON document. A reply that is an error, or is not JSON (a host's own page
 * for a missing path), is "absent". No reply in time, or a server in trouble, is "unreachable".
 */
export async function fetchDocument(url: string, timeoutMs: number): Promise<Fetched> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    let reply: Response;
    try {
      reply = await fetch(url, { signal: controller.signal });
    } catch {
      return 'unreachable';
    }
    if (!reply.ok) {
      // 404 and its kin say there is nothing at the address. A server in trouble may recover.
      return reply.status >= 500 ? 'unreachable' : 'absent';
    }
    try {
      const body: unknown = await reply.json();
      return typeof body === 'object' && body !== null && !Array.isArray(body) ? (body as Record<string, unknown>) : 'absent';
    } catch {
      return controller.signal.aborted ? 'unreachable' : 'absent';
    }
  } finally {
    clearTimeout(timer);
  }
}

const healthTimeoutMs = 8000;
/** A service that did not reply is asked again after this long. */
const retryAfterMs = 30000;

let heldRun: Promise<{ address: string; runId: string | null } | 'absent' | 'unreachable'> | null = null;

/**
 * The service and the run it holds, for calls whose replies do not name their run. "absent"
 * when there is no service, "unreachable" when it did not reply. A page served by the service
 * already knows from the first question it asked. Any other page asks GET /health, once.
 */
export function serviceRun(): Promise<{ address: string; runId: string | null } | 'absent' | 'unreachable'> {
  heldRun ??= (async () => {
    const found = await service();
    if (found.none) {
      return 'absent' as const;
    }
    if (found.own) {
      return { address: found.address, runId: found.runId };
    }
    const health = await fetchDocument(found.address + '/health', healthTimeoutMs);
    if (health === 'unreachable') {
      // Nothing listening now. Ask again later, in case the service is started.
      setTimeout(() => (heldRun = null), retryAfterMs);
      return 'unreachable' as const;
    }
    if (health === 'absent' || health.status !== 'ok') {
      return 'absent' as const;
    }
    return { address: found.address, runId: typeof health.run_id === 'string' ? health.run_id : null };
  })();
  return heldRun;
}
