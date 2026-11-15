import { BufferAttribute, BufferGeometry, Color, SRGBColorSpace } from 'three';
import { colourAt, opacityAt } from './colourScale';
import type { LayerField } from './field';

// Relief: the layer drawn as columns. Each cell the layer would paint stands up from the globe,
// as tall as its value is large against the colour scale's own range, whichever way it goes.
// Its top takes the colour the layer gives the cell. Nothing is worked out here that the flat
// layer does not already show: height repeats what the colour says.

/**
 * The tallest a column gets, in globe radii: a value at or beyond the end of the colour scale.
 * The desktop app's is 0.09. Here it stops just under the lowest orbit drawn (the GPM Core
 * Observatory, 407 km, 0.064 radii), so no column stands through a satellite's ring.
 */
export const MaxReliefAltitude = 0.06;
/** A cell with a value near zero still stands this far clear of the surface, so its top is drawn cleanly. */
export const MinReliefAltitude = 0.003;

// The sides are drawn darker than the top by fixed amounts, so a column reads as a solid
// whatever the light, and the top keeps the legend's exact colour.
const SideShadeNorthSouth = 0.74;
const SideShadeEastWest = 0.6;

/** How far a cell's top stands above the surface, in globe radii. */
export function reliefAltitude(value: number, limit: number): number {
  const size = limit > 0 ? Math.min(1, Math.abs(value) / limit) : 0;
  return MinReliefAltitude + size * (MaxReliefAltitude - MinReliefAltitude);
}

/** The height of the column over a place, or 0 where the layer shows no cell. */
export function reliefAltitudeAt(field: LayerField, lat: number, lng: number): number {
  const row = Math.floor(((lat - field.lat_min) / (field.lat_max - field.lat_min)) * field.rows);
  const column = Math.floor(((lng - field.lon_min) / (field.lon_max - field.lon_min)) * field.columns);
  if (row < 0 || row >= field.rows || column < 0 || column >= field.columns) {
    return 0;
  }
  const value = field.value(row, column);
  return value == null ? 0 : reliefAltitude(value, field.limit);
}

type Point = { x: number; y: number; z: number };

/**
 * Every shown cell as a column, in one geometry, so the whole layer is a single draw. `coords`
 * turns a latitude, longitude and altitude into the globe's own coordinates.
 */
export function buildRelief(field: LayerField, coords: (lat: number, lng: number, altitude: number) => Point): BufferGeometry {
  const positions: number[] = [];
  const colours: number[] = [];
  const indices: number[] = [];
  const { limit } = field;
  const latStep = (field.lat_max - field.lat_min) / Math.max(1, field.rows);
  const lonStep = (field.lon_max - field.lon_min) / Math.max(1, field.columns);
  const colour = new Color();

  // One flat face: four corners, anticlockwise as seen from outside the column.
  const face = (a: Point, b: Point, c: Point, d: Point, shade: number, alpha: number) => {
    const start = positions.length / 3;
    for (const point of [a, b, c, d]) {
      positions.push(point.x, point.y, point.z);
      colours.push(colour.r * shade, colour.g * shade, colour.b * shade, alpha);
    }
    indices.push(start, start + 1, start + 2, start, start + 2, start + 3);
  };

  for (let row = 0; row < field.rows; row++) {
    const south = field.lat_min + row * latStep;
    const north = south + latStep;
    for (let column = 0; column < field.columns; column++) {
      const value = field.value(row, column);
      if (value == null) {
        continue;
      }
      const west = field.lon_min + column * lonStep;
      const east = west + lonStep;
      const top = reliefAltitude(value, limit);
      const t = (value + limit) / (2 * limit);
      const [r, g, b] = colourAt(t);
      colour.setRGB(r / 255, g / 255, b / 255, SRGBColorSpace);
      const alpha = opacityAt(t);

      const baseSW = coords(south, west, 0);
      const baseSE = coords(south, east, 0);
      const baseNE = coords(north, east, 0);
      const baseNW = coords(north, west, 0);
      const topSW = coords(south, west, top);
      const topSE = coords(south, east, top);
      const topNE = coords(north, east, top);
      const topNW = coords(north, west, top);

      face(topSW, topSE, topNE, topNW, 1, alpha);
      face(baseSW, baseSE, topSE, topSW, SideShadeNorthSouth, alpha);
      face(baseNE, baseNW, topNW, topNE, SideShadeNorthSouth, alpha);
      face(baseSE, baseNE, topNE, topSE, SideShadeEastWest, alpha);
      face(baseNW, baseSW, topSW, topNW, SideShadeEastWest, alpha);
    }
  }

  const geometry = new BufferGeometry();
  geometry.setAttribute('position', new BufferAttribute(new Float32Array(positions), 3));
  geometry.setAttribute('color', new BufferAttribute(new Float32Array(colours), 4));
  geometry.setIndex(positions.length / 3 > 65535 ? new BufferAttribute(new Uint32Array(indices), 1) : new BufferAttribute(new Uint16Array(indices), 1));
  return geometry;
}
