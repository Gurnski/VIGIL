import type { GridLayer, YearsDocument } from '../contract';

// What the globe draws as a layer, whichever way it is drawn: one value per grid cell and the
// range the colours run over. The flat layer, the relief and anything lifted to sit on a column
// all read the same field, so they cannot disagree about which cells are shown or how strongly.

export interface LayerField {
  rows: number;
  columns: number;
  lat_min: number;
  lat_max: number;
  lon_min: number;
  lon_max: number;
  /** The colours run from -limit to +limit, centred on zero. */
  limit: number;
  /** The cell's value, or null where the layer shows nothing. */
  value: (row: number, column: number) => number | null;
}

/** A value rounded up to two significant figures, for the end of a colour scale. 1 when there is nothing to round. */
export function roundedUp(value: number | null | undefined): number {
  if (value == null || !(value > 0) || !Number.isFinite(value)) {
    return 1;
  }
  const unit = Math.pow(10, Math.floor(Math.log10(value)) - 1);
  return Math.ceil(value / unit - 1e-9) * unit;
}

/**
 * A variable's trend in each cell. A cell is shown when it was scanned and, with
 * significantOnly, when its adjusted p-value is below the threshold.
 */
export function trendField(grid: GridLayer, limit: number, significantOnly: boolean, threshold: number): LayerField {
  return {
    rows: grid.rows,
    columns: grid.columns,
    lat_min: grid.lat_min,
    lat_max: grid.lat_max,
    lon_min: grid.lon_min,
    lon_max: grid.lon_max,
    limit,
    value: (row, column) => {
      const slope = grid.slope_per_decade[row]?.[column];
      if (slope == null) {
        return null;
      }
      if (significantOnly) {
        const adjusted = grid.p_adjusted[row]?.[column];
        if (adjusted == null || !(adjusted < threshold)) {
          return null;
        }
      }
      return slope;
    },
  };
}

/**
 * One calendar year of a variable: each scanned cell's mean anomaly against its own normal.
 * Every year shares one scale, ending at the document's own scale_max, so a colour means the
 * same in 2003 as in 2023. No test is run on these values and no finding rests on them.
 */
export function yearField(years: YearsDocument, index: number): LayerField {
  const values = years.values[index] ?? [];
  return {
    rows: years.rows,
    columns: years.columns,
    lat_min: years.lat_min,
    lat_max: years.lat_max,
    lon_min: years.lon_min,
    lon_max: years.lon_max,
    limit: roundedUp(years.scale_max),
    value: (row, column) => values[row]?.[column] ?? null,
  };
}

/** The same field, showing only the cells a test lets through. Used to keep a layer to a zone's cells. */
export function maskedField(field: LayerField, shown: (row: number, column: number) => boolean): LayerField {
  return { ...field, value: (row, column) => (shown(row, column) ? field.value(row, column) : null) };
}
