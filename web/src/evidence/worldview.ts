import type { Finding } from '../contract';

// A web address that opens NASA Worldview on a finding's cell, so a person can look at the
// place itself in satellite imagery. The web twin of VIGIL/VIGIL/Data/Worldview.cs: keep the
// two in step. The address format is Worldview's own, from its documentation of URL
// parameters: `p` the projection, `v` the view as west, south, east, north in degrees, and `t`
// the day. Nothing is fetched here.

/** Degrees of surroundings shown round the cell, so it can be placed by eye. */
const Margin = 5;

/** A number with at most three decimal places and no trailing zeros. */
function number(value: number): string {
  return String(Math.round(value * 1000) / 1000 + 0);
}

/**
 * Worldview on the finding's cell, on the middle day of the month that matters most to it:
 * the month a change point puts the change in, and otherwise the last month of the period.
 */
export function worldviewLink(finding: Finding): string {
  const { cell } = finding;
  // A month in the contract is written as its first day, "2020-02-01".
  const month = (finding.change_point?.date ?? finding.period.end).slice(0, 7);
  const view = [
    number(Math.max(-180, cell.lon_min - Margin)),
    number(Math.max(-90, cell.lat_min - Margin)),
    number(Math.min(180, cell.lon_max + Margin)),
    number(Math.min(90, cell.lat_max + Margin)),
  ].join(',');
  // Put together by hand: the commas and colons must reach Worldview as they are.
  return 'https://worldview.earthdata.nasa.gov/?p=geographic&v=' + view + '&t=' + month + '-15-T12:00:00Z';
}
