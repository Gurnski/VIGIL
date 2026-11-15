import { useCallback, useEffect, useMemo, useState } from 'react';
import type { ResultsDocument } from '../contract';
import { boundaryLines, cellId, cellsIn, refusal, shortWay } from './geometry';
import type { ZoneCell, ZoneGrid, ZonePoint } from './geometry';
import { loadZones, newZoneId, storeZones } from './store';
import type { SavedZone } from './store';

/** An outline with the grid cells it takes in on the scan on screen. */
export interface ZoneOnGrid {
  points: ZonePoint[];
  cells: ZoneCell[];
  /** The cells' ids, as the contract writes them. */
  cellIds: string[];
  /** The stepped edge of the cells, as lines of two [latitude, longitude] points. */
  edges: [ZonePoint, ZonePoint][];
  /** Why it cannot be a zone, or null. */
  refusal: string | null;
}

/** The scan grid of the results on screen: the run record's, or failing that the first layer's own. */
function gridOf(results: ResultsDocument | null): ZoneGrid | null {
  const settings = results?.run?.settings.grid;
  if (settings) {
    return settings;
  }
  const layer = results?.grids?.[0];
  if (!layer || layer.columns <= 0) {
    return null;
  }
  return { cell_degrees: (layer.lon_max - layer.lon_min) / layer.columns, rows: layer.rows, columns: layer.columns, lat_min: layer.lat_min, lon_min: layer.lon_min };
}

function onGrid(points: ZonePoint[], grid: ZoneGrid | null): ZoneOnGrid {
  const cells = grid ? cellsIn(points, grid) : [];
  return {
    points,
    cells,
    cellIds: cells.map((cell) => cellId(cell.row, cell.column)),
    edges: grid ? boundaryLines(cells, grid) : [],
    refusal: grid ? refusal(points, cells) : 'These results describe no grid, so a zone has no cells to take in.',
  };
}

/**
 * Everything about zones that the shell needs: the saved zones, the one chosen, an outline
 * being drawn and one waiting for its name. A zone is only a name and an outline. Its cells
 * are worked out here against the scan on screen, and nothing else about it is: a zone's
 * series and trends come from the service.
 */
export function useZones(results: ResultsDocument | null) {
  const grid = useMemo(() => gridOf(results), [results]);
  const [saved, setSaved] = useState<SavedZone[]>(loadZones);
  const [chosenId, setChosenId] = useState<string | null>(null);
  // The points laid so far, while drawing. Null when not drawing.
  const [drawing, setDrawing] = useState<ZonePoint[] | null>(null);
  // A finished outline waiting to be named and saved, or discarded.
  const [pendingPoints, setPendingPoints] = useState<ZonePoint[] | null>(null);
  // Set when the browser would not keep the zones.
  const [notKept, setNotKept] = useState(false);

  const chosenZone = useMemo(() => saved.find((zone) => zone.id === chosenId) ?? null, [saved, chosenId]);
  const chosen = useMemo(() => (chosenZone ? onGrid(chosenZone.points, grid) : null), [chosenZone, grid]);
  const pending = useMemo(() => (pendingPoints ? onGrid(pendingPoints, grid) : null), [pendingPoints, grid]);

  /** Whether a cell is in the chosen zone. Null when no zone is chosen, so everything is shown. */
  const mask = useMemo(() => {
    // An outline that is not a zone on this grid limits nothing: the panel says why instead.
    if (!chosen || !grid || chosen.refusal) {
      return null;
    }
    const members = new Set(chosen.cells.map((cell) => cell.row * grid.columns + cell.column));
    return (row: number, column: number) => members.has(row * grid.columns + column);
  }, [chosen, grid]);
  const chosenCellIds = useMemo(() => (chosen && !chosen.refusal ? new Set(chosen.cellIds) : null), [chosen]);

  const keep = useCallback((zones: SavedZone[]) => {
    setSaved(zones);
    setNotKept(!storeZones(zones));
  }, []);

  const startDrawing = useCallback(() => {
    setPendingPoints(null);
    setDrawing([]);
  }, []);
  /** A point laid on the globe. Its longitude is carried on from the point before, the short way round. */
  const addPoint = useCallback((latitude: number, longitude: number) => {
    setDrawing((points) => {
      if (!points) {
        return points;
      }
      const before = points[points.length - 1];
      return [...points, [latitude, before ? before[1] + shortWay(longitude - before[1]) : longitude]];
    });
  }, []);
  const undoPoint = useCallback(() => setDrawing((points) => (points ? points.slice(0, -1) : points)), []);
  const cancelDrawing = useCallback(() => setDrawing(null), []);
  /** Closes the outline. It then waits for a name. */
  const finishDrawing = useCallback(() => {
    if (drawing && drawing.length >= 3) {
      setPendingPoints(drawing);
      setDrawing(null);
    }
  }, [drawing]);
  const discardPending = useCallback(() => setPendingPoints(null), []);

  /** The first of "Zone 1", "Zone 2" and so on that no saved zone has. */
  const defaultName = useMemo(() => {
    const taken = new Set(saved.map((zone) => zone.name.trim().toLowerCase()));
    let number = 1;
    while (taken.has(`zone ${number}`)) {
      number++;
    }
    return `Zone ${number}`;
  }, [saved]);

  /** Saves the outline that is waiting, under a name, and chooses it. */
  const savePending = useCallback(
    (name: string) => {
      if (!pending || pending.refusal) {
        return;
      }
      const zone: SavedZone = { id: newZoneId(), name: name.trim() || defaultName, created: new Date().toISOString(), points: pending.points };
      keep([...saved, zone]);
      setPendingPoints(null);
      setChosenId(zone.id);
    },
    [pending, saved, defaultName, keep],
  );
  const remove = useCallback(
    (id: string) => {
      keep(saved.filter((zone) => zone.id !== id));
      setChosenId((current) => (current === id ? null : current));
    },
    [saved, keep],
  );

  // Escape abandons an outline that is being drawn.
  const isDrawing = drawing !== null;
  useEffect(() => {
    if (!isDrawing) {
      return;
    }
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setDrawing(null);
      }
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [isDrawing]);

  return {
    /** False when the results describe no grid, so no zone can be made. */
    possible: grid !== null,
    saved,
    notKept,
    chosenZone,
    chosen,
    chosenCellIds,
    mask,
    choose: setChosenId,
    remove,
    drawing,
    startDrawing,
    addPoint,
    undoPoint,
    finishDrawing,
    cancelDrawing,
    pending,
    defaultName,
    savePending,
    discardPending,
  };
}

export type Zones = ReturnType<typeof useZones>;
