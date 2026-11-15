import type { GlobeMethods } from 'react-globe.gl';
import { Matrix4, PerspectiveCamera, Quaternion, Vector3 } from 'three';
import type { BufferAttribute, Mesh, Object3D, SphereGeometry } from 'three';

// The stage: where a place is in three dimensions, and where the view looks at it from.
//
// The globe library draws a sphere and turns a camera about its centre, and nothing else. Two
// things here go beyond that, so both are kept in this one file and the rest of the viewer asks
// the stage instead of the library for a place's position, its spot on screen, the place under
// the pointer and a flight to somewhere.
//
// Fold: the surface can be unrolled from a sphere into a flat map and rolled up again. Every
// position comes from `coords`, which takes the fold into account, so the Earth's own mesh, the
// layer, the columns of the relief and every outline move together.
//
// Tilt: the view leans over, so that a column is seen from its side and not as a lid. On the
// globe the camera swings down about the place under it. On the flat map it swings down about
// the middle of the view.
//
// The library keeps its own camera and controls, and they still turn the globe. What is drawn
// is seen through the stage's camera, `view`, which follows the library's and adds the tilt, or
// stands over the flat map. Nothing here changes a number: it only moves the picture.

const Radians = Math.PI / 180;

/** How far the view leans over when tilted, in degrees from straight down. The desktop app's is 50 too. */
export const TiltDegrees = 50;
export const FoldMilliseconds = 1400;
export const TiltMilliseconds = 800;

/**
 * Far from the globe a full tilt would drop it half out of the view, so the lean eases off:
 * full up to the first altitude, and down to this share of it by the second. Altitudes in globe radii.
 */
const FarTiltShare = 0.6;
const FullTiltAltitude = 0.8;
const FarTiltAltitude = 2.5;
/**
 * On the flat map everything stands this many times as far off the surface as it does on the
 * globe. A column there is seen from much further off, and no orbit is drawn for it to stand through.
 */
const FlatHeightScale = 2.5;
/** The closest the view comes to the flat map, in globe radii. */
const FlatMinDistance = 0.25;
/**
 * The nearest thing drawn on the flat map, as a share of the view's distance from its middle.
 * The library's own is a fixed 0.05 units, which from far over the map leaves too little depth
 * to tell the layer from the surface under it.
 */
const FlatNearShare = 0.04;
/** How fast the wheel zooms the flat map: the distance is multiplied by e to this, per pixel scrolled. */
const WheelZoom = 0.0015;
/** The furthest north or south the view stands over, in degrees. At a pole there is no north to keep at the top. */
const FurthestLatitude = 85;

/** A position and the way the camera faces. */
interface Pose {
  position: Vector3;
  quaternion: Quaternion;
}

/** Where the view stands: over a place, this far from it, leaning over by this angle in radians. */
interface Standing {
  lat: number;
  lng: number;
  distance: number;
  angle: number;
}

/** A mesh built as a sphere, moved point by point as the surface folds. */
interface Surface {
  mesh: Mesh<SphereGeometry>;
  /** How far above the surface it sits, in globe radii. */
  altitude: number;
  /** Lit meshes need their normals worked out again when they move. */
  lit: boolean;
  /** The mesh's own points as a sphere, to go back to exactly. */
  sphere: Float32Array;
  normals: Float32Array | null;
  /** Each point's latitude and longitude, in degrees. */
  places: Float32Array;
  /** Where each point takes its colour from the image: as the sphere has it, and as the flat map needs it. */
  imageOnSphere: Float32Array;
  imageWhenFolded: Float32Array;
  imageIsFolded: boolean;
}

export interface ScreenPoint {
  x: number;
  y: number;
  /** In front of the view and not round the back of the globe. */
  visible: boolean;
}

function easeInOut(t: number): number {
  return t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2;
}

function clamp(value: number, low: number, high: number): number {
  return Math.min(high, Math.max(low, value));
}

/** A longitude brought into 180° W to 180° E, which is all a flat map has. */
export function wrapLongitude(lng: number): number {
  return lng > 180 || lng < -180 ? ((((lng + 180) % 360) + 360) % 360) - 180 : lng;
}

export class GlobeStage {
  /** The camera the scene is drawn through. */
  readonly view = new PerspectiveCamera();
  /**
   * The object the library scales and turns as the globe opens. Whatever is added to it opens
   * with the globe, where something added to the scene itself would be there at full size at once.
   */
  readonly group: Object3D;
  /** The library's own Earth: the sphere with the Blue Marble image on it. Null if the library is not built as expected. */
  readonly earth: Mesh<SphereGeometry> | null;
  readonly radius: number;

  /** 0 for the globe, 1 for the flat map, and between the two while it folds. */
  fold = 0;
  /** 0 for looking straight down, 1 for fully tilted. */
  tilt = 0;
  /** Goes up whenever the picture has moved: the camera, the fold or the tilt. */
  version = 0;

  private readonly globe: GlobeMethods;
  private readonly canvas: HTMLCanvasElement;
  private readonly restore: (() => void)[] = [];
  private readonly surfaces: Surface[] = [];
  private readonly foldListeners = new Set<() => void>();
  private readonly settledListeners = new Set<() => void>();
  private readonly reducedMotion: boolean;

  private wantFlat = false;
  private wantTilt = false;
  /** The middle of the view on the flat map, and how far back the camera stands. */
  private flat = { x: 0, y: 0, distance: 0 };
  /** The fold in progress: the view goes from standing one way to standing another as the surface folds. */
  private folding: { from: Standing; to: Standing; foldFrom: number; foldTo: number; start: number; ms: number; end: () => void } | null = null;
  private tilting: { from: number; to: number; start: number; ms: number } | null = null;
  private flying: { fromX: number; fromY: number; fromDistance: number; toX: number; toY: number; toDistance: number; start: number; ms: number } | null = null;
  /** A flight asked for while the surface was folding. It is made once the fold is over. */
  private waiting: { to: { lat: number; lng: number; altitude: number }; ms: number } | null = null;
  private readonly pointers = new Map<number, { x: number; y: number }>();
  private lastKey = '';
  /** The view's position as the folded group sees it, for telling the near side of the globe from the far. */
  private readonly eye = new Vector3();

  // Kept to save making new ones every frame.
  private readonly scratch = new Vector3();
  private readonly scratchQuaternion = new Quaternion();
  private readonly matrix = new Matrix4();

  constructor(globe: GlobeMethods) {
    this.globe = globe;
    this.radius = globe.getGlobeRadius();
    this.canvas = globe.renderer().domElement;
    this.reducedMotion = typeof window.matchMedia === 'function' && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    // The library's own Earth: a group marked as the globe, holding the sphere. The object the
    // library animates is the one above it that sits directly in the scene.
    const scene = globe.scene();
    let marked: Object3D | null = null;
    scene.traverse((object) => {
      if (!marked && (object as { __globeObjType?: string }).__globeObjType === 'globe') {
        marked = object;
      }
    });
    let top: Object3D = marked ?? scene;
    while (top.parent && top.parent !== scene) {
      top = top.parent;
    }
    this.group = top;
    const earth = (marked as Object3D | null)?.children.find((child) => (child as Mesh).geometry?.type === 'SphereGeometry') as
      | Mesh<SphereGeometry>
      | undefined;
    this.earth = earth ?? null;
    if (earth) {
      this.addSurface(earth, 0, true);
    }

    // Draw through the stage's camera. The library goes on moving its own, which the stage follows.
    const camera = globe.camera();
    const pass = globe.postProcessingComposer().passes[0] as unknown as { camera: unknown };
    pass.camera = this.view;
    this.restore.push(() => {
      pass.camera = camera;
    });

    // Brought up to date just before each drawing, so the view never lags the controls by a frame.
    const before = scene.onBeforeRender;
    scene.onBeforeRender = () => this.frame();
    this.restore.push(() => {
      scene.onBeforeRender = before;
    });

    this.listen();
    this.frame();
  }

  // ----- Where a place is -----

  /**
   * A place's position, in the group's own coordinates, for the fold as it is now. On the
   * globe it is the library's own answer. Flat, the map faces +Z with north up and its middle
   * (0°, 0°) at the centre. Between the two the sphere's radius grows, so the surface unrolls
   * from the far side and every distance along it stays what it was.
   */
  coords(lat: number, lng: number, altitude: number, into: { x: number; y: number; z: number } = new Vector3()): { x: number; y: number; z: number } {
    const radius = this.radius;
    const fold = this.fold;
    const height = radius * altitude * (1 + (FlatHeightScale - 1) * fold);
    if (fold > 0) {
      // Once it starts to unroll the surface has two edges, at 180° W and 180° E, and nothing beyond them.
      lng = wrapLongitude(lng);
    }
    if (fold >= 1) {
      into.x = radius * lng * Radians;
      into.y = radius * lat * Radians;
      into.z = height;
      return into;
    }
    const curve = 1 - fold;
    const sphere = radius / curve;
    const out = sphere + height;
    const a = lat * Radians * curve;
    const b = lng * Radians * curve;
    const across = Math.cos(a);
    into.x = out * across * Math.sin(b);
    into.y = out * Math.sin(a);
    into.z = out * across * Math.cos(b) - sphere + radius * curve;
    return into;
  }

  /** Where a place is on screen, in pixels from the top left of the globe's canvas. */
  screen(lat: number, lng: number, altitude: number, into: ScreenPoint = { x: 0, y: 0, visible: false }): ScreenPoint {
    const point = this.coords(lat, lng, altitude, this.scratch) as Vector3;
    // Round the back: beyond the horizon as the view sees it. While it unrolls the surface is
    // still part of a sphere, a larger one further back. A flat map has no back.
    let hidden = false;
    if (this.fold < 1) {
      const curve = 1 - this.fold;
      const sphere = this.radius / curve;
      const centre = this.radius * curve - sphere;
      const facing = point.x * this.eye.x + point.y * this.eye.y + (point.z - centre) * (this.eye.z - centre);
      hidden = facing <= sphere * Math.hypot(point.x, point.y, point.z - centre);
    }
    point.applyMatrix4(this.group.matrixWorld).project(this.view);
    into.x = ((point.x + 1) / 2) * this.canvas.clientWidth;
    into.y = ((1 - point.y) / 2) * this.canvas.clientHeight;
    into.visible = !hidden && point.z > -1 && point.z < 1;
    return into;
  }

  /** The place under a point of the canvas, or null where there is none or the surface is still folding. */
  pick(x: number, y: number): { lat: number; lng: number } | null {
    if (this.folding) {
      return null;
    }
    const origin = this.view.position;
    const direction = this.rayThrough(x, y);
    if (this.fold >= 1) {
      const place = this.onMap(x, y);
      if (!place) {
        return null;
      }
      const lat = place.y / this.radius / Radians;
      const lng = place.x / this.radius / Radians;
      return Math.abs(lat) <= 90 && Math.abs(lng) <= 180 ? { lat, lng } : null;
    }
    // The nearer of the two places where the line of sight meets the sphere.
    const along = origin.dot(direction);
    const inside = along * along - (origin.lengthSq() - this.radius * this.radius);
    if (inside < 0) {
      return null;
    }
    const distance = -along - Math.sqrt(inside);
    if (distance <= 0) {
      return null;
    }
    const hit = direction.multiplyScalar(distance).add(origin);
    return { lat: Math.asin(clamp(hit.y / this.radius, -1, 1)) / Radians, lng: Math.atan2(hit.x, hit.z) / Radians };
  }

  // ----- Things that move with the fold -----

  /**
   * Has a sphere, built as the library builds the Earth's, follow the fold. Its points are
   * moved in place, so its texture stays where it was.
   */
  addSurface(mesh: Mesh<SphereGeometry>, altitude: number, lit = false): () => void {
    const positions = mesh.geometry.getAttribute('position') as BufferAttribute;
    const uv = mesh.geometry.getAttribute('uv') as BufferAttribute;
    const across = mesh.geometry.parameters.widthSegments;
    const down = mesh.geometry.parameters.heightSegments;
    const places = new Float32Array(positions.count * 2);
    const imageOnSphere = new Float32Array(uv.array as Float32Array);
    const imageWhenFolded = new Float32Array(imageOnSphere);
    for (let i = 0; i < positions.count; i++) {
      // The image runs from 180° W at its left edge to 180° E at its right, and from the north pole down.
      // A sphere's points at the poles are set half a step along their row, which a flat map must undo,
      // for the point and for where it takes its colour from, or the top and bottom rows lean over.
      const row = Math.floor(i / (across + 1));
      const nudge = row === 0 ? -0.5 / across : row === down ? 0.5 / across : 0;
      const along = uv.getX(i) + nudge;
      imageWhenFolded[i * uv.itemSize] = along;
      places[i * 2] = (uv.getY(i) - 0.5) * 180;
      places[i * 2 + 1] = (along - 0.5) * 360;
    }
    // A sphere leaves out the half of each square that closes to nothing at a pole. Flat, that
    // half is a triangle like any other, and without it the map's top and bottom edges are saw-toothed.
    const index = mesh.geometry.getIndex();
    if (index && positions.count === (across + 1) * (down + 1) && !mesh.geometry.userData.polesClosed) {
      mesh.geometry.userData.polesClosed = true;
      const triangles = Array.from(index.array);
      for (let column = 0; column < across; column++) {
        triangles.push(column + 1, column, across + 1 + column + 1);
        const last = (down - 1) * (across + 1) + column;
        triangles.push(last, last + across + 1, last + across + 2);
      }
      mesh.geometry.setIndex(triangles);
    }
    const normal = mesh.geometry.getAttribute('normal') as BufferAttribute | undefined;
    const surface: Surface = {
      mesh,
      altitude,
      lit,
      sphere: new Float32Array(positions.array as Float32Array),
      normals: normal ? new Float32Array(normal.array as Float32Array) : null,
      places,
      imageOnSphere,
      imageWhenFolded,
      imageIsFolded: false,
    };
    // Its points move, so the bounds three.js would cull against go stale.
    mesh.frustumCulled = false;
    this.surfaces.push(surface);
    this.shape(surface);
    return () => {
      const index = this.surfaces.indexOf(surface);
      if (index >= 0) {
        this.surfaces.splice(index, 1);
      }
    };
  }

  /** Calls back whenever the fold has changed, so that whatever was placed with `coords` can be placed again. */
  onFold(listener: () => void): () => void {
    this.foldListeners.add(listener);
    return () => this.foldListeners.delete(listener);
  }

  /** Calls back when a fold has finished, either way. */
  onSettled(listener: () => void): () => void {
    this.settledListeners.add(listener);
    return () => this.settledListeners.delete(listener);
  }

  /** True from the moment the surface starts to unroll until it is a globe again. */
  get folded(): boolean {
    return this.fold > 0 || this.wantFlat;
  }

  // ----- What the person asks for -----

  setFlat(flat: boolean): void {
    this.wantFlat = flat;
    this.settle();
  }

  setTilt(tilt: boolean): void {
    this.wantTilt = tilt;
    this.settle();
  }

  /** Goes to a place: over it, at an altitude in globe radii, as the library's own flights are given. */
  flyTo(to: { lat: number; lng: number; altitude: number }, ms: number): void {
    if (this.folding) {
      // The view is on its way somewhere already. The last place asked for is gone to when it gets there.
      this.waiting = { to, ms };
      return;
    }
    if (this.fold < 1) {
      this.globe.pointOfView(to, this.reducedMotion ? 0 : ms);
      return;
    }
    const limits = this.flatLimits();
    this.flying = {
      fromX: this.flat.x,
      fromY: this.flat.y,
      fromDistance: this.flat.distance,
      toX: clamp(this.radius * wrapLongitude(to.lng) * Radians, -limits.x, limits.x),
      toY: clamp(this.radius * to.lat * Radians, -limits.y, limits.y),
      // The flat map's scale: the view is about as wide over a place as it is from that altitude over the globe.
      toDistance: clamp(to.altitude * this.radius * 1.6, limits.near, limits.far),
      start: performance.now(),
      ms: this.reducedMotion ? 0 : ms,
    };
  }

  dispose(): void {
    this.folding = null;
    this.waiting = null;
    this.fold = 0;
    for (const surface of this.surfaces) {
      this.shape(surface);
    }
    this.globe.controls().enabled = true;
    for (const undo of this.restore.reverse()) {
      undo();
    }
    this.foldListeners.clear();
    this.settledListeners.clear();
  }

  // ----- Each frame -----

  private frame(): void {
    const now = performance.now();
    const proxy = this.globe.camera() as PerspectiveCamera;
    const view = this.view;
    const near = this.fold >= 1 ? Math.max(proxy.near, this.flat.distance * FlatNearShare) : proxy.near;
    if (view.fov !== proxy.fov || view.aspect !== proxy.aspect || view.near !== near || view.far !== proxy.far) {
      view.fov = proxy.fov;
      view.aspect = proxy.aspect;
      view.near = near;
      view.far = proxy.far;
      view.updateProjectionMatrix();
    }

    if (this.tilting) {
      const { from, to, start, ms } = this.tilting;
      const t = ms <= 0 ? 1 : clamp((now - start) / ms, 0, 1);
      this.tilt = from + (to - from) * easeInOut(t);
      if (t >= 1) {
        this.tilting = null;
      }
    }
    if (this.flying) {
      const { fromX, fromY, fromDistance, toX, toY, toDistance, start, ms } = this.flying;
      const t = ms <= 0 ? 1 : clamp((now - start) / ms, 0, 1);
      const eased = easeInOut(t);
      this.flat.x = fromX + (toX - fromX) * eased;
      this.flat.y = fromY + (toY - fromY) * eased;
      // Distances are eased by ratio, so a zoom looks as even as a move.
      this.flat.distance = fromDistance * Math.pow(toDistance / fromDistance, eased);
      if (t >= 1) {
        this.flying = null;
      }
    }

    if (this.folding) {
      const { from, to, foldFrom, foldTo, start, ms, end } = this.folding;
      const t = ms <= 0 ? 1 : clamp((now - start) / ms, 0, 1);
      const eased = easeInOut(t);
      this.setFold(foldFrom + (foldTo - foldFrom) * eased);
      // The view stays over one place on the surface as it folds, so it is never under the
      // surface and the place never leaves the middle of the picture.
      this.stand(
        {
          lat: from.lat + (to.lat - from.lat) * eased,
          lng: from.lng + (to.lng - from.lng) * eased,
          distance: from.distance * Math.pow(to.distance / from.distance, eased),
          angle: from.angle + (to.angle - from.angle) * eased,
        },
        view,
      );
      if (t >= 1) {
        this.folding = null;
        end();
        this.settledListeners.forEach((listener) => listener());
        this.settle();
      }
    }
    if (!this.folding) {
      if (this.fold >= 1) {
        this.flatPose(this.tilt, view);
      } else {
        this.globePose(proxy.position, proxy.quaternion, this.tilt, view);
      }
    }
    view.updateMatrixWorld(true);
    this.eye.copy(view.position);
    this.group.worldToLocal(this.eye);

    const key = `${view.position.x.toFixed(3)},${view.position.y.toFixed(3)},${view.position.z.toFixed(3)},${view.quaternion.x.toFixed(4)},${view.quaternion.y.toFixed(4)},${view.quaternion.z.toFixed(4)},${this.fold.toFixed(4)},${view.aspect.toFixed(4)},${this.group.scale.x.toFixed(4)},${this.group.quaternion.y.toFixed(4)}`;
    if (key !== this.lastKey) {
      this.lastKey = key;
      this.version++;
    }
  }

  /** Starts whatever is needed to get from how things are to how they were asked to be. */
  private settle(): void {
    if (this.folding) {
      return;
    }
    const isFlat = this.fold >= 1;
    if (this.wantFlat !== isFlat) {
      this.startFold(this.wantFlat);
      return;
    }
    const tilt = this.wantTilt ? 1 : 0;
    const heading = this.tilting ? this.tilting.to : this.tilt;
    if (heading !== tilt) {
      this.tilting = { from: this.tilt, to: tilt, start: performance.now(), ms: this.reducedMotion ? 0 : TiltMilliseconds };
    }
    // Zooming towards the pointer is worked out for the library's camera, which is not where a tilted view stands.
    this.globe.controls().zoomToCursor = !this.wantTilt;
    if (this.waiting) {
      const { to, ms } = this.waiting;
      this.waiting = null;
      this.flyTo(to, ms);
    }
  }

  private startFold(toFlat: boolean): void {
    const controls = this.globe.controls();
    const proxy = this.globe.camera() as PerspectiveCamera;
    const tilt = this.wantTilt ? 1 : 0;
    let from: Standing;
    let to: Standing;
    let end: () => void;

    this.tilting = null;
    this.flying = null;
    this.pointers.clear();
    controls.enabled = false;
    controls.autoRotate = false;

    if (toFlat) {
      // From over the place the library's camera is over, to over the middle of the whole map,
      // or as much of its width as leaves it a fair height on a tall screen.
      const distance = proxy.position.length();
      const height = distance - this.radius;
      from = {
        lat: Math.asin(clamp(proxy.position.y / distance, -1, 1)) / Radians,
        lng: Math.atan2(proxy.position.x, proxy.position.z) / Radians,
        distance: height,
        angle: this.globeLean(height, this.tilt),
      };
      this.flat = { x: 0, y: 0, distance: this.flatLimits().whole };
      to = { lat: 0, lng: 0, distance: this.flat.distance, angle: tilt * TiltDegrees * Radians };
      end = () => {
        this.tilt = tilt;
      };
    } else {
      // Back over the place the flat view was looking at, from about as far away.
      const lat = clamp(this.flat.y / this.radius / Radians, -FurthestLatitude, FurthestLatitude);
      const lng = clamp(this.flat.x / this.radius / Radians, -180, 180);
      const height = clamp((this.flat.distance / this.radius) * 0.75, 0.5, 2.5) * this.radius;
      from = { lat, lng, distance: this.flat.distance, angle: this.tilt * TiltDegrees * Radians };
      to = { lat, lng, distance: height, angle: this.globeLean(height, tilt) };
      end = () => {
        this.tilt = tilt;
        const place = this.globe.getCoords(lat, lng, height / this.radius);
        proxy.position.set(place.x, place.y, place.z);
        controls.target.set(0, 0, 0);
        controls.enabled = true;
        controls.update();
      };
    }

    this.folding = { from, to, foldFrom: this.fold, foldTo: toFlat ? 1 : 0, start: performance.now(), ms: this.reducedMotion ? 0 : FoldMilliseconds, end };
  }

  private setFold(fold: number): void {
    if (fold === this.fold) {
      return;
    }
    this.fold = fold;
    for (const surface of this.surfaces) {
      this.shape(surface);
    }
    this.foldListeners.forEach((listener) => listener());
  }

  /** Moves a surface's points to where the fold puts them. */
  private shape(surface: Surface): void {
    const { mesh, altitude, sphere, places } = surface;
    const positions = mesh.geometry.getAttribute('position') as BufferAttribute;
    const array = positions.array as Float32Array;
    const folded = this.fold > 0;
    if (folded !== surface.imageIsFolded) {
      surface.imageIsFolded = folded;
      const uv = mesh.geometry.getAttribute('uv') as BufferAttribute;
      (uv.array as Float32Array).set(folded ? surface.imageWhenFolded : surface.imageOnSphere);
      uv.needsUpdate = true;
    }
    if (!folded) {
      array.set(sphere);
      if (surface.normals) {
        ((mesh.geometry.getAttribute('normal') as BufferAttribute).array as Float32Array).set(surface.normals);
        mesh.geometry.getAttribute('normal').needsUpdate = true;
      }
    } else {
      const point = this.scratch;
      for (let i = 0; i < positions.count; i++) {
        this.coords(places[i * 2], places[i * 2 + 1], altitude, point);
        // The mesh is turned a quarter of the way round so its image starts at 180° W: undo that turn.
        array[i * 3] = point.z;
        array[i * 3 + 1] = point.y;
        array[i * 3 + 2] = -point.x;
      }
      if (surface.lit) {
        mesh.geometry.computeVertexNormals();
      }
    }
    positions.needsUpdate = true;
  }

  // ----- Where the view stands -----

  /** How far the view over the globe leans, in radians, from this far above the surface. */
  private globeLean(height: number, tilt: number): number {
    const far = clamp((height / this.radius - FullTiltAltitude) / (FarTiltAltitude - FullTiltAltitude), 0, 1);
    return tilt * TiltDegrees * Radians * (1 - (1 - FarTiltShare) * far);
  }

  /**
   * The view over the globe. Untilted it is the library's own camera. Tilted, it swings down
   * about the place beneath that camera, staying as far from the place as it was, and looks at it.
   */
  private globePose(position: Vector3, quaternion: Quaternion, tilt: number, into: Pose): void {
    if (tilt === 0) {
      into.position.copy(position);
      into.quaternion.copy(quaternion);
      return;
    }
    const distance = position.length();
    const height = distance - this.radius;
    const lean = this.scratchQuaternion.setFromAxisAngle(new Vector3(1, 0, 0), this.globeLean(height, tilt));
    into.quaternion.copy(quaternion).multiply(lean);
    // The camera sits back along its own line of sight from the place it looks at.
    const back = this.scratch.set(0, 0, 1).applyQuaternion(into.quaternion);
    into.position.copy(position).multiplyScalar(this.radius / distance).addScaledVector(back, height);
  }

  /** The view over the flat map: above its middle, swung down towards the south when tilted. */
  private flatPose(tilt: number, into: Pose): void {
    const angle = tilt * TiltDegrees * Radians;
    const { x, y, distance } = this.flat;
    into.position.set(x, y - distance * Math.sin(angle), distance * Math.cos(angle));
    into.quaternion.setFromRotationMatrix(this.matrix.lookAt(into.position, new Vector3(x, y, 0), new Vector3(0, 1, 0)));
  }

  /**
   * The view over a place on the surface as it is folded now: back from the place by a
   * distance, leaning towards the south by an angle, with north at the top. Over the globe
   * and over the flat map this is the same view as the two above give.
   */
  private stand(standing: Standing, into: Pose): void {
    const lat = clamp(standing.lat, -FurthestLatitude, FurthestLatitude);
    const place = this.coords(lat, standing.lng, 0, new Vector3()) as Vector3;
    const north = (this.coords(lat + 0.5, standing.lng, 0, new Vector3()) as Vector3).sub(place).normalize();
    const out = (this.coords(lat, standing.lng, 0.01, new Vector3()) as Vector3).sub(place).normalize();
    const back = out.clone().multiplyScalar(Math.cos(standing.angle)).addScaledVector(north, -Math.sin(standing.angle));
    const up = north.multiplyScalar(Math.cos(standing.angle)).addScaledVector(out, Math.sin(standing.angle));
    into.position.copy(place).addScaledVector(back, standing.distance);
    into.quaternion.setFromRotationMatrix(this.matrix.lookAt(into.position, place, up));
  }

  /** How far the flat view may go: to the map's edges, and from close up to a little beyond the whole map. */
  private flatLimits(): { x: number; y: number; near: number; far: number; whole: number } {
    const halfWidth = Math.PI * this.radius;
    const halfHeight = (Math.PI * this.radius) / 2;
    const slope = Math.tan((this.view.fov / 2) * Radians);
    const fitHeight = (halfHeight / slope) * 1.12;
    const fitWidth = (halfWidth / (slope * Math.max(0.1, this.view.aspect))) * 1.06;
    const whole = Math.max(fitHeight, Math.min(fitWidth, 2 * fitHeight));
    return { x: halfWidth, y: halfHeight, near: this.radius * FlatMinDistance, far: Math.max(fitWidth, fitHeight) * 1.2, whole };
  }

  // ----- The pointer on the flat map: drag to move, wheel or pinch to zoom -----

  /** The direction of the line of sight through a point of the canvas. */
  private rayThrough(x: number, y: number): Vector3 {
    const bounds = this.canvas.getBoundingClientRect();
    return new Vector3((x / bounds.width) * 2 - 1, 1 - (y / bounds.height) * 2, 0.5).unproject(this.view).sub(this.view.position).normalize();
  }

  /** Where the line of sight through a point of the canvas meets the flat map, in the map's own units. */
  private onMap(x: number, y: number): Vector3 | null {
    const direction = this.rayThrough(x, y);
    if (direction.z >= -1e-6) {
      return null;
    }
    return direction.multiplyScalar(-this.view.position.z / direction.z).add(this.view.position);
  }

  /** Moves the flat view by an amount in the map's units, then zooms it about a point of the canvas. */
  private moveFlat(dx: number, dy: number, zoom: number, x: number, y: number): void {
    const limits = this.flatLimits();
    const place = () => {
      this.flatPose(this.tilt, this.view);
      this.view.updateMatrixWorld(true);
    };
    this.flat.x = clamp(this.flat.x + dx, -limits.x, limits.x);
    this.flat.y = clamp(this.flat.y + dy, -limits.y, limits.y);
    place();
    if (zoom !== 1) {
      // Zoom about the pointer: the place under it, after the move, stays under it.
      const before = this.onMap(x, y);
      this.flat.distance = clamp(this.flat.distance * zoom, limits.near, limits.far);
      place();
      const after = this.onMap(x, y);
      if (before && after) {
        this.flat.x = clamp(this.flat.x + before.x - after.x, -limits.x, limits.x);
        this.flat.y = clamp(this.flat.y + before.y - after.y, -limits.y, limits.y);
        place();
      }
    }
  }

  private listen(): void {
    const canvas = this.canvas;
    const local = (event: { clientX: number; clientY: number }) => {
      const bounds = canvas.getBoundingClientRect();
      return { x: event.clientX - bounds.left, y: event.clientY - bounds.top };
    };
    const active = () => this.fold >= 1 && !this.folding;

    const down = (event: PointerEvent) => {
      if (!active()) {
        return;
      }
      this.flying = null;
      this.pointers.set(event.pointerId, local(event));
      canvas.setPointerCapture(event.pointerId);
    };
    const move = (event: PointerEvent) => {
      const last = this.pointers.get(event.pointerId);
      if (!active() || !last) {
        return;
      }
      const now = local(event);
      if (this.pointers.size === 1) {
        // The place that was grabbed stays under the pointer.
        const from = this.onMap(last.x, last.y);
        const to = this.onMap(now.x, now.y);
        if (from && to) {
          this.moveFlat(from.x - to.x, from.y - to.y, 1, now.x, now.y);
        }
      } else if (this.pointers.size === 2) {
        const other = [...this.pointers.entries()].find(([id]) => id !== event.pointerId)?.[1];
        if (other) {
          // Two fingers: the place between them goes where they go, and the map zooms as they part.
          const before = Math.hypot(last.x - other.x, last.y - other.y);
          const after = Math.hypot(now.x - other.x, now.y - other.y);
          const middle = { x: (now.x + other.x) / 2, y: (now.y + other.y) / 2 };
          const from = this.onMap((last.x + other.x) / 2, (last.y + other.y) / 2);
          const to = this.onMap(middle.x, middle.y);
          this.moveFlat(from && to ? from.x - to.x : 0, from && to ? from.y - to.y : 0, after > 0 ? before / after : 1, middle.x, middle.y);
        }
      }
      this.pointers.set(event.pointerId, now);
    };
    const up = (event: PointerEvent) => {
      this.pointers.delete(event.pointerId);
    };
    const wheel = (event: WheelEvent) => {
      if (!active()) {
        return;
      }
      event.preventDefault();
      this.flying = null;
      const at = local(event);
      // A wheel that counts in lines scrolls about sixteen pixels a line.
      const pixels = event.deltaMode === 1 ? event.deltaY * 16 : event.deltaY;
      this.moveFlat(0, 0, Math.exp(pixels * WheelZoom), at.x, at.y);
    };

    canvas.addEventListener('pointerdown', down);
    canvas.addEventListener('pointermove', move);
    canvas.addEventListener('pointerup', up);
    canvas.addEventListener('pointercancel', up);
    canvas.addEventListener('wheel', wheel, { passive: false });
    this.restore.push(() => {
      canvas.removeEventListener('pointerdown', down);
      canvas.removeEventListener('pointermove', move);
      canvas.removeEventListener('pointerup', up);
      canvas.removeEventListener('pointercancel', up);
      canvas.removeEventListener('wheel', wheel);
    });
  }
}
