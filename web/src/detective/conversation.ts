import { useCallback, useRef, useState } from 'react';
import type { AskResponse, AskTurn, Finding } from '../contract';
import { labelText, placeText } from '../format';
import { DetectiveUnavailableError, ask, sampleAnswerTo, serviceAddress } from './client';

/** Where an answer came from. A sample is a canned fixture answer and must always be marked. */
export type AnswerSource = 'service' | 'sample' | 'not_connected';

export interface UserMessage {
  role: 'user';
  text: string;
  /** What the attached finding was, as shown on its chip. */
  about?: string;
}

export interface AssistantMessage {
  role: 'assistant';
  text: string;
  source: AnswerSource;
  status?: AskResponse['status'];
  citations: AskResponse['citations'];
  limitations: string[];
}

export type ChatMessage = UserMessage | AssistantMessage;

/** The chip text for an attached finding: "Trend, 61.3°N 101.3°E". */
export function attachedText(finding: Finding): string {
  return `${labelText(finding.kind)}, ${placeText(finding.cell)}`;
}

/**
 * Earlier turns to send with the next question: only questions the service answered, each
 * with that answer. Failed questions are left out, and so are sample answers, because their
 * numbers are made up and must not reach the real service as if it had said them.
 */
function historyOf(messages: ChatMessage[]): AskTurn[] {
  const turns: AskTurn[] = [];
  for (let i = 0; i + 1 < messages.length; i++) {
    const question = messages[i];
    const answer = messages[i + 1];
    if (question.role === 'user' && answer.role === 'assistant' && answer.source === 'service') {
      turns.push({ role: 'user', text: question.text }, { role: 'assistant', text: answer.text });
      i++;
    }
  }
  return turns;
}

function fromResponse(response: AskResponse, source: AnswerSource): AssistantMessage {
  return {
    role: 'assistant',
    text: response.answer,
    source,
    status: response.status,
    citations: response.citations ?? [],
    limitations: response.limitations ?? [],
  };
}

/**
 * One conversation with the Detective. All of its state is here, so it can be lifted out
 * of the panel later without touching the view.
 */
export function useConversation() {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [waiting, setWaiting] = useState(false);
  // Refs as well as state, so two sends in one tick can't both get through.
  const messagesRef = useRef<ChatMessage[]>([]);
  const waitingRef = useRef(false);

  const append = (message: ChatMessage) => {
    messagesRef.current = [...messagesRef.current, message];
    setMessages(messagesRef.current);
  };

  /** Asks a question. Returns false if it was empty or an answer is still on its way. */
  const send = useCallback((text: string, about: Finding | null): boolean => {
    const question = text.trim();
    if (question.length === 0 || waitingRef.current) {
      return false;
    }

    const history = historyOf(messagesRef.current);
    append({ role: 'user', text: question, about: about ? attachedText(about) : undefined });
    waitingRef.current = true;
    setWaiting(true);

    void (async () => {
      let answer: AssistantMessage;
      try {
        answer = fromResponse(
          await ask({ question, history, ...(about ? { finding_id: about.id } : {}) }),
          'service',
        );
      } catch (error) {
        const reason =
          error instanceof DetectiveUnavailableError ? error.message : 'Something went wrong asking the service.';
        const sample = await sampleAnswerTo(question).catch(() => null);
        answer = sample
          ? fromResponse(sample, 'sample')
          : {
              role: 'assistant',
              source: 'not_connected',
              text: `${reason} The Detective answers from the analysis service at ${serviceAddress}, so there is no answer to show. The example questions still work, with sample answers.`,
              citations: [],
              limitations: [],
            };
      }
      append(answer);
      waitingRef.current = false;
      setWaiting(false);
    })();
    return true;
  }, []);

  return { messages, waiting, send };
}
