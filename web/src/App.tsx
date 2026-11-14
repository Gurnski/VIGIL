import { useCallback, useEffect, useMemo, useState } from 'react';
import type { Finding, ResultsDocument } from './contract';
import { loadSnapshot } from './data';
import { count, labelText } from './format';
import { FindingsList } from './findings/FindingsList';
import { GlobeView } from './globe/GlobeView';
import { DetectiveChat } from './detective/DetectiveChat';
import { EvidencePanel } from './evidence/EvidencePanel';

/**
 * The viewer's one screen: the globe with a variable's trend layer, the ranked findings beside
 * it (underneath on a narrow screen), each opening into its evidence, and the Detective in a
 * panel that slides over. It is a read-only companion to the desktop app and shows the same
 * results document.
 */
export function App() {
  const [results, setResults] = useState<ResultsDocument | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  // Whether the findings card shows the chosen finding's evidence or the list.
  const [evidenceOpen, setEvidenceOpen] = useState(false);
  const [layerVariable, setLayerVariable] = useState<string | null>(null);
  const [showLayer, setShowLayer] = useState(true);
  const [significantOnly, setSignificantOnly] = useState(false);
  const [showSatellites, setShowSatellites] = useState(true);
  const [chatOpen, setChatOpen] = useState(false);
  const [attached, setAttached] = useState<Finding | null>(null);

  useEffect(() => {
    loadSnapshot()
      .then((document) => {
        setResults(document);
        setLayerVariable(document.grids?.[0]?.variable ?? null);
      })
      .catch((reason: unknown) => setError(reason instanceof Error ? reason.message : String(reason)));
  }, []);

  const findings = useMemo(() => results?.findings ?? [], [results]);
  const selected = useMemo(() => findings.find((finding) => finding.id === selectedId) ?? null, [findings, selectedId]);

  /** Choosing a finding, in the list or on the globe, opens its evidence and switches to its variable's layer. */
  const selectFinding = useCallback(
    (finding: Finding) => {
      setSelectedId(finding.id);
      setEvidenceOpen(true);
      const variable = finding.variables[0]?.id;
      if (variable && results?.grids?.some((grid) => grid.variable === variable)) {
        setLayerVariable(variable);
      }
    },
    [results],
  );

  /** A citation in a Detective answer: show that finding, if it is in these results. */
  const showCitation = useCallback(
    (findingId: string) => {
      const finding = findings.find((candidate) => candidate.id === findingId);
      if (finding) {
        selectFinding(finding);
      }
    },
    [findings, selectFinding],
  );

  const askAbout = useCallback((finding: Finding) => {
    setAttached(finding);
    setChatOpen(true);
  }, []);

  if (error) {
    return (
      <div className="app-message">
        <h1>VIGIL</h1>
        <p>The results could not be loaded. {error}</p>
      </div>
    );
  }
  if (!results) {
    return (
      <div className="app-message">
        <h1>VIGIL</h1>
        <p>Loading the scan…</p>
      </div>
    );
  }

  const counts = results.run?.counts;
  const grids = results.grids ?? [];

  return (
    <div className="app">
      <header className="app-header">
        <div className="app-title">
          <h1>VIGIL</h1>
          <p>Earth system trend detective</p>
        </div>
        <div className="app-actions">
          {results.synthetic && <span className="badge warning">Synthetic fixture</span>}
          <a className="button" href="download/VIGIL-win-x64.zip">
            Download for Windows
          </a>
          <button className="button primary" onClick={() => setChatOpen((open) => !open)} aria-expanded={chatOpen}>
            Ask the Detective
          </button>
        </div>
      </header>

      <div className="toolbar">
        {grids.length > 0 && (
          <div className="segmented" role="radiogroup" aria-label="Layer">
            {grids.map((grid) => (
              <button
                key={grid.variable}
                role="radio"
                aria-checked={grid.variable === layerVariable}
                className={grid.variable === layerVariable ? 'selected' : ''}
                onClick={() => setLayerVariable(grid.variable)}
              >
                {labelText(grid.variable)}
              </button>
            ))}
          </div>
        )}
        <label className="switch">
          <input type="checkbox" checked={showLayer} onChange={(event) => setShowLayer(event.target.checked)} />
          <span className="track" aria-hidden="true" />
          Layer
        </label>
        <label className="switch">
          <input type="checkbox" checked={significantOnly} onChange={(event) => setSignificantOnly(event.target.checked)} />
          <span className="track" aria-hidden="true" />
          Significant only
        </label>
        <label className="switch">
          <input type="checkbox" checked={showSatellites} onChange={(event) => setShowSatellites(event.target.checked)} />
          <span className="track" aria-hidden="true" />
          Satellites
        </label>
      </div>

      <main className="app-main">
        <section className="globe-area" aria-label="Globe">
          <GlobeView
            results={results}
            layerVariable={layerVariable}
            showLayer={showLayer}
            significantOnly={significantOnly}
            selected={selected}
            showSatellites={showSatellites}
            onFindingClick={selectFinding}
          />
        </section>

        <section className="card findings-card" aria-label="Findings">
          {selected && evidenceOpen ? (
            <EvidencePanel
              finding={selected}
              results={results}
              onBack={() => setEvidenceOpen(false)}
              onAsk={askAbout}
              onOpenFinding={showCitation}
            />
          ) : (
            <>
              <div className="card-title">
                <h2>Findings</h2>
                {selected && (
                  <button className="button" onClick={() => setEvidenceOpen(true)}>
                    Evidence for #{selected.rank}
                  </button>
                )}
              </div>
              {counts && (
                <p className="scan-line">
                  {count(counts.tests_significant)} of {count(counts.tests_run)} tests significant after correction,{' '}
                  {count(findings.length)} findings listed.
                </p>
              )}
              <FindingsList findings={findings} selectedId={selectedId} onSelect={selectFinding} />
            </>
          )}
        </section>
      </main>

      {chatOpen && (
        <aside className="chat-panel" aria-label="The Detective">
          <DetectiveChat
            findings={findings}
            attached={attached}
            onDetach={() => setAttached(null)}
            onCitationClick={showCitation}
            onClose={() => setChatOpen(false)}
          />
        </aside>
      )}
    </div>
  );
}
