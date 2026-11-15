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
import { OverviewPage } from './overview/OverviewPage';
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

/** Below this width one part fills the screen at a time, and the pages are chosen from a menu along the bottom. */
const narrowScreen = '(max-width: 899px)';

/** The pages, as the desktop app has them, in its order. */
type Page = 'overview' | 'map' | 'evidence' | 'detective';
const pages: { id: Page; label: string }[] = [
  { id: 'overview', label: 'Overview' },
  { id: 'map', label: 'Map' },
  { id: 'evidence', label: 'Evidence' },
  { id: 'detective', label: 'Detective' },
];

/** A page's mark in the menus: a house, a globe, a chart and a speech bubble, drawn in the colour of the text beside them. */
function PageIcon({ page }: { page: Page }) {
  return (
    <svg className="page-icon" width="20" height="20" viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      {page === 'overview' && <path d="M3 9.5 10 3l7 6.5V17h-4.5v-5h-5v5H3z" />}
      {page === 'map' && (
        <>
          <circle cx="10" cy="10" r="7" />
          <ellipse cx="10" cy="10" rx="3" ry="7" />
          <path d="M3 10h14" />
        </>
      )}
      {page === 'evidence' && <path d="M3 3v14h14M6.5 13l3-4 3 2 3.5-5.5" />}
      {page === 'detective' && <path d="M4 4h12v9H9.5L6 16v-3H4z" />}
    </svg>
  );
}

/** Whether the screen is narrow enough for the phone layout, kept up to date as the window is resized or turned. */
function useNarrowScreen(): boolean {
  const [narrow, setNarrow] = useState(() => window.matchMedia(narrowScreen).matches);
  useEffect(() => {
    const query = window.matchMedia(narrowScreen);
    const onChange = () => setNarrow(query.matches);
    query.addEventListener('change', onChange);
    return () => query.removeEventListener('change', onChange);
  }, []);
  return narrow;
}

/** Takes a part that is not on screen out of reach of the keyboard and of screen readers. */
function away(hidden: boolean): object {
  return hidden ? { inert: '' } : {};
}

/**
 * The viewer's shell: the desktop app's four pages and the state they share. The Overview, the
 * Map (the globe with a variable's trend layer and the ranked findings beside it), the Evidence
 * page and the Detective, which is also a panel that slides over another page, with "About
 * this scan" in another. It is a read-only companion to the desktop app and shows the same
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
  // The page on show. It opens on the Overview, as the desktop app does.
  const [page, setPage] = useState<Page>('overview');
  const pageNow = useRef(page);
  pageNow.current = page;
  // The page to go back to when the Detective's own page is closed.
  const pageBefore = useRef<Page>('overview');
  const narrow = useNarrowScreen();
  // The globe is made the first time the Map is opened and kept after that, as the desktop app
  // makes its pages. A phone that only reads the Overview never has to fetch the Earth.
  const [mapVisited, setMapVisited] = useState(false);
  useEffect(() => {
    if (page === 'map') {
      setMapVisited(true);
    }
  }, [page]);
  // On a phone the globe's switches sit in a panel that this opens, so the globe keeps the screen.
  const [optionsOpen, setOptionsOpen] = useState(false);
  const [attached, setAttached] = useState<Finding | null>(null);
  const [scanOpen, setScanOpen] = useState(false);
  // What the Detective has pointed out on the globe: an answer's cited findings, or one of its lookups.
  const [highlight, setHighlight] = useState<GlobeHighlight | null>(null);
  const layerBar = useRef<HTMLDivElement>(null);
  const globeArea = useRef<HTMLElement>(null);
  const findingsCard = useRef<HTMLElement>(null);

  /** Goes to a page from one of the menus. */
  const goTo = useCallback((next: Page) => {
    if (next !== 'detective') {
      pageBefore.current = next;
    }
    setOptionsOpen(false);
    setPage(next);
  }, []);

  /**
   * Shows the globe, or the findings, when something is put there from elsewhere. The globe is
   * on the Map page. The findings are beside it there on a wide screen, so that page stays if
   * it is the one on show; otherwise, and always on a phone, they are on the Evidence page.
   */
  const bringToScreen = useCallback((part: 'globe' | 'findings') => {
    const phone = window.matchMedia(narrowScreen).matches;
    const from = pageNow.current;
    const to: Page = part === 'globe' ? 'map' : phone || from !== 'map' ? 'evidence' : 'map';
    if (phone) {
      setOptionsOpen(false);
    } else if (from === 'detective') {
      // Leaving the Detective's page for what it pointed at: the conversation comes along in its panel.
      setChatOpen(true);
    }
    pageBefore.current = to;
    setPage(to);
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

  /** A citation in a Detective answer: open that finding's evidence, which goes to the page that shows it. */
  const showCitation = useCallback(
    (findingId: string) => {
      openFinding(findingId);
    },
    [openFinding],
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
        bringToScreen('globe');
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
      bringToScreen('globe');
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
  /** Opens the Detective: its own page on a phone, its panel over the page on a wide screen. */
  const openChat = useCallback(() => {
    if (window.matchMedia(narrowScreen).matches) {
      setPage('detective');
    } else {
      setChatOpen(true);
    }
  }, []);
  const closeScan = useCallback(() => setScanOpen(false), []);

  // With many layers the row of them scrolls on a phone: keep the chosen one in sight.
  useEffect(() => {
    layerBar.current?.querySelector('.selected')?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
  }, [layerVariable]);

  const askAbout = useCallback(
    (finding: Finding) => {
      setAttached(finding);
      openChat();
    },
    [openChat],
  );

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
  // On a phone the Evidence page is the findings beside the globe, filling the screen. On a wide screen it is a page of its own.
  const shown: Page = page === 'evidence' && narrow ? 'map' : page;
  const chatAsPage = page === 'detective' && !narrow;
  const chatShown = page === 'detective' || (!narrow && chatOpen);
  const chatBeside = !narrow && chatOpen && page !== 'detective';

  // What stands above a list of findings: how many there are, what narrows them, and what they are narrowed to.
  const findingsHead = (
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
    </>
  );

  return (
    <div className={`app page-${page} phone-tab-${page === 'evidence' ? 'findings' : 'globe'}${chatBeside ? ' app-chat-open' : ''}`}>
      <header className="app-header">
        <div className="app-title">
          <h1>VIGIL</h1>
          <p>Earth system trend detective</p>
        </div>
        {/* On a wide screen the pages are chosen here. On a phone they are in the menu along the bottom. */}
        <nav className="segmented app-nav wide-only" aria-label="Pages">
          {pages.map(({ id, label }) => (
            <button key={id} type="button" aria-current={page === id ? 'page' : undefined} className={page === id ? 'selected' : ''} onClick={() => goTo(id)}>
              <PageIcon page={id} />
              {label}
            </button>
          ))}
        </nav>
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
          {/* On the Detective's own page there is nothing for this to open. It keeps its place, so the row of pages does not shift. */}
          <button
            className={page === 'detective' ? 'button primary wide-only unseen' : 'button primary wide-only'}
            onClick={() => setChatOpen((open) => !open)}
            aria-expanded={chatOpen}
            disabled={page === 'detective'}
          >
            Ask the Detective
          </button>
        </div>
      </header>

      {/* The pages. One is on show and the rest stay as they were, out of sight and out of reach, so the globe keeps its place and a list keeps its scroll. */}
      <div className="app-pages">
      <div className={shown === 'overview' ? 'page page-overview page-shown' : 'page page-overview'} {...away(shown !== 'overview')}>
        <OverviewPage results={results} onFinding={selectFinding} onOpenMap={() => goTo('map')} onOpenEvidence={() => goTo('evidence')} />
      </div>

      <div className={shown === 'map' ? 'page page-map page-shown' : 'page page-map'} {...away(shown !== 'map')}>
      <div className="toolbar">
        {grids.length > 0 && (
          // On a wide screen the layer is chosen from a menu: a scan can have eight, and a row of them crowds
          // out the switches beside it. On a phone the row below is used, where it has a line to itself.
          <select
            className="layer-picker wide-only"
            aria-label="Layer variable"
            title="The variable the layer shows"
            value={layerVariable ?? ''}
            onChange={(event) => {
              setYearsNote(null);
              setLayerVariable(event.target.value);
            }}
          >
            {grids.map((grid) => (
              <option key={grid.variable} value={grid.variable}>
                {labelText(grid.variable)}
              </option>
            ))}
          </select>
        )}
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
          {mapVisited && (
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
            onBackToDetective={chatShown ? null : openChat}
          />
          )}
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
              {findingsHead}
              <FindingsList findings={listed} selectedId={selectedId} onSelect={selectFinding} />
                </>
              )}
            </>
          )}
        </section>
      </main>

      </div>

      {/* The Evidence page on a wide screen: the findings on the left and, beside them, everything the results say about the chosen one. */}
      {!narrow && (
        <div className={shown === 'evidence' ? 'page page-evidence page-shown' : 'page page-evidence'} {...away(shown !== 'evidence')}>
          <section className="card evidence-list" aria-label="Findings">
            <div className="card-title">
              <h2>Findings</h2>
            </div>
            {findingsHead}
            <FindingsList findings={listed} selectedId={selectedId} onSelect={selectFinding} place="the Evidence page" />
          </section>
          <section className="card evidence-view" aria-label="Evidence">
            {selected ? (
              <EvidencePanel finding={selected} results={results} onShowOnMap={() => goTo('map')} onAsk={askAbout} onOpenFinding={openFinding} />
            ) : (
              <div className="evidence-none">
                <h2>Evidence</h2>
                <p>Every finding with its series, its uncertainty, its method, its sources and what it cannot say. Choose one from the list.</p>
              </div>
            )}
          </section>
        </div>
      )}

      {/* The Detective: a page of its own, or a panel over another page on a wide screen. Kept in the page while it is away, so the conversation is still there when it is opened again. */}
      <aside
        className={`chat-panel${chatShown ? '' : ' chat-panel-closed'}${chatAsPage ? ' chat-page' : ''}`}
        aria-label="The Detective"
        aria-hidden={!chatShown}
      >
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
          onClose={() => (page === 'detective' ? goTo(pageBefore.current) : setChatOpen(false))}
        />
      </aside>
      </div>

      {/* On a phone: the pages, in a menu along the bottom. Hidden on a wide screen, where they are in the header. */}
      <nav className="phone-tabs" aria-label="Pages">
        {pages.map(({ id, label }) => (
          <button key={id} type="button" aria-current={page === id ? 'page' : undefined} className={page === id ? 'selected' : ''} onClick={() => goTo(id)}>
            <PageIcon page={id} />
            <span>{label}</span>
          </button>
        ))}
      </nav>

      {scanOpen && <ScanSummary results={results} onClose={closeScan} />}
    </div>
  );
}
