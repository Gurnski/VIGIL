// The geometry of a drawn zone: which grid cells are inside its outline. This is layout, not
// statistics: anything measured over a zone comes from the Python service. The web twin of
// VIGIL/VIGIL/Data/ZoneGeometry.cs, and the same rule as the service's (the contract's "A zone"):
// change the rule in all three or in none.
//
// An outline's edges are straight lines in latitude and longitude, it is closed from its last
// point to its first, and a grid cell belongs to the zone when the cell's centre is inside, by
// the even-odd rule. Longitudes along an outline are continuous, so one that crosses the date
// line runs past 180 or below -180 and does not jump.

/** A point of an outline: [latitude, longitude]. */
export type ZonePoint = [number, number];

/** The scan grid, as the run record describes it. */
export interface ZoneGrid {
  cell_degrees: number;
  rows: number;
  columns: number;
  lat_min: number;
  lon_min: number;
}

export interface ZoneCell {
  row: number;
  column: number;
}

/** A change in longitude folded into -180 to 180. */
export function shortWay(delta: number): number {
  let folded = (delta + 180) % 360;
  if (folded < 0) {
    folded += 360;
  }
  return folded - 180;
}

/** Makes a path's longitudes continuous: each is moved by whole turns to be the short way round from the point before. */
export function unwrap(path: ZonePoint[]): ZonePoint[] {
  const result: ZonePoint[] = [];
  for (const [latitude, longitude] of path) {
    const before = result[result.length - 1];
    result.push([latitude, before ? before[1] + shortWay(longitude - before[1]) : longitude]);
  }
  return result;
}

/**
 * True when a closed outline goes all the way round a pole: its last point ends up a whole
 * turn of longitude from its first. Such an outline has no inside in latitude and longitude,
 * so it cannot be a zone.
 */
export function wrapsPole(outline: ZonePoint[]): boolean {
  if (outline.length < 3) {
    return false;
  }
  const longitudes = outline.map((point) => point[1]);
  const west = Math.min(...longitudes);
  const east = Math.max(...longitudes);
  return Math.abs(outline[outline.length - 1][1] - outline[0][1]) > 180 || east - west >= 360;
}

/** Whether a point is inside a closed outline, by the even-odd rule. */
export function contains(outline: ZonePoint[], latitude: number, longitude: number): boolean {
  if (outline.length < 3) {
    return false;
  }
  let west = Infinity;
  let east = -Infinity;
  for (const point of outline) {
    west = Math.min(west, point[1]);
    east = Math.max(east, point[1]);
  }
  // The outline's longitudes may sit outside -180 to 180, so bring the point into the same turn.
  const lon = longitude + 360 * Math.ceil((west - longitude) / 360);
  if (lon > east) {
    return false;
  }
  let inside = false;
  for (let i = 0, j = outline.length - 1; i < outline.length; j = i++) {
    const a = outline[i];
    const b = outline[j];
    if (a[0] > latitude !== b[0] > latitude && lon < ((b[1] - a[1]) * (latitude - a[0])) / (b[0] - a[0]) + a[1]) {
      inside = !inside;
    }
  }
  return inside;
}

/** The grid cells whose centres are inside the outline, row by row from the south. */
export function cellsIn(outline: ZonePoint[], grid: ZoneGrid): ZoneCell[] {
  const cells: ZoneCell[] = [];
  if (outline.length < 3 || !(grid.cell_degrees > 0)) {
    return cells;
  }
  const latitudes = outline.map((point) => point[0]);
  const south = Math.min(...latitudes);
  const north = Math.max(...latitudes);
  for (let row = 0; row < grid.rows; row++) {
    const latitude = grid.lat_min + (row + 0.5) * grid.cell_degrees;
    if (latitude < south || latitude > north) {
      continue;
    }
    for (let column = 0; column < grid.columns; column++) {
      if (contains(outline, latitude, grid.lon_min + (column + 0.5) * grid.cell_degrees)) {
        cells.push({ row, column });
      }
    }
  }
  return cells;
}

/** The contract's id for a cell, such as r60c112. */
export function cellId(row: number, column: number): string {
  return `r${String(row).padStart(2, '0')}c${String(column).padStart(3, '0')}`;
}

/**
 * The stepped edge of the area a zone really covers, for drawing beside the outline that was
 * drawn: the sides of its cells that face a cell outside the set. Sides that run on from one
 * another along a parallel or a meridian are joined, so the globe has fewer lines to draw.
 * Each line is two [latitude, longitude] points.
 */
export function boundaryLines(cells: ZoneCell[], grid: ZoneGrid): [ZonePoint, ZonePoint][] {
  const size = grid.cell_degrees;
  const wraps = Math.abs(grid.columns * size - 360) < 1e-6;
  const members = new Set(cells.map((cell) => cell.row * grid.columns + cell.column));
  const has = (row: number, column: number) => {
    const turned = wraps ? ((column % grid.columns) + grid.columns) % grid.columns : column;
    return row >= 0 && row < grid.rows && turned >= 0 && turned < grid.columns && members.has(row * grid.columns + turned);
  };
  // Sides along a parallel, keyed by the grid line they lie on, as the columns they span. Likewise along a meridian.
  const alongParallel = new Map<number, number[]>();
  const alongMeridian = new Map<number, number[]>();
  const note = (map: Map<number, number[]>, line: number, at: number) => {
    const list = map.get(line);
    if (list) {
      list.push(at);
    } else {
      map.set(line, [at]);
    }
  };
  for (const { row, column } of cells) {
    if (!has(row + 1, column)) note(alongParallel, row + 1, column);
    if (!has(row - 1, column)) note(alongParallel, row, column);
    if (!has(row, column - 1)) note(alongMeridian, column, row);
    if (!has(row, column + 1)) note(alongMeridian, column + 1, row);
  }
  const lines: [ZonePoint, ZonePoint][] = [];
  const runs = (places: number[], add: (from: number, to: number) => void) => {
    const sorted = [...new Set(places)].sort((a, b) => a - b);
    let start = sorted[0];
    let end = start;
    for (let i = 1; i <= sorted.length; i++) {
      if (i < sorted.length && sorted[i] === end + 1) {
        end = sorted[i];
        continue;
      }
      add(start, end + 1);
      start = sorted[i];
      end = start;
    }
  };
  for (const [line, columns] of alongParallel) {
    const latitude = grid.lat_min + line * size;
    runs(columns, (from, to) => lines.push([[latitude, grid.lon_min + from * size], [latitude, grid.lon_min + to * size]]));
  }
  for (const [line, rows] of alongMeridian) {
    const longitude = grid.lon_min + line * size;
    runs(rows, (from, to) => lines.push([[grid.lat_min + from * size, longitude], [grid.lat_min + to * size, longitude]]));
  }
  return lines;
}

/** Why an outline cannot be saved as a zone, in words fit to show, or null when it can. */
export function refusal(outline: ZonePoint[], cells: ZoneCell[]): string | null {
  if (outline.length < 3) {
    return 'An outline needs at least three points.';
  }
  if (wrapsPole(outline)) {
    return 'The outline goes all the way round a pole, so it has no inside. Draw an area that does not circle the pole.';
  }
  if (cells.length === 0) {
    return 'The outline holds no centre of a grid cell, so there is nothing in it to show. Draw a larger area.';
  }
  return null;
}
