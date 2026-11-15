import { useEffect, useRef } from 'react';
import type { ReactNode } from 'react';
import type { ResultsDocument, RetestTally, RunRecord, Variable } from '../contract';
import { count, labelText, monthText, sourceLinkText } from '../format';
import { downloadCsv, downloadGeoJson } from './export';
import './scan.css';

export interface ScanSummaryProps {
  results: ResultsDocument;
  onClose: () => void;
}

/** A timestamp as the contract writes it, "2026-11-14T22:50:14Z", as "14 November 2026, 22:50 UTC". */
function timeText(timestamp: string): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})/.exec(timestamp);
  if (!match) {
    return timestamp;
  }
  return `${Number(match[3])} ${monthText(`${match[1]}-${match[2]}-01`)}, ${match[4]}:${match[5]} UTC`;
}

/**
 * One count as a share of another, to the nearest whole per cent. The only arithmetic in this
 * panel: both counts are always printed beside it. Null when there is nothing to divide by.
 */
function share(part: number, whole: number): string | null {
  return whole > 0 ? `${Math.round((part / whole) * 100)}%` : null;
}

/** "828 of 888 findings re-tested (93%)". */
function ofText(part: number, whole: number, what: string): string {
  const percentage = share(part, whole);
  return `${count(part)} of ${count(whole)} ${what}${percentage ? ` (${percentage})` : ''}`;
}

const skipWords: Record<string, string> = {
  ocean: 'Ocean',
  ice: 'Ice',
  coverage: 'Too few months of data',
};

/** The run record's name for its correction, as it is written in a sentence. */
const correctionWords: Record<string, string> = {
  benjamini_hochberg: 'the Benjamini-Hochberg procedure',
};

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="scan-section">
      <h3>{title}</h3>
      {children}
    </section>
  );
}

function Facts({ facts }: { facts: { term: string; value: ReactNode }[] }) {
  return (
    <dl className="scan-facts">
      {facts.map((fact) => (
        <div key={fact.term}>
          <dt>{fact.term}</dt>
          <dd className="numeric">{fact.value}</dd>
        </div>
      ))}
    </dl>
  );
}

function Source({ mission, product, field, resolution, url }: { mission: string; product: string; field?: string; resolution?: string; url: string }) {
  return (
    <>
      <p>
        {mission}, product {product}
        {field ? `, field ${field}` : ''}
      </p>
      {resolution && <p>Native resolution: {resolution}</p>}
      <p>
        <a href={url} target="_blank" rel="noreferrer">
          {sourceLinkText(url)}
        </a>
      </p>
    </>
  );
}

function settingsFacts(run: RunRecord): { term: string; value: ReactNode }[] {
  const { settings, counts } = run;
  const { grid, period, region } = settings;
  const facts: { term: string; value: ReactNode }[] = [
    { term: 'Period', value: `${monthText(period.start)} to ${monthText(period.end)}, ${period.step}` },
    { term: 'Grid', value: `${grid.cell_degrees}° cells, ${count(grid.rows)} rows by ${count(grid.columns)} columns` },
  ];
  if (region) {
    facts.push({
      term: 'Region scanned',
      value: `${count(region.rows)} rows by ${count(region.columns)} columns of that grid, from row ${region.row} and column ${region.column}. Every count here is for the region.`,
    });
  }
  facts.push(
    { term: 'Cells scanned', value: `${count(counts.cells_scanned)} of ${count(counts.cells_total)}` },
    {
      term: 'Counts as significant',
      value: `Adjusted p-value below ${settings.threshold}, corrected for many tests by ${correctionWords[settings.multiple_testing] ?? labelText(settings.multiple_testing)}`,
    },
    { term: 'Coverage needed', value: `${Math.round(settings.min_coverage * 100)}% of the months` },
    { term: 'Interval level', value: `${Math.round(settings.ci_level * 100)}%` },
  );
  if (settings.max_lag_months !== undefined) {
    facts.push({ term: 'Longest lag tested', value: `${settings.max_lag_months} months` });
  }
  facts.push({ term: 'Finished', value: timeText(run.finished) }, { term: 'Code version', value: run.code_version });
  return facts;
}

function Counts({ run }: { run: RunRecord }) {
  const { counts } = run;
  const skipped = Object.entries(counts.skipped_by_reason);
  const kinds = Object.entries(counts.findings_by_kind);
  return (
    <>
      <Section title="Tests">
        <p className="scan-note">A test is one variable in one cell.</p>
        <Facts
          facts={[
            { term: 'Cells times variables', value: count(counts.tests_total) },
            { term: 'Tests run', value: count(counts.tests_run) },
            { term: 'Significant after correction', value: ofText(counts.tests_significant, counts.tests_run, 'tests run') },
            { term: 'Not significant', value: count(counts.tests_not_significant) },
            { term: 'Skipped', value: count(counts.tests_skipped) },
            ...skipped.map(([reason, tests]) => ({ term: `Skipped: ${(skipWords[reason] ?? labelText(reason)).toLowerCase()}`, value: count(tests) })),
          ]}
        />
      </Section>

      {counts.by_variable.length > 0 && (
        <Section title="Tests by variable">
          <div className="scan-table-scroll">
            <table className="scan-table numeric">
              <thead>
                <tr>
                  <th scope="col">Variable</th>
                  <th scope="col">Run</th>
                  <th scope="col">Significant</th>
                  <th scope="col">Not significant</th>
                  <th scope="col">Skipped</th>
                </tr>
              </thead>
              <tbody>
                {counts.by_variable.map((row) => (
                  <tr key={row.variable}>
                    <th scope="row">{labelText(row.variable)}</th>
                    <td>{count(row.tests_run)}</td>
                    <td>{count(row.tests_significant)}</td>
                    <td>{count(row.tests_not_significant)}</td>
                    <td>{count(row.tests_skipped)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Section>
      )}

      <Section title="Findings">
        <Facts
          facts={[
            { term: 'Findings listed', value: count(counts.findings_total) },
            ...kinds.map(([kind, findings]) => ({ term: labelText(kind), value: count(findings) })),
          ]}
        />
      </Section>
    </>
  );
}

function Variables({ variables }: { variables: Variable[] }) {
  return (
    <Section title="Variables and their sources">
      {variables.map((variable) => (
        <div key={variable.id} className="scan-source">
          <p className="scan-source-name">
            {variable.name}, in {variable.units}
          </p>
          <Source
            mission={variable.mission}
            product={variable.product}
            field={variable.field}
            resolution={variable.native_resolution}
            url={variable.source_url}
          />
        </div>
      ))}
    </Section>
  );
}

/**
 * How one variable's trends came out on its second product. The share confirmed is never
 * printed without the rate beside it: how often the second product has a significant trend
 * where the scan found nothing.
 */
function RetestBlock({ tally }: { tally: RetestTally }) {
  const outcomes: { label: string; findings: number }[] = [
    { label: 'Confirmed', findings: tally.confirmed },
    { label: 'Same direction, not significant on its own', findings: tally.same_direction },
    { label: 'Opposite direction', findings: tally.opposite },
    { label: 'Not possible, too little data', findings: tally.not_possible },
  ];
  const hasRate = tally.other_cells_tested > 0;
  return (
    <div className="scan-retest">
      <h4>{labelText(tally.variable)}</h4>
      <div className="scan-source">
        <p className="scan-source-name">Second product</p>
        <Source
          mission={tally.product.mission}
          product={tally.product.product}
          field={tally.product.field}
          resolution={tally.product.native_resolution}
          url={tally.product.source_url}
        />
      </div>
      <table className="scan-table numeric">
        <thead>
          <tr>
            <th scope="col">Of {count(tally.findings)} trend findings re-tested</th>
            <th scope="col">Findings</th>
          </tr>
        </thead>
        <tbody>
          {outcomes.map((outcome) => (
            <tr key={outcome.label}>
              <th scope="row">{outcome.label}</th>
              <td>{count(outcome.findings)}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <div className="scan-rate">
        {hasRate ? (
          <>
            <p>Confirmed: {ofText(tally.confirmed, tally.findings, 'findings re-tested')}.</p>
            <p>
              Where the scan found nothing, the second product has a significant trend in{' '}
              {ofText(tally.other_cells_significant, tally.other_cells_tested, 'cells')}.
            </p>
            {tally.other_cells_tested < tally.other_cells && (
              <p className="scan-note">
                The second product could be tested in {count(tally.other_cells_tested)} of the {count(tally.other_cells)}{' '}
                cells where the scan found nothing.
              </p>
            )}
            <p className="scan-note">
              Hold the first figure against the second. Where the second product has a trend in most cells whatever
              the scan found, a confirmation says less.
            </p>
          </>
        ) : (
          <>
            <p>
              Confirmed: {count(tally.confirmed)} of {count(tally.findings)} findings re-tested.
            </p>
            <p>
              Where the scan found nothing, the second product could not be tested in any cell, so there is no rate to
              hold the confirmations against.
            </p>
          </>
        )}
      </div>
      <p className="scan-note">{tally.in_common}</p>
    </div>
  );
}

/**
 * "About this scan": what the run covered, what it counted and where its data came from, and
 * how its trends came out when tested again on a second product. Everything is read from the
 * results document. The only arithmetic is a count as a share of another count shown beside it.
 */
export function ScanSummary({ results, onClose }: ScanSummaryProps) {
  const closeButton = useRef<HTMLButtonElement>(null);
  const { run, variables } = results;
  const findingCount = results.findings?.length ?? 0;

  useEffect(() => {
    closeButton.current?.focus();
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onClose();
      }
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [onClose]);

  return (
    <aside className="scan-panel" role="dialog" aria-label="About this scan">
      <header className="scan-header">
        <h2>About this scan</h2>
        <button type="button" ref={closeButton} className="scan-close" onClick={onClose} aria-label="Close">
          <svg width="10" height="10" viewBox="0 0 10 10" aria-hidden="true">
            <path d="M1 1 9 9M9 1 1 9" stroke="currentColor" strokeWidth="1.2" fill="none" />
          </svg>
        </button>
      </header>

      <div className="scan-scroll">
        {results.synthetic && (
          <p className="scan-synthetic">
            <span className="badge warning">Synthetic fixture</span>
            <span>Every number here is made up, to show the viewer working. It is not a real result.</span>
          </p>
        )}

        {run ? (
          <>
            <Section title="What was scanned">
              <Facts facts={[{ term: 'Run', value: run.id }, ...settingsFacts(run)]} />
            </Section>
            <Counts run={run} />
          </>
        ) : (
          <p className="scan-note">These results carry no run record, so there is nothing to say about the scan.</p>
        )}

        {findingCount > 0 && (
          <Section title="Take the findings to another tool">
            <p>
              {findingCount === 1 ? 'The one finding' : `All ${count(findingCount)} findings`}, each with its cell, its figures as the analysis
              stored them, its standing and its score. The GeoJSON holds each cell as a polygon and opens in a GIS such as
              QGIS. The CSV opens in a spreadsheet.
            </p>
            <div className="scan-downloads">
              <button type="button" className="button" onClick={() => downloadCsv(results)}>
                Download CSV
              </button>
              <button type="button" className="button" onClick={() => downloadGeoJson(results)}>
                Download GeoJSON
              </button>
            </div>
            <p className="scan-note">
              The files are made in your browser from the results on screen.
              {results.synthetic
                ? ' These findings are the synthetic fixture, and each file says so in its name and in a field of every row.'
                : ''}
            </p>
          </Section>
        )}

        {variables && variables.length > 0 && <Variables variables={variables} />}

        {run?.retest && (
          <Section title="Trends tested again on a second product">
            <p>{run.retest.rule}</p>
            <p className="scan-note">
              A finding counts as re-tested only when it was confirmed. A confirmation is evidence, not proof: no
              second product is fully independent of the first.
            </p>
            {run.retest.by_variable.map((tally) => (
              <RetestBlock key={tally.variable} tally={tally} />
            ))}
          </Section>
        )}
      </div>
    </aside>
  );
}
