import type { Finding, ResultsDocument } from '../contract';
import { FindingsPreview } from '../findings/FindingsList';
import { count, labelText } from '../format';
import { ScanDetails } from '../scan/ScanSummary';
import './overview.css';

/** How many findings the top of the ranking shows. The desktop app's Overview shows eight too. */
const TopFindings = 8;

export interface OverviewPageProps {
  results: ResultsDocument;
  /** One of the top findings was chosen. */
  onFinding: (finding: Finding) => void;
  onOpenMap: () => void;
  onOpenEvidence: () => void;
}

interface Tile {
  caption: string;
  value: string;
  note: string;
  /** The note carries a warning: something the figure should not be read without. */
  warning?: boolean;
}

/** The four figures at the top, as the desktop app's Overview has them. Every one is read from the run record. */
function tiles(results: ResultsDocument): Tile[] {
  const counts = results.run?.counts;
  const findings = results.findings ?? [];
  if (!counts) {
    return [{ caption: 'Findings', value: count(findings.length), note: 'These results carry no run record.' }];
  }
  const kinds = Object.entries(counts.findings_by_kind)
    .sort((a, b) => b[1] - a[1])
    .map(([kind, total]) => `${count(total)} ${labelText(kind).toLowerCase()}${total === 1 ? '' : 's'}`)
    .join(', ');
  const list: Tile[] = [
    {
      caption: 'Tests run',
      value: count(counts.tests_run),
      note: `one variable in one cell, ${count(results.variables?.length ?? 0)} variables`,
    },
    {
      caption: 'Significant after correction',
      value: count(counts.tests_significant),
      note: counts.tests_run > 0 ? `${Math.round((counts.tests_significant / counts.tests_run) * 100)}% of the tests run` : '',
    },
    { caption: 'Findings', value: count(counts.findings_total > 0 ? counts.findings_total : findings.length), note: kinds },
  ];
  const tallies = results.run?.retest?.by_variable ?? [];
  if (tallies.length > 0) {
    const sum = (pick: (tally: (typeof tallies)[number]) => number) => tallies.reduce((total, tally) => total + pick(tally), 0);
    const opposite = sum((tally) => tally.opposite);
    // The count re-tested is the headline. How many held means little pooled across variables, whose
    // second products differ in how readily they agree: each variable's own rate is in the card below.
    list.push({
      caption: 'Trends tested again on a second product',
      value: count(sum((tally) => tally.findings)),
      note: `${count(sum((tally) => tally.confirmed))} confirmed, ${count(opposite)} contradicted, by variable below`,
      // Contradicted trends are what the re-test is for, so they take the colour.
      warning: opposite > 0,
    });
  }
  return list;
}

/**
 * The Overview page, where the viewer opens, as the desktop app does: what the scan covered
 * and found in four figures, the top of the ranking, and each variable with its source, its
 * results and how its trends came out when tested again. Everything on it is read from the
 * results document, and the only arithmetic is a count and its share of another count.
 */
export function OverviewPage({ results, onFinding, onOpenMap, onOpenEvidence }: OverviewPageProps) {
  const top = (results.findings ?? []).slice().sort((a, b) => a.rank - b.rank).slice(0, TopFindings);

  return (
    <div className="overview">
      <header className="overview-header">
        <h2>Overview</h2>
        <p>
          {results.synthetic
            ? 'These figures are a made-up fixture, to show the viewer working. They are not a real scan.'
            : 'What the scan covered, what it found, and how far each part has been checked.'}
        </p>
      </header>

      <div className="overview-tiles">
        {tiles(results).map((tile) => (
          <div key={tile.caption} className="overview-tile">
            <p className="overview-tile-caption">{tile.caption}</p>
            <p className="overview-tile-value numeric">{tile.value}</p>
            <p className={tile.warning ? 'overview-tile-note overview-tile-warning' : 'overview-tile-note'}>{tile.note}</p>
          </div>
        ))}
      </div>

      <div className="overview-columns">
        <section className="card overview-ranking" aria-label="Top of the ranking">
          <div className="card-title">
            <h2>Top of the ranking</h2>
            {results.synthetic && <span className="badge warning">Synthetic fixture</span>}
          </div>
          {top.length > 0 ? (
            <FindingsPreview findings={top} onSelect={onFinding} />
          ) : (
            <p className="overview-none">These results list no findings.</p>
          )}
          <div className="overview-actions">
            <button type="button" className="button primary" onClick={onOpenMap}>
              Open the map
            </button>
            <button type="button" className="button" onClick={onOpenEvidence}>
              All the evidence
            </button>
          </div>
        </section>

        <section className="card overview-scan" aria-label="The scan">
          <div className="card-title">
            <h2>The scan, variable by variable</h2>
          </div>
          <div className="overview-scan-body">
            <ScanDetails results={results} brief />
          </div>
        </section>
      </div>
    </div>
  );
}
