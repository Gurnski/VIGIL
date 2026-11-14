// The web twin of VIGIL/VIGIL/Controls/Globe/ColourScale.cs (BlueWhiteRed): keep the two in step.

export type Rgb = [number, number, number];

/**
 * Blue, white, red: ColorBrewer's RdBu scheme reversed (Cynthia Brewer, colorbrewer2.org,
 * Apache 2.0). Colour-vision safe, with the neutral centre for "no change".
 */
const stops: Rgb[] = [
  [0x05, 0x30, 0x61],
  [0x21, 0x66, 0xac],
  [0x43, 0x93, 0xc3],
  [0x92, 0xc5, 0xde],
  [0xd1, 0xe5, 0xf0],
  [0xf7, 0xf7, 0xf7],
  [0xfd, 0xdb, 0xc7],
  [0xf4, 0xa5, 0x82],
  [0xd6, 0x60, 0x4d],
  [0xb2, 0x18, 0x2b],
  [0x67, 0x00, 0x1f],
];

const clamp01 = (t: number) => Math.min(1, Math.max(0, t));

/** The colour at a position in [0, 1] along the scale. */
export function colourAt(t: number): Rgb {
  const position = clamp01(t) * (stops.length - 1);
  const index = Math.min(Math.floor(position), stops.length - 2);
  const amount = position - index;
  const from = stops[index];
  const to = stops[index + 1];
  return [
    Math.round(from[0] + (to[0] - from[0]) * amount),
    Math.round(from[1] + (to[1] - from[1]) * amount),
    Math.round(from[2] + (to[2] - from[2]) * amount),
  ];
}

/** The middle means "no change", so cells there fade out and the eye goes to where things change. */
export function opacityAt(t: number): number {
  return 0.18 + 0.82 * Math.abs(2 * clamp01(t) - 1);
}

/** Colour and fade together, as a CSS colour. */
export function cssColourAt(t: number): string {
  const [r, g, b] = colourAt(t);
  return `rgba(${r}, ${g}, ${b}, ${opacityAt(t).toFixed(3)})`;
}

/** The 98th percentile of |slope| over every scanned cell, rounded up to two significant figures. */
export function scaleLimit(slopes: (number | null)[][]): number {
  const magnitudes: number[] = [];
  for (const row of slopes) {
    for (const slope of row) {
      if (slope != null) {
        magnitudes.push(Math.abs(slope));
      }
    }
  }
  if (magnitudes.length === 0) {
    return 1;
  }
  magnitudes.sort((a, b) => a - b);
  let limit = magnitudes[Math.floor(0.98 * (magnitudes.length - 1))];
  if (limit <= 0) {
    limit = magnitudes[magnitudes.length - 1];
  }
  if (limit <= 0) {
    return 1;
  }
  const unit = Math.pow(10, Math.floor(Math.log10(limit)) - 1);
  return Math.ceil(limit / unit - 1e-9) * unit;
}

/** Legend figures: fewer decimals as the range grows. */
export function formatScaleValue(value: number, limit: number): string {
  const decimals = limit >= 10 ? 0 : limit >= 1 ? 1 : limit >= 0.1 ? 2 : 3;
  return value.toLocaleString('en-GB', { minimumFractionDigits: decimals, maximumFractionDigits: decimals });
}
