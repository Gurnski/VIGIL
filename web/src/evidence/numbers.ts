// How the evidence view writes the service's numbers. Nothing here changes a value: it only
// chooses how many figures to show.

const minus = '−';

/** Three significant figures, without exponent notation for ordinary sizes. */
export function figures(value: number, significant = 3): string {
  if (value === 0 || !Number.isFinite(value)) {
    return String(value);
  }
  const magnitude = Math.floor(Math.log10(Math.abs(value)));
  if (magnitude < -5 || magnitude >= 7) {
    return value.toExponential(significant - 1).replace('-', minus);
  }
  const decimals = Math.max(0, significant - 1 - magnitude);
  return value.toFixed(decimals).replace('-', minus);
}

/** As figures(), with a plus sign on a positive value: a change, not a level. */
export function signed(value: number, significant = 3): string {
  return (value > 0 ? '+' : '') + figures(value, significant);
}

/**
 * A p-value: two significant figures, in exponent form when it is very small. One stored as
 * exactly 0 was too small for the arithmetic, not zero, and is written as the desktop app writes it.
 */
export function pValue(value: number): string {
  if (value === 0) {
    return 'below 1e-16';
  }
  return value < 0.001 ? value.toExponential(1).replace('-', minus) : figures(value, 2);
}

/** 0.95 as "95%". */
export function percent(value: number): string {
  return `${figures(value * 100, 3).replace(/\.0+$/, '')}%`;
}

export { monthText } from '../format';
