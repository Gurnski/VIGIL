import { useEffect, useId, useRef, useState } from 'react';
import type { KeyboardEvent } from 'react';
import type { Finding } from '../contract';
import { exampleQuestions } from './client';
import { attachedText, useConversation } from './conversation';
import type { AssistantMessage } from './conversation';
import './detective.css';

export interface DetectiveChatProps {
  /** The findings in the results, for naming citations. */
  findings: Finding[];
  /** The finding attached to the next question, or null. */
  attached: Finding | null;
  onDetach: () => void;
  /** A citation in an answer was clicked. */
  onCitationClick: (findingId: string) => void;
  onClose: () => void;
}

function Cross() {
  return (
    <svg width="10" height="10" viewBox="0 0 10 10" aria-hidden="true">
      <path d="M1 1 9 9M9 1 1 9" stroke="currentColor" strokeWidth="1.2" fill="none" />
    </svg>
  );
}

// The line above an answer when it is anything other than a plain answer.
function statusNote(message: AssistantMessage): string | null {
  if (message.source === 'not_connected') return 'No answer';
  if (message.status === 'inconclusive') return "The results can't settle this";
  if (message.status === 'out_of_scope') return 'Outside what the scan covers';
  return null;
}

interface AnswerProps {
  message: AssistantMessage;
  findings: Finding[];
  onCitationClick: (findingId: string) => void;
}

function Answer({ message, findings, onCitationClick }: AnswerProps) {
  const note = statusNote(message);
  return (
    <div className="detective-answer">
      {message.source === 'sample' && (
        <div>
          <span className="badge warning">Sample answer</span>
        </div>
      )}
      {note && <p className="detective-status">{note}</p>}
      <p className={message.source === 'not_connected' ? 'detective-text detective-text-quiet' : 'detective-text'}>
        {message.text}
      </p>

      {message.citations.length > 0 && (
        <div className="detective-section">
          <p className="detective-caption">Based on</p>
          <ul className="detective-citations">
            {message.citations.map((citation, index) => {
              const finding = findings.find((item) => item.id === citation.finding_id);
              return (
                <li key={index}>
                  <button
                    type="button"
                    className="detective-citation"
                    onClick={() => onCitationClick(citation.finding_id)}
                  >
                    {finding ? (
                      <span className="detective-citation-name">
                        <span className="numeric detective-citation-rank">#{finding.rank}</span>
                        <span>{finding.summary}</span>
                      </span>
                    ) : (
                      <span className="detective-citation-name numeric">{citation.finding_id}</span>
                    )}
                    {citation.supports && <span className="detective-citation-supports">{citation.supports}</span>}
                  </button>
                </li>
              );
            })}
          </ul>
        </div>
      )}

      {message.limitations.length > 0 && (
        <div className="detective-section">
          <p className="detective-caption">Keep in mind</p>
          <ul className="detective-limitations">
            {message.limitations.map((limitation, index) => (
              <li key={index}>{limitation}</li>
            ))}
          </ul>
        </div>
      )}

      {message.source === 'sample' && (
        <p className="detective-sample-note">
          Sample answer from made-up results. The Detective service isn't connected.
        </p>
      )}
    </div>
  );
}

/** The chat with the AI Detective: the web twin of the desktop app's chat panel. */
export function DetectiveChat({ findings, attached, onDetach, onCitationClick, onClose }: DetectiveChatProps) {
  const { messages, waiting, send } = useConversation();
  const [draft, setDraft] = useState('');
  const [suggestions, setSuggestions] = useState<string[]>([]);
  const transcript = useRef<HTMLDivElement>(null);
  const input = useRef<HTMLTextAreaElement>(null);
  const inputId = useId();

  useEffect(() => {
    let current = true;
    void exampleQuestions().then((questions) => {
      if (current) setSuggestions(questions);
    });
    return () => {
      current = false;
    };
  }, []);

  // Keep the newest message in view.
  useEffect(() => {
    const element = transcript.current;
    if (element) element.scrollTop = element.scrollHeight;
  }, [messages, waiting]);

  // Only the finding is attached: the question itself is always the user's own words.
  const submit = (text: string) => {
    if (!send(text, attached)) return;
    if (attached) onDetach();
    setDraft('');
    input.current?.focus();
  };

  const onKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => {
    if (event.key === 'Enter' && !event.shiftKey && !event.nativeEvent.isComposing) {
      event.preventDefault();
      submit(draft);
    }
  };

  return (
    <div className="detective">
      <header className="detective-header">
        <h2 className="detective-title">Detective</h2>
        <button type="button" className="detective-icon-button" onClick={onClose} aria-label="Close the Detective">
          <Cross />
        </button>
      </header>

      <div className="detective-transcript" ref={transcript} aria-live="polite" aria-busy={waiting}>
        {messages.length === 0 && (
          <div className="detective-intro">
            <h3 className="detective-intro-heading">Ask about this scan</h3>
            <p className="detective-intro-text">
              The Detective answers only from the scan's findings and names the ones it used. When the results can't
              settle a question, it says so.
            </p>
            {suggestions.length > 0 && (
              <>
                <p className="detective-caption detective-intro-caption">Try one of these</p>
                <ul className="detective-suggestions">
                  {suggestions.map((question) => (
                    <li key={question}>
                      <button
                        type="button"
                        className="detective-suggestion"
                        disabled={waiting}
                        onClick={() => submit(question)}
                      >
                        {question}
                      </button>
                    </li>
                  ))}
                </ul>
              </>
            )}
          </div>
        )}

        {messages.map((message, index) =>
          message.role === 'user' ? (
            <div key={index} className="detective-question">
              {message.about && <p className="detective-question-about">About: {message.about}</p>}
              <p className="detective-text">{message.text}</p>
            </div>
          ) : (
            <Answer key={index} message={message} findings={findings} onCitationClick={onCitationClick} />
          ),
        )}

        {waiting && <p className="detective-waiting">Thinking…</p>}
      </div>

      <form
        className="detective-composer"
        onSubmit={(event) => {
          event.preventDefault();
          submit(draft);
        }}
      >
        {attached && (
          <div className="detective-chip">
            <span className="detective-chip-label">Asking about</span>
            <span className="detective-chip-text">{attachedText(attached)}</span>
            <button
              type="button"
              className="detective-icon-button detective-chip-remove"
              onClick={onDetach}
              aria-label="Remove the attached finding"
            >
              <Cross />
            </button>
          </div>
        )}
        <label className="detective-label" htmlFor={inputId}>
          Your question
        </label>
        <div className="detective-input-row">
          <textarea
            id={inputId}
            ref={input}
            className="detective-input"
            rows={2}
            placeholder="Ask about the findings"
            value={draft}
            onChange={(event) => setDraft(event.target.value)}
            onKeyDown={onKeyDown}
          />
          <button type="submit" className="button primary" disabled={waiting || draft.trim().length === 0}>
            Send
          </button>
        </div>
      </form>
    </div>
  );
}
