import { BufferAttribute, BufferGeometry, Color, Points, PointsMaterial, SRGBColorSpace } from 'three';

// The star field behind the globe, as the desktop app has one: a fixed scatter of faint
// points, most of them dim and a few brighter. It is made from one seed, so it is the same
// sky every time and nothing twinkles. It is a backdrop and no more: no star is a real one.

/** How many stars there are all round. About a fifteenth of them are in view at once. */
const StarCount = 7000;
/** How far away they are, in the globe's own units: its radius is 100. Far enough that moving the view barely moves them. */
const StarDistance = 40000;
/** The desktop app's brightest star, as a share of white. */
const Brightest = 175 / 255;
/** A star this far up the scale of brightness is drawn larger, as the desktop gives its brightest a soft edge. */
const BrightFrom = 0.94;

/** A stream of numbers from 0 to 1 that is the same every time for the same seed. */
function seeded(seed: number): () => number {
  let state = seed >>> 0;
  return () => {
    state = (state + 0x6d2b79f5) >>> 0;
    let t = state;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

function points(positions: number[], colours: number[], size: number): Points<BufferGeometry, PointsMaterial> {
  const geometry = new BufferGeometry();
  geometry.setAttribute('position', new BufferAttribute(new Float32Array(positions), 3));
  geometry.setAttribute('color', new BufferAttribute(new Float32Array(colours), 3));
  // The same size on screen however far the view is pulled back, and never in front of anything.
  const material = new PointsMaterial({ size, sizeAttenuation: false, vertexColors: true, depthWrite: false });
  const stars = new Points(geometry, material);
  // They are all round the view, so there is no box to cull them by.
  stars.frustumCulled = false;
  // Drawn before the globe, so that nothing see-through is drawn over a gap where a star should be.
  stars.renderOrder = -1;
  return stars;
}

/** The stars, as two sets of points: the many dim ones, and the few bright ones drawn larger. */
export function makeStars(): Points<BufferGeometry, PointsMaterial>[] {
  const random = seeded(2026);
  const dim = { positions: [] as number[], colours: [] as number[] };
  const bright = { positions: [] as number[], colours: [] as number[] };
  const colour = new Color();
  for (let i = 0; i < StarCount; i++) {
    // Evenly over the sphere: an even height, and an even turn round it.
    const height = random() * 2 - 1;
    const turn = random() * 2 * Math.PI;
    const across = Math.sqrt(1 - height * height);
    const q = random();
    const brightness = (0.12 + 0.88 * q * q * q) * Brightest;
    // From faintly warm to faintly blue.
    const tint = random();
    colour.setRGB(brightness * (0.78 + 0.22 * tint), brightness * (0.84 + 0.12 * tint), brightness * (0.86 + 0.14 * (1 - tint)), SRGBColorSpace);
    const set = q > BrightFrom ? bright : dim;
    set.positions.push(StarDistance * across * Math.cos(turn), StarDistance * height, StarDistance * across * Math.sin(turn));
    set.colours.push(colour.r, colour.g, colour.b);
  }
  return [points(dim.positions, dim.colours, 1.4), points(bright.positions, bright.colours, 2.4)];
}
