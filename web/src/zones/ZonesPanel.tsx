import { useEffect, useState } from 'react';
import { count } from '../format';
import { askZone } from './client';
import type { ZoneAnswer } from './client';
import type { Zones } from './useZones';
import { ZoneSummaryView } from './ZoneSummaryView';
import './zones.css';

export interface ZonesPanelProps {
  zones: Zones;
  /** The run on screen, which a zone's figures must belong to. */
  runId: string | null;
  /** How many findings are in the chosen zone's cells. */
  findingsInZone: number;
  /** "Draw a zone" was pressed. The shell brings the globe on screen. */
  onDraw: () => void;
}

/** Answers kept for this visit, by run and zone, so going to a finding and back does not ask again. */
const answers = new Map<string, ZoneAnswer>();

/**
 * Everything about zones beside the globe: the button that starts an outline, the bar that
 * names a new one, the saved zones, and the chosen zone's trends as the service gave them.
 * The web twin of the desktop app's ZonePanel.
 */
export function ZonesPanel({ zones, runId, findingsInZone, onDraw }: ZonesPanelProps) {
  const { chosenZone, chosen, pending } = zones;
  const [name, setName] = useState('');
  const [, setArrived] = useState(0);

  // A new outline starts with the next free name.
  const pendingPoints = pending?.points;
  useEffect(() => {
    if (pendingPoints) {
      setName(zones.defaultName);
    }
    // Only when another outline is finished: the default must not overwrite what is being typed.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pendingPoints]);

  // The chosen zone's trends, asked of the service once the zone is known to be one.
  const answerKey = chosenZone && chosen && !chosen.refusal ? `${runId}\n${chosenZone.id}\n${chosen.cellIds.length}` : null;
  useEffect(() => {
    if (!answerKey || !chosenZone || !chosen || answers.has(answerKey)) {
      return;
    }
    let current = true;
    void askZone(runId, chosenZone.points, chosen.cellIds).then((answer) => {
      answers.set(answerKey, answer);
      if (current) {
        setArrived((arrivals) => arrivals + 1);
      }
    });
    return () => {
      current = false;
      // A refusal is not kept: the service may be there the next time the zone is chosen.
      if (answers.get(answerKey)?.status === 'refused') {
        answers.delete(answerKey);
      }
    };
  }, [answerKey, chosenZone, chosen, runId]);

  const drawing = zones.drawing !== null;

  return (
    <div className="zones">
      <div className="zones-actions">
        <button type="button" className="button" onClick={drawing ? zones.cancelDrawing : onDraw} disabled={!zones.possible}>
          {drawing ? 'Cancel drawing' : 'Draw a zone'}
        </button>
        {chosenZone && (
          <button type="button" className="button" onClick={() => zones.choose(null)}>
            Whole globe
          </button>
        )}
      </div>
      <p className="zones-note">
        {drawing
          ? 'Click or tap the globe to lay each point of the outline. Dragging still turns the globe. Click the first point again, or Finish, to close it. Escape cancels.'
          : 'A zone limits the findings and the layer to an area you draw on the globe.'}
      </p>

      {pending && (
        <form
          className="zones-new"
          onSubmit={(event) => {
            event.preventDefault();
            zones.savePending(name);
          }}
        >
          <h4>New zone</h4>
          {pending.refusal ? (
            <p className="zones-refusal">{pending.refusal} It has not been saved.</p>
          ) : (
            <>
              <p className="zones-note numeric">
                It takes in {pending.cells.length === 1 ? '1 grid cell' : `${count(pending.cells.length)} grid cells`}: those whose centres the
                outline holds. Their stepped edge is drawn beside the outline.
              </p>
              <label className="zones-name">
                <span>Name</span>
                <input type="text" value={name} maxLength={60} onChange={(event) => setName(event.target.value)} />
              </label>
            </>
          )}
          <div className="zones-actions">
            {!pending.refusal && (
              <button type="submit" className="button primary">
                Save zone
              </button>
            )}
            <button type="button" className="button" onClick={zones.discardPending}>
              Discard
            </button>
          </div>
        </form>
      )}

      <h4>Saved zones</h4>
      {zones.notKept && <p className="zones-refusal">This browser would not keep the zones, so they will be gone when the page is closed.</p>}
      {zones.saved.length === 0 ? (
        <p className="zones-note">No zones saved yet.</p>
      ) : (
        <ul className="zones-list">
          {zones.saved.map((zone) => {
            const selected = zone.id === chosenZone?.id;
            return (
              <li key={zone.id} className={selected ? 'zones-item zones-item-selected' : 'zones-item'}>
                <button type="button" className="zones-item-name" aria-pressed={selected} onClick={() => zones.choose(selected ? null : zone.id)}>
                  {zone.name}
                </button>
                <button type="button" className="zones-item-delete" onClick={() => zones.remove(zone.id)} aria-label={`Delete the zone ${zone.name}`}>
                  <svg width="10" height="10" viewBox="0 0 10 10" aria-hidden="true">
                    <path d="M1 1 9 9M9 1 1 9" stroke="currentColor" strokeWidth="1.2" fill="none" />
                  </svg>
                </button>
              </li>
            );
          })}
        </ul>
      )}

      {chosenZone && chosen && (
        <section className="zones-chosen-zone" aria-label={chosenZone.name}>
          <h3>{chosenZone.name}</h3>
          {chosen.refusal ? (
            <p className="zones-refusal">{chosen.refusal}</p>
          ) : (
            <>
              <p className="zones-note numeric">
                {chosen.cells.length === 1 ? '1 grid cell' : `${count(chosen.cells.length)} grid cells`} and{' '}
                {findingsInZone === 1 ? '1 finding' : `${count(findingsInZone)} findings`} on this scan. The findings, the markers and the layer
                show only these cells.
              </p>
              <ZoneSummaryView name={chosenZone.name} answer={answerKey ? answers.get(answerKey) ?? null : null} />
            </>
          )}
        </section>
      )}
    </div>
  );
}
