import { orbitText, satelliteCaveat } from './satellite';
import type { Satellite } from './satellite';

export interface SatelliteCardProps {
  satellite: Satellite;
  onClose: () => void;
}

/**
 * A small panel about one satellite, shown over the globe when it is clicked: its picture,
 * whether its data is in the scan, a few facts and the note on what the dot does and does
 * not show. The web twin of the desktop app's SatelliteCard control.
 */
export function SatelliteCard({ satellite, onClose }: SatelliteCardProps) {
  return (
    <aside className="globe-satellite-card" aria-label={`About ${satellite.label}`}>
      <button type="button" className="globe-satellite-card-close" onClick={onClose} aria-label="Close">
        <svg width="10" height="10" viewBox="0 0 10 10" aria-hidden="true">
          <path d="M1 1 9 9M9 1 1 9" stroke="currentColor" strokeWidth="1.2" fill="none" />
        </svg>
      </button>
      <img src={satellite.picture} alt={`${satellite.label} spacecraft`} />
      <h3>{satellite.label}</h3>
      <p className={satellite.inScan ? 'globe-satellite-card-status in-scan' : 'globe-satellite-card-status'}>
        {satellite.inScan ? 'Its data is in the first scan' : 'Not in the scan: behind a source kept for cross-checks'}
      </p>
      <dl>
        <div>
          <dt>For VIGIL</dt>
          <dd>{satellite.detail}</dd>
        </div>
        <div>
          <dt>Run by</dt>
          <dd>{satellite.operator}</dd>
        </div>
        <div>
          <dt>Launched</dt>
          <dd>{satellite.launched}</dd>
        </div>
        <div>
          <dt>Carries</dt>
          <dd>{satellite.instrument}</dd>
        </div>
        <div>
          <dt>Orbit</dt>
          <dd>{orbitText(satellite)}</dd>
        </div>
      </dl>
      <p className="globe-satellite-card-note">{satelliteCaveat} Picture: NASA.</p>
    </aside>
  );
}
