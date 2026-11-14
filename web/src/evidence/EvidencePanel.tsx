import type { ReactNode } from 'react';
import type { Cell, Finding, ResultsDocument, Series, Variable } from '../contract';
import { labelText, placeText } from '../format';
import { figures, monthText, pValue, percent, signed } from './numbers';
import { SeriesChart } from './SeriesChart';
import type { ChangePointMark } from './SeriesChart';
import './evidence.css';

export interface EvidencePanelProps {
  finding: Finding;
  results: ResultsDocument;
  /** Back to the list. */
  onBack: () => void;
  /** Open the Detective with this finding attached. */
  onAsk: (finding: Finding) => void;
  /** A related finding was chosen. */
  onOpenFinding: (findingId: string) => void;
}

interface Fact {
  term: string;
  value: ReactNode;
}

function Facts({ facts }: { facts: Fact[] }) {
  return (
    <dl className="evidence-facts">
      {facts.map((fact) => (
        <div key={fact.term}>
          <dt>{fact.term}</dt>
          <dd className="numeric">{fact.value}</dd>
        </div>
      ))}
    </dl>
  );
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="evidence-section">
      <h4>{title}</h4>
      {children}
    </section>
  );
}

/** The numbers for the finding's own kind, each as the service reported it. */
function keyFacts(finding: Finding): Fact[] {
  const { result, change_point: step, cascade, contradiction } = finding;
  if (result) {
    return [
      { term: 'Trend', value: `${signed(result.slope_per_decade)} ${result.units}` },
      {
        term: `${percent(result.ci_level)} interval`,
        value: `${signed(result.ci_low)} to ${signed(result.ci_high)} ${result.units}`,
      },
      { term: 'Adjusted p-value', value: pValue(result.p_adjusted) },
      { term: 'p-value before correction', value: pValue(result.p_value) },
      { term: 'Direction', value: result.direction === 'none' ? 'No significant direction' : labelText(result.direction) },
    ];
  }
  if (step) {
    return [
      { term: 'Level shift', value: `${signed(step.shift)} ${step.units}` },
      { term: 'When', value: monthText(step.date) },
      { term: 'Date range', value: `${monthText(step.date_low)} to ${monthText(step.date_high)}` },
      { term: 'Level before', value: `${signed(step.level_before)} ${step.units}` },
      { term: 'Level after', value: `${signed(step.level_after)} ${step.units}` },
    ];
  }
  if (cascade) {
    return [
      { term: 'Leads', value: labelText(cascade.leading_variable) },
      { term: 'Follows', value: labelText(cascade.following_variable) },
      { term: 'Lag', value: `${cascade.lag_months} ${cascade.lag_months === 1 ? 'month' : 'months'}` },
      { term: 'Correlation at that lag', value: signed(cascade.correlation) },
      { term: 'Adjusted p-value', value: pValue(cascade.p_adjusted) },
      { term: 'p-value before correction', value: pValue(cascade.p_value) },
    ];
  }
  if (contradiction) {
    const facts: Fact[] = [
      { term: 'Trend here', value: `${signed(contradiction.slope_per_decade)} ${contradiction.units}` },
      {
        term: `Trend at ${placeText(contradiction.paired_cell)}`,
        value: `${signed(contradiction.paired_slope_per_decade)} ${contradiction.units}`,
      },
    ];
    if (contradiction.distance_km !== undefined) {
      facts.push({ term: 'Distance between the cells', value: `${figures(contradiction.distance_km)} km` });
    }
    return facts;
  }
  return [];
}

/** The correlation at each lag tested, with the lag the finding reports picked out. */
function LagBars({ correlations, chosen }: { correlations: number[]; chosen: number }) {
  return (
    <div className="evidence-lags" role="img" aria-label={`Correlation at each lag tested. Strongest at ${chosen} months.`}>
      {correlations.map((correlation, lag) => (
        <div key={lag} className={lag === chosen ? 'evidence-lag evidence-lag-chosen' : 'evidence-lag'}>
          <span className="evidence-lag-value numeric">{signed(correlation, 2)}</span>
          <span className="evidence-lag-track">
            <span className="evidence-lag-bar" style={{ height: `${Math.min(1, Math.abs(correlation)) * 100}%` }} />
          </span>
          <span className="evidence-lag-label numeric">{lag}</span>
        </div>
      ))}
    </div>
  );
}

const scoreParts: { key: keyof Finding['score']; label: string }[] = [
  { key: 'effect_size', label: 'Effect size' },
  { key: 'certainty', label: 'Certainty' },
  { key: 'coverage', label: 'Coverage' },
  { key: 'robustness', label: 'Robustness' },
  { key: 'cross_system', label: 'Cross-system' },
];

function Source({ variable }: { variable: Variable }) {
  return (
    <div className="evidence-source">
      <p className="evidence-source-name">{variable.name}</p>
      <p>
        {variable.mission}, product {variable.product}
        {variable.field ? `, field ${variable.field}` : ''}
      </p>
      {variable.native_resolution && <p>Native resolution: {variable.native_resolution}</p>}
      <p>
        <a href={variable.source_url} target="_blank" rel="noreferrer">
          Find the product on NASA Earthdata
        </a>
      </p>
    </div>
  );
}

/**
 * A finding opened into its evidence: the series it was measured on, its numbers with their
 * uncertainty, how it scored, and where every number came from. Everything shown is read
 * from the results document; nothing is worked out here except how to draw it.
 */
export function EvidencePanel({ finding, results, onBack, onAsk, onOpenFinding }: EvidencePanelProps) {
  const seriesFor = (cell: Cell, variableId: string): Series | undefined =>
    results.series?.find((series) => series.cell_id === cell.id && series.variable === variableId);

  // One chart per series the finding rests on: each of its variables in its cell, and for a
  // contradiction the same variable in the paired cell.
  const charts: { key: string; title: string; series: Series | undefined; slope?: number; step?: ChangePointMark }[] =
    finding.variables.map((variable) => ({
      key: `${finding.cell.id}.${variable.id}`,
      title: `${variable.name} at ${placeText(finding.cell)}`,
      series: seriesFor(finding.cell, variable.id),
      slope: finding.result?.slope_per_decade ?? finding.contradiction?.slope_per_decade,
      step: finding.change_point && {
        date: finding.change_point.date,
        dateLow: finding.change_point.date_low,
        dateHigh: finding.change_point.date_high,
        levelBefore: finding.change_point.level_before,
        levelAfter: finding.change_point.level_after,
      },
    }));
  if (finding.contradiction) {
    const paired = finding.contradiction.paired_cell;
    const variable = finding.variables.find((item) => item.id === finding.contradiction?.variable) ?? finding.variables[0];
    charts.push({
      key: `${paired.id}.${finding.contradiction.variable}`,
      title: `${variable?.name ?? labelText(finding.contradiction.variable)} at ${placeText(paired)}`,
      series: seriesFor(paired, finding.contradiction.variable),
      slope: finding.contradiction.paired_slope_per_decade,
    });
  }

  const hasGuide = charts.some((chart) => chart.series && chart.slope !== undefined);
  const facts = keyFacts(finding);
  const { preprocessing, method, period, standing } = finding;
  const parameters = Object.entries(method.parameters);

  return (
    <div className="evidence">
      <div className="evidence-bar">
        <button type="button" className="button" onClick={onBack}>
          Back to findings
        </button>
        <button type="button" className="button" onClick={() => onAsk(finding)}>
          Ask the Detective
        </button>
      </div>

      <div className="evidence-scroll">
        <header className="evidence-head">
          <p className="evidence-kicker numeric">
            #{finding.rank} {labelText(finding.kind)}, {placeText(finding.cell)}
          </p>
          <h3 className={finding.significant ? '' : 'evidence-quiet'}>{finding.summary}</h3>
          <div className="evidence-badges">
            <span className="badge">{finding.significant ? 'Significant after correction' : 'Not significant'}</span>
            <span className="badge">{labelText(standing.status)}</span>
            {results.synthetic && <span className="badge warning">Synthetic fixture</span>}
          </div>
        </header>

        {charts.map((chart) => (
          <Section key={chart.key} title={chart.title}>
            {chart.series ? (
              <SeriesChart
                dates={chart.series.dates}
                values={chart.series.anomaly}
                units={chart.series.units}
                label={`Monthly anomaly of ${chart.title}, ${monthText(period.start)} to ${monthText(period.end)}`}
                guideSlopePerDecade={chart.slope}
                changePoint={chart.step}
              />
            ) : (
              <p className="evidence-note">The series for this cell is not in this snapshot.</p>
            )}
          </Section>
        ))}
        {hasGuide && (
          <p className="evidence-note">
            The dashed line has the slope the analysis reported. It is placed on the series as a guide for the eye
            and is not itself a result.
          </p>
        )}
        {finding.change_point && (
          <p className="evidence-note">
            The vertical line is the reported date of the shift, the band its date range, and the dashed lines the
            mean level before and after.
          </p>
        )}

        {finding.cascade?.correlations_by_lag && (
          <Section title="Correlation by lag, in months">
            <LagBars correlations={finding.cascade.correlations_by_lag} chosen={finding.cascade.lag_months} />
          </Section>
        )}

        {facts.length > 0 && (
          <Section title="The numbers">
            <Facts facts={facts} />
            {finding.cascade && (
              <p className="evidence-note">An association in time between two variables. It does not show that one caused the other.</p>
            )}
          </Section>
        )}

        {finding.contradiction && finding.contradiction.confounders.length > 0 && (
          <Section title="What else could explain the difference">
            <ul className="evidence-list">
              {finding.contradiction.confounders.map((confounder) => (
                <li key={confounder}>{confounder}</li>
              ))}
            </ul>
          </Section>
        )}

        <Section title="Limitations">
          {standing.limitations.length > 0 ? (
            <ul className="evidence-list">
              {standing.limitations.map((limitation) => (
                <li key={limitation}>{limitation}</li>
              ))}
            </ul>
          ) : (
            <p className="evidence-note">None recorded.</p>
          )}
        </Section>

        <Section title={`Ranking score, ${figures(finding.score.total, 2)} of 1`}>
          <div className="evidence-scores">
            {scoreParts.map((part) => (
              <div key={part.key} className="evidence-score">
                <span>{part.label}</span>
                <span className="evidence-score-track">
                  <span className="evidence-score-bar" style={{ width: `${Math.max(0, Math.min(1, finding.score[part.key])) * 100}%` }} />
                </span>
                <span className="numeric">{finding.score[part.key].toFixed(2)}</span>
              </div>
            ))}
          </div>
        </Section>

        <Section title="Where the data came from">
          {finding.variables.map((variable) => (
            <Source key={variable.id} variable={variable} />
          ))}
        </Section>

        <Section title="What was done to it">
          <Facts
            facts={[
              { term: 'Period', value: `${monthText(period.start)} to ${monthText(period.end)}, ${period.step}` },
              { term: 'Regridding', value: preprocessing.regridding },
              { term: 'Aggregation', value: preprocessing.aggregation },
              { term: 'Anomaly', value: preprocessing.anomaly },
              { term: 'Missing data', value: preprocessing.missing_data_rule },
              {
                term: 'Coverage',
                value: `${preprocessing.months_present} of ${preprocessing.months_expected} months (${percent(preprocessing.coverage)})`,
              },
            ]}
          />
        </Section>

        <Section title="How it was tested">
          <Facts
            facts={[
              { term: 'Test', value: labelText(method.test) },
              ...parameters.map(([name, value]) => ({ term: labelText(name), value: labelText(String(value)) })),
            ]}
          />
        </Section>

        {finding.links.length > 0 && (
          <Section title="Related findings">
            <ul className="evidence-links">
              {finding.links.map((link) => {
                const other = results.findings?.find((candidate) => candidate.id === link.finding_id);
                return (
                  <li key={link.finding_id}>
                    <button type="button" className="evidence-link" disabled={!other} onClick={() => onOpenFinding(link.finding_id)}>
                      {other ? (
                        <>
                          <span className="numeric evidence-link-rank">#{other.rank}</span>
                          <span>{other.summary}</span>
                        </>
                      ) : (
                        <span className="numeric">{link.finding_id}</span>
                      )}
                    </button>
                  </li>
                );
              })}
            </ul>
          </Section>
        )}

        <p className="evidence-id numeric">{finding.id}</p>
      </div>
    </div>
  );
}
