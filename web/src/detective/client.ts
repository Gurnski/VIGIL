import type { AskFixture, AskRequest, AskResponse, AskStep, AskStepBox } from '../contract';
import { loadAskFixture } from '../data';
import { service } from '../service';

// The viewer never calls a language model itself and holds no API key: the service does that.
// The Detective may look things up over several rounds: the service allows it 75 seconds, and the desktop app waits 90.
const timeoutMs = 90000;

/** The service could not give an answer. The message says why, in words fit to show. */
export class DetectiveUnavailableError extends Error {}

function isNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value);
}

/** A step's box, kept only when it is one the globe can draw: four edges, in range and the right way round. */
function readBox(value: unknown): AskStepBox | undefined {
  if (typeof value !== 'object' || value === null) {
    return undefined;
  }
  const { lat_min, lat_max, lon_min, lon_max } = value as Record<string, unknown>;
  if (!isNumber(lat_min) || !isNumber(lat_max) || !isNumber(lon_min) || !isNumber(lon_max)) {
    return undefined;
  }
  if (!(-90 <= lat_min && lat_min < lat_max && lat_max <= 90 && -180 <= lon_min && lon_min < lon_max && lon_max <= 180)) {
    return undefined;
  }
  return { lat_min, lat_max, lon_min, lon_max };
}

/**
 * The steps of an answer, read defensively: a step that is not in the contract's shape is
 * dropped, never the answer. Undefined when there are none to show.
 */
function readSteps(value: unknown): AskStep[] | undefined {
  if (!Array.isArray(value)) {
    return undefined;
  }
  const steps: AskStep[] = [];
  for (const item of value) {
    if (typeof item !== 'object' || item === null) {
      continue;
    }
    const raw = item as Record<string, unknown>;
    if (typeof raw.summary !== 'string' || raw.summary.length === 0) {
      continue;
    }
    const step: AskStep = {
      tool: typeof raw.tool === 'string' ? raw.tool : '',
      input: typeof raw.input === 'object' && raw.input !== null ? (raw.input as Record<string, unknown>) : {},
      summary: raw.summary,
      finding_ids: Array.isArray(raw.finding_ids) ? raw.finding_ids.filter((id): id is string => typeof id === 'string') : [],
    };
    if (isNumber(raw.matching)) {
      step.matching = raw.matching;
    }
    const box = readBox(raw.box);
    if (box) {
      step.box = box;
    }
    if (raw.failed === true) {
      step.failed = true;
    }
    steps.push(step);
  }
  return steps.length > 0 ? steps : undefined;
}

/** Calls POST /ask. Throws DetectiveUnavailableError whenever no usable answer comes back. */
export async function ask(request: AskRequest): Promise<AskResponse> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    let reply: Response;
    try {
      // Which service is worked out once for the whole viewer: see service.ts.
      const { address } = await service();
      reply = await fetch(address + '/ask', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(request),
        signal: controller.signal,
      });
    } catch {
      throw new DetectiveUnavailableError(
        controller.signal.aborted ? 'The service took too long to answer.' : 'The service could not be reached.',
      );
    }
    if (!reply.ok) {
      throw new DetectiveUnavailableError(`The service answered with an error (${reply.status}).`);
    }

    let response: AskResponse | null;
    try {
      response = (await reply.json()) as AskResponse | null;
    } catch {
      // A reply that isn't the contract's JSON, such as an HTML error page from a gateway.
      throw new DetectiveUnavailableError(
        controller.signal.aborted ? 'The service took too long to answer.' : 'The service sent an answer the app could not read.',
      );
    }
    if (!response || typeof response.answer !== 'string' || response.answer.length === 0 || !response.status) {
      throw new DetectiveUnavailableError('The service sent an answer the app could not read.');
    }
    return {
      ...response,
      citations: response.citations ?? [],
      limitations: response.limitations ?? [],
      steps: readSteps(response.steps),
    };
  } finally {
    clearTimeout(timer);
  }
}

let fixture: Promise<AskFixture | null> | null = null;

/** The fixture's examples, loaded once. Null when the file can't be loaded. */
function examples(): Promise<AskFixture | null> {
  fixture ??= loadAskFixture().catch(() => {
    fixture = null; // try again next time
    return null;
  });
  return fixture;
}

/** The fixture's example questions, in the order they are written. */
export async function exampleQuestions(): Promise<string[]> {
  const loaded = await examples();
  return loaded?.examples.map((example) => example.request.question) ?? [];
}

// The same matching as the desktop app's Fixture.Normalise.
function normalise(text: string): string {
  return text.trim().replace(/[?.!]+$/, '').toLowerCase();
}

/** The canned answer to an example question, or null if the question is not one of them. */
export async function sampleAnswerTo(question: string): Promise<AskResponse | null> {
  const loaded = await examples();
  const asked = normalise(question);
  return loaded?.examples.find((example) => normalise(example.request.question) === asked)?.response ?? null;
}
