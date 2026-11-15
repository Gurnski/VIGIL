import { colourAt, opacityAt } from './colourScale';
import type { LayerField } from './field';

const PixelsPerCell = 8;
const MaxCanvasWidth = 4096;

/**
 * Paints a layer onto an equirectangular canvas of the whole globe (180°W to 180°E, north at
 * the top). Each cell with a value is a flat block, never blended with its neighbours, because
 * each one stands alone. Cells without one stay clear. Values beyond the limit take the end colour.
 */
export function paintLayer(field: LayerField): HTMLCanvasElement {
  const cellDegrees = (field.lon_max - field.lon_min) / Math.max(1, field.columns);
  const width = Math.min(MaxCanvasWidth, Math.max(2, Math.round((360 / cellDegrees) * PixelsPerCell)));
  const height = width / 2;
  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = height;
  const context = canvas.getContext('2d');
  if (!context) {
    return canvas;
  }

  const { limit } = field;
  const latStep = (field.lat_max - field.lat_min) / Math.max(1, field.rows);
  const lonStep = (field.lon_max - field.lon_min) / Math.max(1, field.columns);
  // Both edges of every cell are rounded the same way, so neighbours meet with no gap or overlap.
  const x = (lon: number) => Math.round(((lon + 180) / 360) * width);
  const y = (lat: number) => Math.round(((90 - lat) / 180) * height);

  for (let row = 0; row < field.rows; row++) {
    const top = y(field.lat_min + (row + 1) * latStep);
    const bottom = y(field.lat_min + row * latStep);
    for (let column = 0; column < field.columns; column++) {
      const value = field.value(row, column);
      if (value == null) {
        continue;
      }
      const t = (value + limit) / (2 * limit);
      const [r, g, b] = colourAt(t);
      context.fillStyle = `rgba(${r}, ${g}, ${b}, ${opacityAt(t)})`;
      const left = x(field.lon_min + column * lonStep);
      const right = x(field.lon_min + (column + 1) * lonStep);
      context.fillRect(left, top, right - left, bottom - top);
    }
  }
  return canvas;
}
