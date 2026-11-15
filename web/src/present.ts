import { useEffect, useRef, useState } from 'react';
import type { PresentState } from './contract';
import { fetchDocument, service } from './service';

// Following a presenter (the contract's "Presenting"). The viewer asks the service once what
// is on show. If nothing answers in the contract's shape it never asks again, so a website
// with no service behind it stays quiet. If the service answers, the viewer asks again every
// two seconds, and while someone is presenting the run on screen it shows what they show.
// Nothing here changes a result: it only moves this screen.

const PollMilliseconds = 2000;
const TimeoutMilliseconds = 5000;

export type Shown = NonNullable<PresentState['show']>;

export interface Presenter {
  /** Someone is presenting the run on screen, so there is something to follow. */
  presenting: boolean;
  /** Whether this screen follows. On until the person turns it off. */
  following: boolean;
  setFollowing: (following: boolean) => void;
}

/** What the service said, when it is in the contract's shape. */
function read(reply: Record<string, unknown> | 'absent' | 'unreachable'): PresentState | null {
  if (typeof reply !== 'object' || typeof reply.presenting !== 'boolean' || typeof reply.sequence !== 'number') {
    return null;
  }
  return reply as unknown as PresentState;
}

/**
 * Follows a presenter. `apply` is called with what is on show each time it changes, while
 * someone is presenting the run on screen and following is on.
 */
export function usePresenter(runId: string | null, apply: (show: Shown) => void): Presenter {
  const [presenting, setPresenting] = useState(false);
  const [following, setFollowing] = useState(true);
  // The poll reads these as they are now, so a new handler or a flick of the switch does not restart it.
  const applyRef = useRef(apply);
  applyRef.current = apply;
  const followingRef = useRef(following);
  followingRef.current = following;
  // The sequence last applied. Null until something has been, and again after following is switched back on.
  const applied = useRef<number | null>(null);
  const latest = useRef<PresentState | null>(null);

  const act = (state: PresentState, run: string) => {
    latest.current = state;
    const live = state.presenting && state.run_id === run;
    setPresenting(live);
    if (live && state.show && followingRef.current && state.sequence !== applied.current) {
      applied.current = state.sequence;
      applyRef.current(state.show);
    }
  };

  useEffect(() => {
    if (runId === null) {
      return;
    }
    let stopped = false;
    let timer: ReturnType<typeof setInterval> | undefined;
    void (async () => {
      const found = await service();
      if (found.none || stopped) {
        return;
      }
      const address = found.address + '/present';
      const first = read(await fetchDocument(address, TimeoutMilliseconds));
      if (!first || stopped) {
        // No service, or one that does not present: this page never asks again.
        return;
      }
      act(first, runId);
      timer = setInterval(async () => {
        const next = read(await fetchDocument(address, TimeoutMilliseconds));
        if (stopped) {
          return;
        }
        if (next) {
          act(next, runId);
        } else {
          setPresenting(false);
        }
      }, PollMilliseconds);
    })();
    return () => {
      stopped = true;
      clearInterval(timer);
    };
    // `act` only reads refs and sets state.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [runId]);

  const follow = (on: boolean) => {
    setFollowing(on);
    followingRef.current = on;
    if (on) {
      // Catch up with what is on show now, without waiting for the next change.
      applied.current = null;
      const state = latest.current;
      if (state && runId !== null) {
        act(state, runId);
      }
    }
  };

  return { presenting, following, setFollowing: follow };
}
