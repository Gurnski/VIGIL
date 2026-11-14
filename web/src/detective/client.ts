import type { AskFixture, AskRequest, AskResponse } from '../contract';
import { loadAskFixture } from '../data';

// The viewer never calls a language model itself and holds no API key: the service does that.
const timeoutMs = 20000;

const localService = 'http://127.0.0.1:8000';

/**
 * With no address built in, the service is taken to be on the machine serving the page: the
 * local one while developing, and otherwise the same address the page came from, which needs
 * no configuration and no cross-origin permission when one server hosts both.
 */
function defaultAddress(): string {
  const host = window.location.hostname;
  const local = host === '' || host === 'localhost' || host === '127.0.0.1';
  return local ? localService : window.location.origin;
}

/** The address of the analysis service, without a trailing slash. */
export const serviceAddress: string = (
  (import.meta.env.VITE_VIGIL_API_URL as string | undefined) || defaultAddress()
).replace(/\/+$/, '');

/** The service could not give an answer. The message says why, in words fit to show. */
export class DetectiveUnavailableError extends Error {}

/** Calls POST /ask. Throws DetectiveUnavailableError whenever no usable answer comes back. */
export async function ask(request: AskRequest): Promise<AskResponse> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    let reply: Response;
    try {
      reply = await fetch(serviceAddress + '/ask', {
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
    return { ...response, citations: response.citations ?? [], limitations: response.limitations ?? [] };
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
