import type { Finding, ResultsDocument } from '../contract';

// The findings on screen written out for other tools: a CSV for a spreadsheet or a notebook and
// a GeoJSON for a GIS such as QGIS. Both are made in the browser from the results document, with
// no service. The columns, their order and the file's name are the contract's ("Findings as a
// file for other tools"), and the desktop app writes the same in VIGIL/VIGIL/Data/FindingsExport.cs:
// keep the two in step. Every value is copied from the document. The only thing worked out is a
// cell's centre, which is the middle of its bounds.

type Value = string | number | boolean | null;

/** The columns, in the contract's order. */
export const columns = [
  'rank',
  'id',
  'kind',
  'significant',
  'summary',
  'variables',
  'cell_id',
  'lat_centre',
  'lon_centre',
  'lat_min',
  'lat_max',
  'lon_min',
  'lon_max',
  'period_start',
  'period_end',
  'slope_per_decade',
  'units',
  'ci_low',
  'ci_high',
  'ci_level',
  'p_value',
  'p_adjusted',
  'change_date',
  'shift',
  'lag_months',
  'correlation',
  'paired_cell_id',
  'standing',
  'retest_outcome',
  'retest_product',
  'score',
  'run_id',
  'synthetic',
] as const;

/** A number the document holds, or null when it is absent or is not a number a file can carry. */
function number(value: number | undefined): number | null {
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
}

/** One finding as the contract's columns, in order. A column its kind does not have is null. */
function rowOf(finding: Finding, results: ResultsDocument): Value[] {
  const { cell, result: trend, change_point: change, cascade, contradiction } = finding;
  const retest = finding.standing.retest;
  return [
    finding.rank,
    finding.id,
    finding.kind,
    finding.significant,
    finding.summary,
    finding.variables.map((variable) => variable.id).join('+'),
    cell.id,
    (cell.lat_min + cell.lat_max) / 2,
    (cell.lon_min + cell.lon_max) / 2,
    cell.lat_min,
    cell.lat_max,
    cell.lon_min,
    cell.lon_max,
    finding.period.start,
    finding.period.end,
    number(trend?.slope_per_decade ?? contradiction?.slope_per_decade),
    trend?.units ?? contradiction?.units ?? change?.units ?? null,
    number(trend?.ci_low),
    number(trend?.ci_high),
    number(trend?.ci_level),
    number(trend?.p_value ?? cascade?.p_value),
    number(trend?.p_adjusted ?? cascade?.p_adjusted),
    change?.date ?? null,
    number(change?.shift),
    number(cascade?.lag_months),
    number(cascade?.correlation),
    contradiction?.paired_cell.id ?? null,
    finding.standing.status,
    retest?.outcome ?? null,
    retest ? `${retest.product.mission} ${retest.product.product}`.trim() : null,
    number(finding.score.total),
    results.run?.id ?? null,
    results.synthetic === true,
  ];
}

function inRankOrder(results: ResultsDocument): Finding[] {
  return [...(results.findings ?? [])].sort((a, b) => a.rank - b.rank);
}

/**
 * The file's name without its ending: "vigil-findings-scan-20261115T005506Z". Made-up findings
 * say "synthetic-fixture" in place of the run, so they cannot be taken for real ones once they
 * have left the viewer.
 */
export function fileStem(results: ResultsDocument): string {
  return `vigil-findings-${results.synthetic === true || !results.run ? 'synthetic-fixture' : results.run.id}`;
}

/**
 * The findings as a GeoJSON FeatureCollection in longitude and latitude: one Feature per
 * finding, in rank order, its cell as a Polygon with one closed ring wound anticlockwise from
 * the south-west corner. The run, the period, the contract version and whether the results are
 * made up are in the collection's `vigil` member.
 */
export function findingsGeoJson(results: ResultsDocument): string {
  const period = results.run?.settings.period;
  const features = inRankOrder(results).map((finding) => {
    const { lat_min, lat_max, lon_min, lon_max } = finding.cell;
    const row = rowOf(finding, results);
    return {
      type: 'Feature',
      id: finding.id,
      geometry: {
        type: 'Polygon',
        coordinates: [
          [
            [lon_min, lat_min],
            [lon_max, lat_min],
            [lon_max, lat_max],
            [lon_min, lat_max],
            [lon_min, lat_min],
          ],
        ],
      },
      properties: Object.fromEntries(columns.map((column, index) => [column, row[index]])),
    };
  });
  return JSON.stringify({
    type: 'FeatureCollection',
    // GDAL, and so QGIS, names the layer after this.
    name: fileStem(results),
    vigil: {
      contract_version: results.contract_version,
      run_id: results.run?.id ?? null,
      synthetic: results.synthetic === true,
      ...(period ? { period: { start: period.start, end: period.end } } : {}),
    },
    features,
  });
}

/** One CSV field: empty for nothing, and quoted when it holds a comma, a quote or a line break, with quotes doubled. */
function csvField(value: Value): string {
  if (value === null) {
    return '';
  }
  const text = String(value);
  return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}

/** The findings as CSV: UTF-8, a header row, then one row per finding in rank order. */
export function findingsCsv(results: ResultsDocument): string {
  const lines = [columns.join(',')];
  for (const finding of inRankOrder(results)) {
    lines.push(rowOf(finding, results).map(csvField).join(','));
  }
  return lines.join('\r\n') + '\r\n';
}

/** Hands the browser a file to save. */
function save(name: string, text: string, type: string): void {
  const url = URL.createObjectURL(new Blob([text], { type }));
  const link = document.createElement('a');
  link.href = url;
  link.download = name;
  document.body.appendChild(link);
  link.click();
  link.remove();
  // The browser has taken the file by now. Letting go of it frees the memory.
  setTimeout(() => URL.revokeObjectURL(url), 10000);
}

export function downloadCsv(results: ResultsDocument): void {
  save(`${fileStem(results)}.csv`, findingsCsv(results), 'text/csv;charset=utf-8');
}

export function downloadGeoJson(results: ResultsDocument): void {
  save(`${fileStem(results)}.geojson`, findingsGeoJson(results), 'application/geo+json');
}
