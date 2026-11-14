import type { GridLayer } from '../contract';
import { colourAt, opacityAt } from './colourScale';

const PixelsPerCell = 8;
const MaxCanvasWidth = 4096;

/**
 * Paints a layer onto an equirectangular canvas of the whole globe (180°W to 180°E, north at
 * the top). Each scanned cell is a flat block, never blended with its neighbours, because each
 * one is a separate test. Skipped cells, and with significantOnly the cells at or above the
 * threshold, stay clear. Slopes beyond the limit take the end colour.
 */
export function paintLayer(
  grid: GridLayer,
  limit: number,
  significantOnly: boolean,
  threshold: number,
): HTMLCanvasElement {
  const cellDegrees = (grid.lon_max - grid.lon_min) / Math.max(1, grid.columns);
  const width = Math.min(MaxCanvasWidth, Math.max(2, Math.round((360 / cellDegrees) * PixelsPerCell)));
  const height = width / 2;
  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = height;
  const context = canvas.getContext('2d');
  if (!context) {
    return canvas;
  }

  const latStep = (grid.lat_max - grid.lat_min) / Math.max(1, grid.rows);
  const lonStep = (grid.lon_max - grid.lon_min) / Math.max(1, grid.columns);
  // Both edges of every cell are rounded the same way, so neighbours meet with no gap or overlap.
  const x = (lon: number) => Math.round(((lon + 180) / 360) * width);
  const y = (lat: number) => Math.round(((90 - lat) / 180) * height);

  for (let row = 0; row < grid.rows; row++) {
    const top = y(grid.lat_min + (row + 1) * latStep);
    const bottom = y(grid.lat_min + row * latStep);
    for (let column = 0; column < grid.columns; column++) {
      const slope = grid.slope_per_decade[row]?.[column];
      if (slope == null) {
        continue;
      }
      if (significantOnly) {
        const adjusted = grid.p_adjusted[row]?.[column];
        if (adjusted == null || !(adjusted < threshold)) {
          continue;
        }
      }
      const t = (slope + limit) / (2 * limit);
      const [r, g, b] = colourAt(t);
      context.fillStyle = `rgba(${r}, ${g}, ${b}, ${opacityAt(t)})`;
      const left = x(grid.lon_min + column * lonStep);
      const right = x(grid.lon_min + (column + 1) * lonStep);
      context.fillRect(left, top, right - left, bottom - top);
    }
  }
  return canvas;
}
