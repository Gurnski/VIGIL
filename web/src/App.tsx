import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { AskStep, Finding, FindingKind, PresentZone, ResultsDocument } from './contract';
import { loadSnapshot } from './data';
import { count, labelText } from './format';
import { FindingsList } from './findings/FindingsList';
import { GlobeView } from './globe/GlobeView';
import type { GlobeBox, GlobeHighlight, GlobeZone } from './globe/GlobeView';
import { PlaceSearch } from './globe/PlaceSearch';
import { placeBox } from './globe/places';
import type { Place } from './globe/places';
import { satelliteCaveat } from './globe/satellite';
import { useYears } from './globe/years';
import { usePresenter } from './present';
import { useZones } from './zones/useZones';
import { ZonesPanel } from './zones/ZonesPanel';
import type { Shown } from './present';
import { DetectiveChat } from './detective/DetectiveChat';
import { EvidencePanel } from './evidence/EvidencePanel';
import { ScanSummary } from './scan/ScanSummary';

/** Says so when some of the findings named are not in the results on screen, and so cannot be ringed. */
function missingText(found: number, named: number): string | undefined {
  return found < named ? `${count(found)} of the ${count(named)} findings named are in these results.` : undefined;
}

/** The kinds the findings list can be narrowed to, as on the desktop app's Evidence page. */
const kindChoices: { kind: FindingKind | null; label: string }[] = [
  { kind: null, label: 'All' },
  { kind: 'trend', label: 'Trends' },
  { kind: 'change_point', label: 'Change points' },
  { kind: 'cascade', label: 'Cascades' },
  { kind: 'contradiction', label: 'Contradictions' },
];

/** Below this width the Detective covers the whole screen, so showing something on the globe has to put it away. */
const narrowScreen = '(max-width: 899px)';

/**
 * The viewer's one screen: the globe with a variable's trend layer, the ranked findings beside
 * it (underneath on a narrow screen), each opening into its evidence, the Detective in a panel
 * that slides over, and "About this scan" in another. It is a read-only companion to the
 * desktop app and shows the same results document.
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
  const [relief, setRelief] = useState(false);
  // How the globe is looked at: leaned over, and unrolled into a flat map. Neither changes what is shown.
  const [tilt, setTilt] = useState(false);
  const [flat, setFlat] = useState(false);
  // Whether the layer shows one calendar year at a time in place of the trend.
  const [showYears, setShowYears] = useState(false);
  // Why the years were switched back off, when they could not be had.
  const [yearsNote, setYearsNote] = useState<string | null>(null);
  // A zone the presenter is showing. Drawn on the globe and never saved.
  const [presentedZone, setPresentedZone] = useState<PresentZone | null>(null);
  // Which of the two tabs beside the globe is open: the findings, or the zones.
  const [sideTab, setSideTab] = useState<'findings' | 'zones'>('findings');
  // A place chosen by name, as the box the globe frames. A new object each time, so the same place can be gone to twice.
  const [placeToShow, setPlaceToShow] = useState<GlobeBox | null>(null);
  const [showSatellites, setShowSatellites] = useState(true);
  // The kind and the variable the findings are narrowed to, or null for every one. The list and the
  // globe's markers are both narrowed, so the dots on the globe are the findings in the list.
  const [kind, setKind] = useState<FindingKind | null>(null);
  const [wantedVariable, setWantedVariable] = useState<string | null>(null);
  const [chatOpen, setChatOpen] = useState(false);
  // On a phone one part fills the screen at a time, chosen from the tabs along the bottom. The Detective is the third tab.
  const [phoneTab, setPhoneTab] = useState<'globe' | 'findings'>('globe');
  // On a phone the globe's switches sit in a panel that this opens, so the globe keeps the screen.
  const [optionsOpen, setOptionsOpen] = useState(false);
  const [attached, setAttached] = useState<Finding | null>(null);
  const [scanOpen, setScanOpen] = useState(false);
  // What the Detective has pointed out on the globe: an answer's cited findings, or one of its lookups.
  const [highlight, setHighlight] = useState<GlobeHighlight | null>(null);
  const layerBar = useRef<HTMLDivElement>(null);
  const globeArea = useRef<HTMLElement>(null);
  const findingsCard = useRef<HTMLElement>(null);

  /**
   * On a phone only one of the globe and the findings is on screen. This shows the one that
   * matters when something is put there from elsewhere. On a wide screen both are always in view.
   */
  const bringToScreen = useCallback((part: 'globe' | 'findings') => {
    if (window.matchMedia(narrowScreen).matches) {
      setPhoneTab(part);
      setOptionsOpen(false);
    }
  }, []);

  useEffect(() => {
    loadSnapshot()
      .then((document) => {
        setResults(document);
        setLayerVariable(document.grids?.[0]?.variable ?? null);
      })
      .catch((reason: unknown) => setError(reason instanceof Error ? reason.message : String(reason)));
  }, []);

  const runId = results?.run?.id ?? null;
  const yearsState = useYears(runId, layerVariable, showYears && showLayer);
  // Neither the file beside the page nor a service has this variable's years: back to the trend, and say why.
  useEffect(() => {
    if (yearsState.status === 'missing') {
      setShowYears(false);
      setYearsNote(
        `The years for ${labelText(layerVariable ?? '').toLowerCase()} are not beside this page and no service holding this scan answered, so the layer shows the trend.`,
      );
    }
  }, [yearsState.status, layerVariable]);

  const findings = useMemo(() => results?.findings ?? [], [results]);
  const selected = useMemo(() => findings.find((finding) => finding.id === selectedId) ?? null, [findings, selectedId]);
  // Zones: areas drawn on the globe, named and saved. A chosen one limits the findings, the markers and the layer to its cells.
  const zones = useZones(results);
  const { chosenCellIds } = zones;
  const inZone = useMemo(
    () => (chosenCellIds ? findings.filter((finding) => chosenCellIds.has(finding.cell.id)) : findings),
    [findings, chosenCellIds],
  );
  // The variables the findings can be narrowed to: those some finding involves, in the scan's own order.
  const variableChoices = useMemo(() => {
    const involved = new Set(findings.flatMap((finding) => finding.variables.map((variable) => variable.id)));
    const inOrder = (results?.variables ?? []).map((variable) => variable.id).filter((id) => involved.has(id));
    return [...inOrder, ...[...involved].filter((id) => !inOrder.includes(id))];
  }, [findings, results]);
  // A variable these results do not have, as after another scan is loaded, narrows nothing.
  const variable = wantedVariable !== null && variableChoices.includes(wantedVariable) ? wantedVariable : null;
  // A finding is of a variable when any of its variables is: a cascade is listed under both of its two.
  const listed = useMemo(
    () =>
      inZone.filter(
        (finding) =>
          (kind === null || finding.kind === kind) &&
          (variable === null || finding.variables.some((candidate) => candidate.id === variable)),
      ),
    [inZone, kind, variable],
  );

  /** Narrowing to a variable also shows that variable's layer, when the results have one. "All variables" leaves the layer alone. */
  const chooseVariable = useCallback(
    (id: string | null) => {
      setWantedVariable(id);
      if (id !== null && results?.grids?.some((grid) => grid.variable === id)) {
        setYearsNote(null);
        setLayerVariable(id);
      }
    },
    [results],
  );

  /** Choosing a finding, in the list or on the globe, opens its evidence and switches to its variable's layer. */
  const selectFinding = useCallback(
    (finding: Finding) => {
      setSelectedId(finding.id);
      setEvidenceOpen(true);
      // On a phone the evidence opens in the findings tab, whether the finding was chosen in the list or on the globe.
      bringToScreen('findings');
      const variable = finding.variables[0]?.id;
      if (variable && results?.grids?.some((grid) => grid.variable === variable)) {
        setLayerVariable(variable);
      }
    },
    [results],
  );

  /** A related finding in the evidence view: show it, if it is in these results. */
  const openFinding = useCallback(
    (findingId: string) => {
      const finding = findings.find((candidate) => candidate.id === findingId);
      if (finding) {
        selectFinding(finding);
      }
      return finding !== undefined;
    },
    [findings, selectFinding],
  );

  /** A citation in a Detective answer: open that finding's evidence. On a phone the chat is put away so it can be seen. */
  const showCitation = useCallback(
    (findingId: string) => {
      if (openFinding(findingId) && window.matchMedia(narrowScreen).matches) {
        setChatOpen(false);
        bringToScreen('findings');
      }
    },
    [openFinding, bringToScreen],
  );

  /** The layer follows what is pointed out, so a ring is read against its own variable's trends. */
  const showLayerOf = useCallback(
    (finding: Finding | undefined) => {
      const variable = finding?.variables[0]?.id;
      if (variable && results?.grids?.some((grid) => grid.variable === variable)) {
        setLayerVariable(variable);
      }
    },
    [results],
  );

  /**
   * Rings the findings an answer cites and flies to the first. It never opens the evidence. When
   * an answer has just arrived the chat and the layer stay as they are, and an answer with nothing
   * to ring takes the last answer's marks down. When the user asked, with "Show on globe", the
   * layer follows the finding and a phone puts the chat away so the globe can be seen.
   */
  const highlightCited = useCallback(
    (findingIds: string[], sample: boolean, asked: boolean) => {
      const cited = findingIds
        .map((id) => findings.find((candidate) => candidate.id === id))
        .filter((finding): finding is Finding => finding !== undefined);
      if (cited.length === 0) {
        if (!asked) {
          setHighlight(null);
        }
        return;
      }
      setHighlight({
        tone: 'cited',
        // A canned fixture answer is never passed off as the Detective's.
        title: sample ? 'Cited in the sample answer' : "Cited in the Detective's answer",
        caption: missingText(cited.length, findingIds.length),
        findings: cited,
      });
      if (asked) {
        showLayerOf(cited[0]);
        if (window.matchMedia(narrowScreen).matches) {
          setChatOpen(false);
          bringToScreen('globe');
        }
      }
    },
    [findings, showLayerOf, bringToScreen],
  );
  const onAnswerCited = useCallback(
    (findingIds: string[], sample: boolean) => highlightCited(findingIds, sample, false),
    [highlightCited],
  );
  const onShowCitation = useCallback(
    (findingId: string, sample: boolean) => highlightCited([findingId], sample, true),
    [highlightCited],
  );

  /** One of the Detective's lookups: outline where it searched and ring what it read. These are not the answer's evidence. */
  const showStep = useCallback(
    (step: AskStep) => {
      const read = step.finding_ids
        .map((id) => findings.find((candidate) => candidate.id === id))
        .filter((finding): finding is Finding => finding !== undefined);
      if (!step.box && read.length === 0) {
        return;
      }
      setHighlight({
        tone: 'read',
        title: 'Read by the Detective, not the evidence for its answer',
        caption: [step.summary, missingText(read.length, step.finding_ids.length)].filter(Boolean).join('. '),
        findings: read,
        box: step.box,
      });
      showLayerOf(read[0]);
      if (window.matchMedia(narrowScreen).matches) {
        setChatOpen(false);
        bringToScreen('globe');
      }
    },
    [findings, showLayerOf, bringToScreen],
  );

  /**
   * What a presenter has put on show: open the finding as a tap would, switch to the layer,
   * draw the zone. Anything left null is left as it is on this screen.
   */
  const showPresented = useCallback(
    (show: Shown) => {
      const finding = show.finding_id ? findings.find((candidate) => candidate.id === show.finding_id) : undefined;
      if (finding) {
        selectFinding(finding);
        bringToScreen('findings');
      }
      // After the finding, which switches to its own variable's layer.
      const layer = show.variable != null && results?.grids?.some((grid) => grid.variable === show.variable) ? show.variable : null;
      if (layer) {
        setLayerVariable(layer);
      }
      const zone = show.zone && Array.isArray(show.zone.polygon) && show.zone.polygon.length >= 3 ? show.zone : null;
      if (zone) {
        setPresentedZone(zone);
      }
      // With no finding to open, what changed is on the globe, which a phone may have scrolled away.
      if (!finding && (layer || zone)) {
        bringToScreen('globe');
      }
    },
    [findings, results, selectFinding, bringToScreen],
  );
  const presenter = usePresenter(runId, showPresented);
  const clearPresentedZone = useCallback(() => setPresentedZone(null), []);
  const goToPlace = useCallback(
    (place: Place) => {
      setPlaceToShow(placeBox(place));
      bringToScreen('globe');
    },
    [bringToScreen],
  );

  // What is outlined on the globe: a presenter's zone while there is one, else an outline that
  // has just been finished, else the chosen zone. A presenter's zone takes the place of the
  // person's own on the globe and deletes nothing: theirs is drawn again when it is removed.
  const { chosenZone, chosen, pending } = zones;
  const zoneOnGlobe = useMemo<GlobeZone | null>(() => {
    if (presentedZone) {
      return presentedZone;
    }
    if (pending) {
      return { name: 'New zone', polygon: pending.points, edges: pending.edges };
    }
    return chosenZone && chosen ? { name: chosenZone.name, polygon: chosenZone.points, edges: chosen.edges } : null;
  }, [presentedZone, pending, chosenZone, chosen]);

  /** "Draw a zone": the globe takes clicks as points, so it is brought on screen and the evidence is put away. */
  const drawZone = useCallback(() => {
    zones.startDrawing();
    setSideTab('zones');
    setEvidenceOpen(false);
    bringToScreen('globe');
    // The zones object is new on every draw, and only its stable starter is used here.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [zones.startDrawing, bringToScreen]);
  /** The outline is closed: on to its name, which is in the Zones tab. */
  const finishZone = () => {
    zones.finishDrawing();
    setSideTab('zones');
    // The name is asked for where the evidence may be open.
    setEvidenceOpen(false);
    bringToScreen('findings');
  };

  const clearHighlight = useCallback(() => setHighlight(null), []);
  const openChat = useCallback(() => setChatOpen(true), []);
  const closeScan = useCallback(() => setScanOpen(false), []);

  // With many layers the row of them scrolls on a phone: keep the chosen one in sight.
  useEffect(() => {
    layerBar.current?.querySelector('.selected')?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
  }, [layerVariable]);

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
    <div className={`app phone-tab-${phoneTab}${chatOpen ? ' app-chat-open' : ''}`}>
      <header className="app-header">
        <div className="app-title">
          <h1>VIGIL</h1>
          <p>Earth system trend detective</p>
        </div>
        <div className="app-actions">
          {results.synthetic && <span className="badge warning">Synthetic fixture</span>}
          {presenter.presenting && (
            <label className="switch" title="Someone is presenting this scan. While this is on, your screen shows what they show.">
              <input type="checkbox" checked={presenter.following} onChange={(event) => presenter.setFollowing(event.target.checked)} />
              <span className="track" aria-hidden="true" />
              Following the presenter
            </label>
          )}
          <button className="button" onClick={() => setScanOpen((open) => !open)} aria-expanded={scanOpen}>
            About this scan
          </button>
          <a className="button wide-only" href="download/VIGIL-win-x64.zip">
            Download for Windows
          </a>
          <button className="button primary wide-only" onClick={() => setChatOpen((open) => !open)} aria-expanded={chatOpen}>
            Ask the Detective
          </button>
        </div>
      </header>

      <div className="toolbar">
        {grids.length > 0 && (
          <div className="segmented" role="radiogroup" aria-label="Layer" ref={layerBar}>
            {grids.map((grid) => (
              <button
                key={grid.variable}
                role="radio"
                aria-checked={grid.variable === layerVariable}
                className={grid.variable === layerVariable ? 'selected' : ''}
                onClick={() => {
                  setYearsNote(null);
                  setLayerVariable(grid.variable);
                }}
              >
                {labelText(grid.variable)}
              </button>
            ))}
          </div>
        )}
        {/* On a phone the switches wait in a panel opened by "Map options". On a wide screen this wrapper is not a box and they sit in the toolbar as before. */}
        <div className={optionsOpen ? 'toolbar-options toolbar-options-open' : 'toolbar-options'} id="map-options">
        <div className="toolbar-options-head">
          <span>Map options</span>
          <button type="button" className="button" onClick={() => setOptionsOpen(false)}>
            Done
          </button>
        </div>
        <label className="switch">
          <input type="checkbox" checked={showLayer} onChange={(event) => setShowLayer(event.target.checked)} />
          <span className="track" aria-hidden="true" />
          Layer
        </label>
        <label
          className={showYears ? 'switch switch-disabled' : 'switch'}
          title={showYears ? "A year's values have no significance to filter by." : undefined}
        >
          <input
            type="checkbox"
            checked={significantOnly && !showYears}
            disabled={showYears}
            onChange={(event) => setSignificantOnly(event.target.checked)}
          />
          <span className="track" aria-hidden="true" />
          Significant only
        </label>
        <label
          className={showLayer ? 'switch' : 'switch switch-disabled'}
          title="Draws each cell of the layer as a column. The taller the column, the larger the trend, rising or falling."
        >
          <input
            type="checkbox"
            checked={relief}
            disabled={!showLayer}
            onChange={(event) => {
              setRelief(event.target.checked);
              if (event.target.checked) {
                // Seen from straight above a column is only its lid, so the view leans over to show its height, as on the desktop.
                setTilt(true);
              }
            }}
          />
          <span className="track" aria-hidden="true" />
          Relief
        </label>
        <label className="switch" title="Leans the view over, so that a column of the relief is seen from its side.">
          <input type="checkbox" checked={tilt} onChange={(event) => setTilt(event.target.checked)} />
          <span className="track" aria-hidden="true" />
          Tilt
        </label>
        <label className="switch" title="Unrolls the globe into a flat map. Drag the map to move it, and scroll or pinch to zoom.">
          <input type="checkbox" checked={flat} onChange={(event) => setFlat(event.target.checked)} />
          <span className="track" aria-hidden="true" />
          Flat map
        </label>
        <label
          className={showLayer ? 'switch' : 'switch switch-disabled'}
          title="Shows one calendar year at a time: each cell's mean for the year against its own normal. No test is run on it and it is not a finding."
        >
          <input
            type="checkbox"
            checked={showYears}
            disabled={!showLayer}
            onChange={(event) => {
              setYearsNote(null);
              setShowYears(event.target.checked);
            }}
          />
          <span className="track" aria-hidden="true" />
          Years
        </label>
        <label
          className={flat ? 'switch switch-disabled' : 'switch'}
          title={flat ? `Not shown on the flat map: the orbits are drawn round the globe. ${satelliteCaveat}` : satelliteCaveat}
        >
          <input type="checkbox" checked={showSatellites} disabled={flat} onChange={(event) => setShowSatellites(event.target.checked)} />
          <span className="track" aria-hidden="true" />
          Satellites
        </label>
        </div>
        <PlaceSearch onGo={goToPlace} />
        <button
          type="button"
          className="button narrow-only"
          aria-expanded={optionsOpen}
          aria-controls="map-options"
          onClick={() => setOptionsOpen((open) => !open)}
        >
          Map options
        </button>
      </div>

      {yearsNote && (
        <p className="toolbar-note" role="status">
          {yearsNote}
        </p>
      )}

      {/* A presenter never takes a zone down again, so the person can. It is here, not on the globe, to be in reach whichever way the globe is turned. */}
      {presentedZone && (
        <p className="toolbar-zone">
          <span>
            Zone shown by the presenter: {presentedZone.name}. It is an outline on the globe and is not saved.
          </span>
          <button type="button" className="button" onClick={clearPresentedZone}>
            Remove it
          </button>
        </p>
      )}

      <main className="app-main">
        <section className="globe-area" aria-label="Globe" ref={globeArea}>
          <GlobeView
            results={results}
            layerVariable={layerVariable}
            showLayer={showLayer}
            significantOnly={significantOnly}
            relief={relief}
            tilt={tilt}
            flat={flat}
            years={yearsState.status === 'ready' ? yearsState.years : null}
            cellMask={zones.mask}
            kind={kind}
            variable={variable}
            goTo={placeToShow}
            zone={zoneOnGlobe}
            sketch={zones.drawing}
            onSketchPoint={zones.addPoint}
            onSketchUndo={zones.undoPoint}
            onSketchFinish={finishZone}
            onSketchCancel={zones.cancelDrawing}
            selected={selected}
            showSatellites={showSatellites}
            onFindingClick={selectFinding}
            highlight={highlight}
            onClearHighlight={clearHighlight}
            onBackToDetective={chatOpen ? null : openChat}
          />
        </section>

        <section className="card findings-card" aria-label="Findings" ref={findingsCard}>
          {selected && evidenceOpen ? (
            <EvidencePanel
              finding={selected}
              results={results}
              onBack={() => setEvidenceOpen(false)}
              onAsk={askAbout}
              onOpenFinding={openFinding}
            />
          ) : (
            <>
              <div className="card-title">
                <div className="segmented side-tabs" role="tablist" aria-label="Beside the globe">
                  <button role="tab" aria-selected={sideTab === 'findings'} className={sideTab === 'findings' ? 'selected' : ''} onClick={() => setSideTab('findings')}>
                    Findings
                  </button>
                  <button role="tab" aria-selected={sideTab === 'zones'} className={sideTab === 'zones' ? 'selected' : ''} onClick={() => setSideTab('zones')}>
                    Zones
                  </button>
                </div>
                {selected && (
                  <button className="button" onClick={() => setEvidenceOpen(true)}>
                    Evidence for #{selected.rank}
                  </button>
                )}
              </div>
              {sideTab === 'zones' ? (
                <ZonesPanel zones={zones} runId={runId} findingsInZone={inZone.length} onDraw={drawZone} />
              ) : (
                <>
              {counts && (
                <p className="scan-line">
                  {count(counts.tests_significant)} of {count(counts.tests_run)} tests significant after correction,{' '}
                  {count(findings.length)} findings listed.
                </p>
              )}
              <div className="segmented findings-kinds" role="radiogroup" aria-label="Kind of finding">
                {kindChoices.map((choice) => (
                  <button
                    key={choice.label}
                    role="radio"
                    aria-checked={choice.kind === kind}
                    className={choice.kind === kind ? 'selected' : ''}
                    onClick={() => setKind(choice.kind)}
                  >
                    {choice.label}
                  </button>
                ))}
              </div>
              {variableChoices.length > 1 && (
                <div className="segmented findings-kinds" role="radiogroup" aria-label="Variable of finding">
                  <button role="radio" aria-checked={variable === null} className={variable === null ? 'selected' : ''} onClick={() => chooseVariable(null)}>
                    All variables
                  </button>
                  {variableChoices.map((id) => (
                    <button
                      key={id}
                      role="radio"
                      aria-checked={id === variable}
                      className={id === variable ? 'selected' : ''}
                      onClick={() => chooseVariable(id)}
                    >
                      {labelText(id)}
                    </button>
                  ))}
                </div>
              )}
              {zones.chosenZone && zones.chosen && !zones.chosen.refusal && (
                <p className="zones-limit numeric">
                  <span>
                    {zones.chosenZone.name}: {zones.chosen.cells.length === 1 ? '1 grid cell' : `${count(zones.chosen.cells.length)} grid cells`},{' '}
                    {inZone.length === 1 ? '1 finding' : `${count(inZone.length)} findings`}.
                  </span>
                  <button type="button" className="button" onClick={() => zones.choose(null)}>
                    Whole globe
                  </button>
                </p>
              )}
              {(kind !== null || variable !== null) && (
                <p className="scan-line numeric">
                  {[kind !== null ? kindChoices.find((choice) => choice.kind === kind)?.label : null, variable !== null ? labelText(variable) : null]
                    .filter(Boolean)
                    .join(', ')}
                  , {count(listed.length)} of {count(inZone.length)}
                </p>
              )}
              <FindingsList findings={listed} selectedId={selectedId} onSelect={selectFinding} />
                </>
              )}
            </>
          )}
        </section>
      </main>

      {/* On a phone: one part on screen at a time. Hidden on a wide screen, where everything is in view at once. */}
      <nav className="phone-tabs" aria-label="Parts of the viewer">
        {(['globe', 'findings'] as const).map((tab) => (
          <button
            key={tab}
            type="button"
            aria-current={!chatOpen && phoneTab === tab ? 'page' : undefined}
            className={!chatOpen && phoneTab === tab ? 'selected' : ''}
            onClick={() => {
              setChatOpen(false);
              setOptionsOpen(false);
              setPhoneTab(tab);
            }}
          >
            {tab === 'globe' ? 'Globe' : 'Findings'}
          </button>
        ))}
        <button
          type="button"
          aria-current={chatOpen ? 'page' : undefined}
          className={chatOpen ? 'selected' : ''}
          onClick={() => {
            setOptionsOpen(false);
            setChatOpen(true);
          }}
        >
          Detective
        </button>
      </nav>

      {/* Kept in the page while closed, so the conversation is still there when it is opened again. */}
      <aside className={chatOpen ? 'chat-panel' : 'chat-panel chat-panel-closed'} aria-label="The Detective" aria-hidden={!chatOpen}>
        <DetectiveChat
          findings={findings}
          runId={results.run?.id ?? null}
          synthetic={results.synthetic === true}
          attached={attached}
          onDetach={() => setAttached(null)}
          onCitationClick={showCitation}
          onAnswerCited={onAnswerCited}
          onShowCitation={onShowCitation}
          onShowStep={showStep}
          onClose={() => setChatOpen(false)}
        />
      </aside>

      {scanOpen && <ScanSummary results={results} onClose={closeScan} />}
    </div>
  );
}
