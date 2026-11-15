import type { Object3D } from 'three';
import { LineMaterial } from 'three/examples/jsm/lines/LineMaterial.js';
import { LineSegments2 } from 'three/examples/jsm/lines/LineSegments2.js';
import { LineSegmentsGeometry } from 'three/examples/jsm/lines/LineSegmentsGeometry.js';
import type { Coords } from './relief';
import { wrapLongitude } from './stage';

// The lines drawn over the surface: a chosen cell's outline, a zone, the box a search covered.
// They are the viewer's own, not the globe library's paths, so that they are placed by the same
// `coords` as everything else and fold flat with the surface.

/** A line drawn on the surface, or over the columns when the relief is on. */
export interface Outline {
  /** Latitude, longitude and altitude in globe radii. */
  points: [number, number, number][];
  colour: string;
  /** Thickness in pixels. */
  width: number;
  dashed: boolean;
}

/** A dash and the gap after it, in the globe's own units: its radius is 100. */
const DashLength = 2.2;
const DashGap = 1.5;

interface Drawn {
  outline: Outline;
  line: LineSegments2;
}

/** The numbers behind one of a line's attributes, which several attributes share. */
type Shared = { data: { array: Float32Array; needsUpdate: boolean } };

/** The outlines on show, as lines in `parent`. */
export class OutlineSet {
  private drawn: Drawn[] = [];

  constructor(private readonly parent: Object3D) {}

  /** Replaces the lines with these. `unrolled` is whether the surface has edges at 180° W and 180° E. */
  set(outlines: Outline[], coords: Coords, unrolled: boolean): void {
    this.clear();
    for (const outline of outlines) {
      if (outline.points.length < 2) {
        continue;
      }
      const material = new LineMaterial({
        color: outline.colour,
        linewidth: outline.width,
        dashed: outline.dashed,
        dashSize: DashLength,
        gapSize: DashGap,
      });
      // One piece for each pair of neighbouring points, so that a piece can be left out.
      const geometry = new LineSegmentsGeometry();
      geometry.setPositions(new Float32Array((outline.points.length - 1) * 6));
      const line = new LineSegments2(geometry, material);
      if (outline.dashed) {
        // Makes the attributes the dashes are measured along. `place` fills them in.
        line.computeLineDistances();
      }
      // Its points move with the fold, so the bounds three.js would cull against go stale.
      line.frustumCulled = false;
      this.parent.add(line);
      this.drawn.push({ outline, line });
    }
    this.place(coords, unrolled);
  }

  /** Puts every line where `coords` now puts its points. Written over the numbers already there, so a fold makes nothing new. */
  place(coords: Coords, unrolled: boolean): void {
    const from = { x: 0, y: 0, z: 0 };
    const to = { x: 0, y: 0, z: 0 };
    for (const { outline, line } of this.drawn) {
      const { points } = outline;
      const ends = (line.geometry.getAttribute('instanceStart') as unknown as Shared).data;
      const lengths = outline.dashed ? (line.geometry.getAttribute('instanceDistanceStart') as unknown as Shared | undefined)?.data : undefined;
      let along = 0;
      for (let i = 0; i < points.length - 1; i++) {
        coords(points[i][0], points[i][1], points[i][2], from);
        coords(points[i + 1][0], points[i + 1][1], points[i + 1][2], to);
        // A piece that crosses the date line would run the whole width of the unrolled surface
        // to join its two ends. It is closed up to nothing instead, and the line stops at each edge.
        if (unrolled && Math.abs(wrapLongitude(points[i][1]) - wrapLongitude(points[i + 1][1])) > 180) {
          to.x = from.x;
          to.y = from.y;
          to.z = from.z;
        }
        ends.array.set([from.x, from.y, from.z, to.x, to.y, to.z], i * 6);
        if (lengths) {
          const length = Math.hypot(to.x - from.x, to.y - from.y, to.z - from.z);
          lengths.array.set([along, along + length], i * 2);
          along += length;
        }
      }
      ends.needsUpdate = true;
      if (lengths) {
        lengths.needsUpdate = true;
      }
    }
  }

  clear(): void {
    for (const { line } of this.drawn) {
      this.parent.remove(line);
      line.geometry.dispose();
      line.material.dispose();
    }
    this.drawn = [];
  }
}
