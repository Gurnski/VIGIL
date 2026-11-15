import { useState } from 'react';
import type { ZoneAnalysis, ZoneSummary } from '../contract';
import { SeriesChart } from '../evidence/SeriesChart';
import { signed } from '../evidence/numbers';
import { count, labelText, monthText } from '../format';
import type { ZoneAnswer } from './client';

// What the service said about a zone: one row per variable, then the chosen variable's series
// and every limitation the service sent for it. The web twin of the desktop app's
// ZoneSummaryView. Nothing is worked out here. A zone's p-value is one test on an area chosen
// by eye, so no row is ever called "significant after correction".

const Caveat = 'The zone was chosen by eye and nothing is corrected for many tests, so read each p-value as exploratory.';

/** "28 grid cells, 1.95 million km²". */
function sizeText(analysis: ZoneAnalysis): string {
  const cells = analysis.zone.cell_count === 1 ? '1 grid cell' : `${count(analysis.zone.cell_count)} grid cells`;
  const area = analysis.zone.area_km2;
  const size = area >= 1e6 ? `${(area / 1e6).toFixed(2)} million km²` : `${count(Math.round(area))} km²`;
  return `${cells}, ${size}${analysis.synthetic ? ', made-up data' : ''}`;
}

/** A p-value as the desktop app's zone rows write it. */
function pText(p: number): string {
  return p < 0.001 ? 'p < 0.001' : `p = ${p.toFixed(p < 0.01 ? 3 : 2)}`;
}

interface RowText {
  /** The trend, or what stands in for one. */
  trend: string;
  /** The p-value, a caution, or the reason there is no trend. */
  detail: string;
  /** The interval, when there is one to show. */
  interval: string;
  /** How the row is weighted: a trend below the threshold, one that is not, one to be wary of, or none. */
  tone: 'trend' | 'quiet' | 'caution' | 'none';
}

function rowText(summary: ZoneSummary): RowText {
  if (!summary.tested) {
    return {
      trend: 'Not tested',
      detail: summary.skip_reason === 'no_cells' ? "None of the zone's cells were scanned for this" : 'Too little data over the zone to test',
      interval: '',
      tone: 'none',
    };
  }
  const { result } = summary;
  if (!result) {
    return { trend: 'No trend to report', detail: 'Flat, or a median slope of zero', interval: '', tone: 'none' };
  }
  // The heading above the rows says "per decade", so the unit here is the variable's own.
  const units = result.units.replace(/ per decade$/, '');
  const trend = `${signed(result.slope_per_decade)} ${units}`;
  const p = pText(result.p_value);
  if (summary.caution) {
    // Not to be taken at face value: the caution takes the interval's place, and the trend is given no weight.
    return { trend, detail: `${summary.caution}, ${p}`, interval: '', tone: 'caution' };
  }
  return {
    trend,
    detail: summary.significant ? p : `${p}, not significant`,
    interval: `${signed(result.ci_low)} to ${signed(result.ci_high)}`,
    tone: summary.significant ? 'trend' : 'quiet',
  };
}

export interface ZoneSummaryViewProps {
  name: string;
  /** The service's answer, or null while it is on its way. */
  answer: ZoneAnswer | null;
}

export function ZoneSummaryView({ name, answer }: ZoneSummaryViewProps) {
  const [chosenVariable, setChosenVariable] = useState<string | null>(null);

  if (!answer) {
    return (
      <p className="zones-note" role="status">
        Asking the analysis service about {name}
      </p>
    );
  }
  if (answer.status === 'refused') {
    // The reason, and no figures.
    return <p className="zones-refusal">{answer.reason}</p>;
  }

  const { analysis } = answer;
  const level = analysis.summaries.find((summary) => summary.result)?.result?.ci_level;
  const chosen = analysis.summaries.find((summary) => summary.variable === chosenVariable) ?? analysis.summaries.find((summary) => summary.series) ?? null;
  const nameOf = (variable: string) => analysis.variables.find((item) => item.id === variable)?.name ?? labelText(variable);

  return (
    <div className="zones-summary">
      <p className="zones-summary-size numeric">{sizeText(analysis)}</p>
      <p className="zones-note">{Caveat}</p>
      <p className="zones-heading">
        Trend per decade{level !== undefined ? `, and its ${Math.round(level * 100)}% interval` : ''}
      </p>
      <ul className="zones-rows" role="listbox" aria-label={`Each variable over ${name}`}>
        {analysis.summaries.map((summary) => {
          const text = rowText(summary);
          const selected = summary === chosen;
          return (
            <li key={summary.variable} role="option" aria-selected={selected}>
              <button
                type="button"
                className={selected ? 'zones-row zones-row-selected' : 'zones-row'}
                onClick={() => setChosenVariable(summary.variable)}
              >
                <span className="zones-row-name">{labelText(summary.variable)}</span>
                <span className={`zones-row-trend zones-tone-${text.tone} numeric`}>{text.trend}</span>
                <span className={text.tone === 'caution' ? 'zones-row-detail zones-row-caution numeric' : 'zones-row-detail numeric'}>{text.detail}</span>
                {text.interval && <span className="zones-row-interval numeric">{text.interval}</span>}
              </button>
            </li>
          );
        })}
      </ul>

      {chosen && (
        <section className="zones-chosen" aria-label={nameOf(chosen.variable)}>
          <h4>{nameOf(chosen.variable)} over the zone</h4>
          <p className="zones-chosen-summary">{chosen.summary}</p>
          {chosen.series ? (
            <>
              <SeriesChart
                dates={chosen.series.dates}
                values={chosen.series.anomaly}
                units={chosen.series.units}
                label={`Monthly anomaly of ${nameOf(chosen.variable)} over ${name}, ${monthText(analysis.period.start)} to ${monthText(analysis.period.end)}`}
                guideSlopePerDecade={chosen.result?.slope_per_decade}
              />
              {chosen.result && (
                <p className="zones-note">The dashed line is a guide for the eye. The figures above are the analysis service's.</p>
              )}
            </>
          ) : (
            <p className="zones-note">No cell of the zone was used for this variable, so there is no series to draw.</p>
          )}
          <p className="zones-note numeric">
            {count(chosen.cells_used)} of the zone's {count(analysis.zone.cell_count)} cells went into the series.
          </p>
          {chosen.limitations.length > 0 && (
            <>
              <h4>Limitations</h4>
              <ul className="zones-limitations">
                {chosen.limitations.map((limitation) => (
                  <li key={limitation}>{limitation}</li>
                ))}
              </ul>
            </>
          )}
        </section>
      )}
    </div>
  );
}
