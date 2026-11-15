import { useEffect, useRef } from 'react';

export interface YearBarProps {
  /** The calendar years, in order. */
  years: number[];
  /** The place of the chosen year in `years`. */
  index: number;
  onIndex: (index: number) => void;
  playing: boolean;
  onPlaying: (playing: boolean) => void;
}

/** Playing shows a new year this often, as the desktop app's YearBar does. */
const StepMilliseconds = 650;

/**
 * Chooses the year the layer shows: a play button, the chosen year, and a track with a mark
 * per year. The web twin of the desktop app's YearBar. The track is a range input, so it can
 * be dragged, clicked, moved with the arrow keys and read by a screen reader. Playing steps
 * through the years and stops at the last.
 */
export function YearBar({ years, index, onIndex, playing, onPlaying }: YearBarProps) {
  const last = years.length - 1;
  // The timer reads the newest place without being restarted by every step.
  const place = useRef(index);
  place.current = index;

  useEffect(() => {
    if (!playing) {
      return;
    }
    const timer = setInterval(() => {
      if (place.current >= last) {
        onPlaying(false);
        return;
      }
      onIndex(place.current + 1);
      if (place.current + 1 >= last) {
        onPlaying(false);
      }
    }, StepMilliseconds);
    return () => clearInterval(timer);
  }, [playing, last, onIndex, onPlaying]);

  const toggle = () => {
    if (!playing && index >= last) {
      // Played to the end: start again from the first year.
      onIndex(0);
    }
    onPlaying(!playing && years.length > 1);
  };

  // The first and last year are named under the track, and each fifth year between where there is room.
  const labelled = (i: number) => i === 0 || i === last || (years[i] % 5 === 0 && i > 1 && i < last - 1);

  return (
    <div className="year-bar">
      <button type="button" className="year-bar-play" onClick={toggle} aria-label={playing ? 'Pause' : 'Play through the years'}>
        <svg width="12" height="12" viewBox="0 0 12 12" aria-hidden="true">
          {playing ? <path d="M2 1h3v10H2zM7 1h3v10H7z" fill="currentColor" /> : <path d="M2.5 1 11 6 2.5 11z" fill="currentColor" />}
        </svg>
      </button>
      <span className="year-bar-year numeric" aria-hidden="true">
        {years[index]}
      </span>
      <div className="year-bar-track">
        <div className="year-bar-marks" aria-hidden="true">
          {years.map((year, i) => (
            <span
              key={year}
              className={
                'year-bar-mark' + (year % 5 === 0 ? ' year-bar-mark-fifth' : '') + (i <= index ? ' year-bar-mark-passed' : '')
              }
              style={{ left: `${last > 0 ? (i / last) * 100 : 0}%` }}
            />
          ))}
          <span className="year-bar-passed" style={{ width: `${last > 0 ? (index / last) * 100 : 0}%` }} />
        </div>
        <input
          type="range"
          className="year-bar-range"
          min={0}
          max={Math.max(0, last)}
          step={1}
          value={index}
          aria-label="Year"
          aria-valuetext={String(years[index])}
          onChange={(event) => {
            onPlaying(false);
            onIndex(Number(event.target.value));
          }}
        />
        <div className="year-bar-labels numeric" aria-hidden="true">
          {years.map((year, i) =>
            labelled(i) ? (
              <span key={year} style={{ left: `${last > 0 ? (i / last) * 100 : 0}%` }}>
                {year}
              </span>
            ) : null,
          )}
        </div>
      </div>
    </div>
  );
}
