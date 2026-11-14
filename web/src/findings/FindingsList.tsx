import { memo, useEffect, useId, useRef } from 'react';
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

/** The line under a summary: "Cascade, 8.8°S 51.3°W, GLDAS Noah and Terra MODIS". */
function detailText(finding: Finding): string {
  let text = `${labelText(finding.kind)}, ${placeText(finding.cell)}`;
  const missions = [...new Set(finding.variables.map((variable) => variable.mission).filter((mission) => mission.length > 0))];
  if (missions.length > 0) {
    text += `, ${missions.join(' and ')}`;
  }
  return finding.significant ? text : `${text}, not significant`;
}

interface FindingRowProps {
  finding: Finding;
  elementId: string;
  selected: boolean;
  onSelect: (finding: Finding) => void;
}

// Memoised so that changing the selection redraws two rows, not thousands.
const FindingRow = memo(function FindingRow({ finding, elementId, selected, onSelect }: FindingRowProps) {
  return (
    <li
      id={elementId}
      role="option"
      aria-selected={selected}
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
 */
export function FindingsList({ findings, selectedId, onSelect }: FindingsListProps) {
  const listRef = useRef<HTMLUListElement>(null);
  const idPrefix = useId();
  const elementId = (finding: Finding) => `${idPrefix}-${finding.id}`;

  // Also covers a selection made elsewhere, such as a click on a globe marker.
  useEffect(() => {
    if (selectedId !== null) {
      document.getElementById(`${idPrefix}-${selectedId}`)?.scrollIntoView({ block: 'nearest' });
    }
  }, [selectedId, idPrefix]);

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

  return (
    <ul
      ref={listRef}
      className={selectedId === null ? 'findings-list' : 'findings-list findings-has-selection'}
      role="listbox"
      aria-label="Findings, in rank order"
      aria-activedescendant={selectedId === null ? undefined : `${idPrefix}-${selectedId}`}
      tabIndex={0}
      onKeyDown={handleKeyDown}
    >
      {findings.map((finding) => (
        <FindingRow
          key={finding.id}
          finding={finding}
          elementId={elementId(finding)}
          selected={finding.id === selectedId}
          onSelect={onSelect}
        />
      ))}
    </ul>
  );
}
