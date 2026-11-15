import { memo, useEffect, useId, useLayoutEffect, useMemo, useRef, useState } from 'react';
import type { KeyboardEvent } from 'react';
import type { Finding } from '../contract';
import { labelText, placeText } from '../format';
import './findings.css';

export interface FindingsListProps {
  /** In rank order. Shown in the order given. */
  findings: Finding[];
  selectedId: string | null;
  onSelect: (finding: Finding) => void;
}

/** Rows moved by Page Up and Page Down. */
const pageStep = 5;

/** A row's height before any row has been measured: a two-line summary with its detail line. */
const defaultRowHeight = 88;
/** Rows are kept this far beyond the edges of the view at least, so a quick scroll does not show a gap. */
const minOverscan = 600;
/** Bringing a row into view settles in two or three moves. This stops a row that never settles. */
const maxBringTries = 8;

// Row heights as measured, by finding id, for the list's current width. Kept outside the
// component because the list is taken off the page while a finding's evidence is open, and
// should come back where it was without measuring again.
const measured = { width: 0, heights: new Map<string, number>(), estimate: defaultRowHeight, estimated: false };

/** The line under a summary: "Cascade, 8.8°S 51.3°W, GLDAS Noah and Terra MODIS". */
function detailText(finding: Finding): string {
  let text = `${labelText(finding.kind)}, ${placeText(finding.cell)}`;
  const missions = [...new Set(finding.variables.map((variable) => variable.mission).filter((mission) => mission.length > 0))];
  if (missions.length > 0) {
    text += `, ${missions.join(' and ')}`;
  }
  return finding.significant ? text : `${text}, not significant`;
}

/** The last row that starts at or above `y`, where `offsets[i]` is the top of row i. */
function rowAt(offsets: Float64Array, y: number): number {
  let low = 0;
  let high = offsets.length - 2;
  while (low < high) {
    const middle = (low + high + 1) >> 1;
    if (offsets[middle] <= y) {
      low = middle;
    } else {
      high = middle - 1;
    }
  }
  return Math.max(0, low);
}

interface FindingRowProps {
  finding: Finding;
  index: number;
  total: number;
  elementId: string;
  selected: boolean;
  onSelect: (finding: Finding) => void;
}

// Memoised so that scrolling and changing the selection redraw only the rows that changed.
const FindingRow = memo(function FindingRow({ finding, index, total, elementId, selected, onSelect }: FindingRowProps) {
  return (
    <li
      id={elementId}
      role="option"
      aria-selected={selected}
      // Only the rows near the view are in the page, so each says where it is in the whole list.
      aria-posinset={index + 1}
      aria-setsize={total}
      data-index={index}
      className={selected ? 'findings-row findings-selected' : 'findings-row'}
      onClick={() => onSelect(finding)}
    >
      <span className="findings-rank">{finding.rank}</span>
      <span className="findings-text">
        <span className={finding.significant ? 'findings-summary' : 'findings-summary findings-quiet'}>{finding.summary}</span>
        <span className="findings-detail">{detailText(finding)}</span>
      </span>
      <span className="findings-score">{finding.score.total.toFixed(2)}</span>
    </li>
  );
});

/**
 * The ranked list of findings, the web twin of the desktop app's FindingsList control: rank,
 * the service's summary in full, then the kind, place and missions underneath, with the
 * ranking score on the right. One listbox: the arrow keys move the selection.
 *
 * A scan has thousands of findings, so only the rows in and near the view are in the page.
 * Rows are as tall as their text: each is measured when it is first drawn, and the rest are
 * taken to be the average height until they are.
 */
export function FindingsList({ findings, selectedId, onSelect }: FindingsListProps) {
  const listRef = useRef<HTMLUListElement>(null);
  const idPrefix = useId();
  const elementId = (finding: Finding) => `${idPrefix}-${finding.id}`;
  // Where the list is scrolled to and how tall it is. Two plain numbers, so that setting either
  // to the value it already has changes nothing and draws nothing.
  const [viewTop, setViewTop] = useState(0);
  const [viewHeight, setViewHeight] = useState(0);
  const view = { top: viewTop, height: viewHeight };
  // Counts changes to the measured heights, so the rows' places are worked out again.
  const [measures, setMeasures] = useState(0);
  // The finding still to be brought into view, once its row has been drawn and measured.
  const bringIntoView = useRef<string | null>(null);
  // How many times the list has been moved for it. A row that cannot be reached is let go.
  const bringTries = useRef(0);
  // The list last shown, to tell a change of list (a kind was chosen) from a change of selection.
  const shownFindings = useRef(findings);

  // The top of every row, and of the end of the list, from the heights known so far.
  const offsets = useMemo(() => {
    const tops = new Float64Array(findings.length + 1);
    for (let i = 0; i < findings.length; i++) {
      tops[i + 1] = tops[i] + (measured.heights.get(findings[i].id) ?? measured.estimate);
    }
    return tops;
    // `measures` stands for the heights, which are kept outside React.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [findings, measures]);

  const readView = () => {
    const list = listRef.current;
    if (list) {
      setViewTop(list.scrollTop);
      setViewHeight(list.clientHeight);
    }
  };

  // The view's size, and a change of width, which changes where every summary wraps.
  useEffect(() => {
    const list = listRef.current;
    if (!list) {
      return;
    }
    const observer = new ResizeObserver(() => {
      if (list.clientWidth !== measured.width) {
        measured.width = list.clientWidth;
        measured.heights.clear();
        measured.estimated = false;
        setMeasures((count) => count + 1);
      }
      readView();
    });
    observer.observe(list);
    return () => observer.disconnect();
  }, [findings.length === 0]);

  // A selection made anywhere (a row, the keyboard, a globe marker) is brought into view. So is
  // the selected row when the list changes under it, as when a kind is chosen.
  useLayoutEffect(() => {
    bringTries.current = 0;
    bringIntoView.current = selectedId;
    if (selectedId === null || !findings.some((finding) => finding.id === selectedId)) {
      bringIntoView.current = null;
      // A different list with nothing of the old one to hold on to starts from its top.
      if (shownFindings.current !== findings && listRef.current) {
        listRef.current.scrollTop = 0;
      }
    }
    shownFindings.current = findings;
  }, [selectedId, findings]);

  // After every draw: measure the rows on the page, keep the view still while rows above it
  // turn out taller or shorter than estimated, and bring the chosen row into view.
  useLayoutEffect(() => {
    const list = listRef.current;
    if (!list) {
      return;
    }
    if (measured.width === 0) {
      measured.width = list.clientWidth;
    }
    const anchor = rowAt(offsets, list.scrollTop);
    let changed = false;
    let shift = 0;
    let sum = 0;
    let rows = 0;
    for (const element of list.querySelectorAll<HTMLElement>('[data-index]')) {
      const index = Number(element.dataset.index);
      const finding = findings[index];
      if (!finding) {
        continue;
      }
      const height = element.getBoundingClientRect().height;
      const known = measured.heights.get(finding.id);
      sum += height;
      rows++;
      if (known === undefined || Math.abs(known - height) > 0.25) {
        measured.heights.set(finding.id, height);
        changed = true;
        if (index < anchor) {
          shift += height - (known ?? measured.estimate);
        }
      }
    }
    // The first rows drawn give the height assumed for the rest. Set once for a width, so the
    // places of rows not yet seen do not drift as more are measured.
    if (!measured.estimated && rows > 0) {
      measured.estimate = sum / rows;
      measured.estimated = true;
      changed = true;
    }
    if (shift !== 0) {
      list.scrollTop += shift;
    }

    const wanted = bringIntoView.current;
    if (wanted !== null) {
      const index = findings.findIndex((finding) => finding.id === wanted);
      if (index < 0) {
        bringIntoView.current = null;
      } else if (!changed) {
        // Places are settled for this draw. Move the least distance that shows the whole row.
        const top = offsets[index];
        const bottom = offsets[index + 1];
        let target = list.scrollTop;
        if (top < target || bottom - top >= list.clientHeight) {
          // Above the view, or taller than it: show the row from its top.
          target = top;
        } else if (bottom > target + list.clientHeight) {
          target = bottom - list.clientHeight;
        }
        target = Math.max(0, Math.min(target, list.scrollHeight - list.clientHeight));
        if (Math.abs(target - list.scrollTop) > 1 && bringTries.current < maxBringTries) {
          bringTries.current++;
          list.scrollTop = target;
        } else if (measured.heights.has(wanted) || bringTries.current >= maxBringTries) {
          bringIntoView.current = null;
        }
      }
    }

    if (changed) {
      setMeasures((count) => count + 1);
    }
    readView();
  });

  if (findings.length === 0) {
    return <p className="findings-empty">No findings</p>;
  }

  const handleKeyDown = (event: KeyboardEvent<HTMLUListElement>) => {
    if (event.altKey || event.ctrlKey || event.metaKey) {
      return;
    }
    const current = findings.findIndex((finding) => finding.id === selectedId);
    let target: number;
    switch (event.key) {
      case 'ArrowUp':
        target = current - 1;
        break;
      case 'ArrowDown':
        target = current + 1;
        break;
      case 'PageUp':
        target = current - pageStep;
        break;
      case 'PageDown':
        target = current + pageStep;
        break;
      case 'Home':
        target = 0;
        break;
      case 'End':
        target = findings.length - 1;
        break;
      case 'Enter':
      case ' ':
        // On the selected row this selects it again, as a click does.
        target = current;
        break;
      default:
        return;
    }
    event.preventDefault();
    target = Math.min(Math.max(target, 0), findings.length - 1);
    const repeat = event.key === 'Enter' || event.key === ' ';
    if (target !== current || repeat) {
      onSelect(findings[target]);
    }
  };

  const overscan = Math.max(minOverscan, view.height);
  const first = rowAt(offsets, view.top - overscan);
  const last = rowAt(offsets, view.top + view.height + overscan);
  const selectedIndex = selectedId === null ? -1 : findings.findIndex((finding) => finding.id === selectedId);
  // The listbox can only point at a row that is on the page.
  const selectedDrawn = selectedIndex >= first && selectedIndex <= last;
  const stopBringing = () => {
    bringIntoView.current = null;
  };

  return (
    <ul
      ref={listRef}
      className={selectedId === null ? 'findings-list' : 'findings-list findings-has-selection'}
      role="listbox"
      aria-label="Findings, in rank order"
      aria-activedescendant={selectedDrawn ? `${idPrefix}-${selectedId}` : undefined}
      tabIndex={0}
      onKeyDown={handleKeyDown}
      onScroll={readView}
      // Scrolling by hand takes over from bringing a row into view.
      onWheel={stopBringing}
      onTouchStart={stopBringing}
    >
      {/* Stand in for the rows above and below the ones drawn, so the scroll bar covers the whole list. */}
      <li role="presentation" aria-hidden="true" className="findings-gap" style={{ height: offsets[first] }} />
      {findings.slice(first, last + 1).map((finding, position) => (
        <FindingRow
          key={finding.id}
          finding={finding}
          index={first + position}
          total={findings.length}
          elementId={elementId(finding)}
          selected={finding.id === selectedId}
          onSelect={onSelect}
        />
      ))}
      <li
        role="presentation"
        aria-hidden="true"
        className="findings-gap"
        style={{ height: Math.max(0, offsets[findings.length] - offsets[last + 1]) }}
      />
    </ul>
  );
}
