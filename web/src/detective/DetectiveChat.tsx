import { useEffect, useId, useRef, useState } from 'react';
import type { KeyboardEvent } from 'react';
import type { AskStep, Finding } from '../contract';
import { count } from '../format';
import { exampleQuestions } from './client';
import { attachedText, useConversation } from './conversation';
import type { AssistantMessage } from './conversation';
import './detective.css';

export interface DetectiveChatProps {
  /** The findings in the results, for naming citations. */
  findings: Finding[];
  /** The run on screen. An answer drawn from another run is not matched to these findings. */
  runId: string | null;
  /** Whether the results on screen are the made-up fixture. Only then are its example questions and sample answers offered. */
  synthetic: boolean;
  /** The finding attached to the next question, or null. */
  attached: Finding | null;
  onDetach: () => void;
  /** A citation in an answer was clicked. */
  onCitationClick: (findingId: string) => void;
  /**
   * An answer has just arrived, citing these findings. Called once per answer, with an empty
   * list when it cites nothing that can be matched. `sample` marks a canned fixture answer.
   */
  onAnswerCited: (findingIds: string[], sample: boolean) => void;
  /** "Show on globe" was clicked on a citation. */
  onShowCitation: (findingId: string, sample: boolean) => void;
  /** A lookup step was clicked: show where the Detective looked, or what it read there. */
  onShowStep: (step: AskStep) => void;
  onClose: () => void;
}

/**
 * Questions offered while a finding is attached. Each is sent exactly as written, with the
 * finding attached, when the user clicks it: nothing is added to a question out of sight.
 */
const followUpQuestions = ['What else is changing in this cell?', 'Is anything linked to this finding?', 'How sure is this?'];

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

/**
 * Whether an answer's finding ids can be matched to the findings on screen. They cannot when
 * the answer names another run, as a sample answer does beside a real scan: the same id may
 * exist in both and mean different results.
 */
function fromRunOnScreen(message: AssistantMessage, runId: string | null): boolean {
  return message.runId === undefined || runId === null || message.runId === runId;
}

/** "3 found", or "10 of 57 shown" when the search matched more than it returned. */
function foundText(step: AskStep): string | null {
  if (step.failed || step.tool === 'get_run_summary') {
    return null;
  }
  const found = step.finding_ids.length;
  if (step.matching !== undefined && step.matching > found) {
    return `${count(found)} of ${count(step.matching)} shown`;
  }
  return found === 0 ? 'None found' : `${count(found)} found`;
}

interface StepsProps {
  steps: AskStep[];
  findings: Finding[];
  /** False when the answer is from another run: the steps are listed but cannot be drawn. */
  drawable: boolean;
  onShowStep: (step: AskStep) => void;
}

/** What the Detective looked up on the way to an answer. Closed until asked for. */
function Steps({ steps, findings, drawable, onShowStep }: StepsProps) {
  return (
    <details className="detective-steps">
      <summary>
        How it looked this up: {steps.length} {steps.length === 1 ? 'lookup' : 'lookups'}
      </summary>
      <p className="detective-steps-note">
        The Detective read the findings counted here on the way to its answer. Only those under "Based on" back what
        it said.
      </p>
      <ol className="detective-step-list">
        {steps.map((step, index) => {
          const found = foundText(step);
          const known = step.finding_ids.some((id) => findings.some((finding) => finding.id === id));
          const action = !drawable ? null : step.box ? 'Show where it looked' : known ? 'Show on the globe' : null;
          const body = (
            <>
              <span className="detective-step-summary">{step.summary}</span>
              <span className="detective-step-detail">
                {step.failed && <span className="badge">Failed</span>}
                {found && <span className="numeric">{found}</span>}
                {action && <span className="detective-step-action">{action}</span>}
              </span>
            </>
          );
          return (
            <li key={index}>
              {action ? (
                <button type="button" className="detective-step detective-step-button" onClick={() => onShowStep(step)}>
                  {body}
                </button>
              ) : (
                <div className="detective-step">{body}</div>
              )}
            </li>
          );
        })}
      </ol>
    </details>
  );
}

interface AnswerProps {
  message: AssistantMessage;
  findings: Finding[];
  runId: string | null;
  onCitationClick: (findingId: string) => void;
  onShowCitation: (findingId: string, sample: boolean) => void;
  onShowStep: (step: AskStep) => void;
}

function Answer({ message, findings, runId, onCitationClick, onShowCitation, onShowStep }: AnswerProps) {
  const note = statusNote(message);
  const matched = fromRunOnScreen(message, runId);
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
              const finding = matched ? findings.find((item) => item.id === citation.finding_id) : undefined;
              return (
                <li key={index} className="detective-citation-row">
                  <button
                    type="button"
                    className="detective-citation"
                    disabled={!finding}
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
                  {finding && (
                    <button type="button" className="detective-show" onClick={() => onShowCitation(citation.finding_id, message.source === 'sample')}>
                      Show on globe
                    </button>
                  )}
                </li>
              );
            })}
          </ul>
          {!matched && (
            <p className="detective-steps-note">
              This answer was drawn from another run ({message.runId}), not the one on screen, so its findings are
              not matched to the list or the globe.
            </p>
          )}
        </div>
      )}

      {message.steps && message.steps.length > 0 && (
        <Steps steps={message.steps} findings={findings} drawable={matched} onShowStep={onShowStep} />
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

/**
 * The questions on offer over a real scan, the same as the desktop app's. The fixture's own
 * examples name made-up cells. The last one shows the Detective declining.
 */
const realScanQuestions = [
  'Where is it warming fastest?',
  'Where is rainfall falling fastest, and how sure is that?',
  'What is the top-ranked finding, and how sure is it?',
  'What will the weather be like in Plymouth tomorrow?',
];

/** The chat with the AI Detective: the web twin of the desktop app's chat panel. */
export function DetectiveChat({
  findings,
  runId,
  synthetic,
  attached,
  onDetach,
  onCitationClick,
  onAnswerCited,
  onShowCitation,
  onShowStep,
  onClose,
}: DetectiveChatProps) {
  const { messages, waiting, send } = useConversation(synthetic);
  const [draft, setDraft] = useState('');
  const [suggestions, setSuggestions] = useState<string[]>([]);
  const transcript = useRef<HTMLDivElement>(null);
  const input = useRef<HTMLTextAreaElement>(null);
  const inputId = useId();

  useEffect(() => {
    if (!synthetic) {
      setSuggestions(realScanQuestions);
      return undefined;
    }
    let current = true;
    void exampleQuestions().then((questions) => {
      if (current) setSuggestions(questions);
    });
    return () => {
      current = false;
    };
  }, [synthetic]);

  // Keep the newest message in view.
  useEffect(() => {
    const element = transcript.current;
    if (element) element.scrollTop = element.scrollHeight;
  }, [messages, waiting]);

  // When an answer arrives, its cited findings go on the globe: once, as it arrives, and never
  // again for that answer, so the view is not taken back from a user who has moved on. An answer
  // that cites nothing is announced too, so an earlier answer's marks do not stay up under it.
  const answersSeen = useRef(0);
  const announce = useRef(onAnswerCited);
  announce.current = onAnswerCited;
  const screenRun = useRef(runId);
  screenRun.current = runId;
  useEffect(() => {
    if (messages.length <= answersSeen.current) {
      return;
    }
    answersSeen.current = messages.length;
    const newest = messages[messages.length - 1];
    if (newest.role === 'assistant') {
      announce.current(
        fromRunOnScreen(newest, screenRun.current) ? newest.citations.map((citation) => citation.finding_id) : [],
        newest.source === 'sample',
      );
    }
  }, [messages]);

  // Only the finding is attached: the question itself is always words the user chose.
  const submit = (text: string) => {
    if (!send(text, attached)) return;
    if (attached) onDetach();
    // A suggested question leaves a half-written one of the user's own in the box.
    if (text === draft) setDraft('');
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
            <Answer
              key={index}
              message={message}
              findings={findings}
              runId={runId}
              onCitationClick={onCitationClick}
              onShowCitation={onShowCitation}
              onShowStep={onShowStep}
            />
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
          <>
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
            <div className="detective-followups" role="group" aria-label="Questions to ask about this finding">
              {followUpQuestions.map((question) => (
                <button
                  key={question}
                  type="button"
                  className="detective-followup"
                  disabled={waiting}
                  onClick={() => submit(question)}
                >
                  {question}
                </button>
              ))}
            </div>
          </>
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
