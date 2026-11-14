// The result contract as TypeScript types. contract/README.md explains every field and
// contract/schema.json is the same thing for machines. Field names are the JSON's own
// snake_case, so a document is used exactly as it arrives. This file only describes the
// format: it never changes it. If something is missing, ask Dan.

export type VariableId = string;
export type FindingKind = 'trend' | 'change_point' | 'cascade' | 'contradiction';
export type SkipReason = 'ocean' | 'ice' | 'coverage';

/** A results document. Every call sends only the parts it is for, so any part may be missing. */
export interface ResultsDocument {
  contract_version: string;
  /** True when the numbers are made up, as in the fixture. */
  synthetic?: boolean;
  run?: RunRecord;
  variables?: Variable[];
  /** In rank order. */
  findings?: Finding[];
  series?: Series[];
  /** One globe layer per variable. */
  grids?: GridLayer[];
}

export interface Variable {
  id: VariableId;
  name: string;
  units: string;
  mission: string;
  product: string;
  field?: string;
  source_url: string;
  native_resolution?: string;
}

export interface Cell {
  id: string;
  row: number;
  column: number;
  lat_min: number;
  lat_max: number;
  lon_min: number;
  lon_max: number;
}

/** Dates are YYYY-MM-DD. A month is its first day, and `end` is the first day of the last month. */
export interface Period {
  start: string;
  end: string;
  step: 'monthly';
}

export interface Finding {
  id: string;
  kind: FindingKind;
  /** 1 is the top of the list. Sort by this and never by the score. */
  rank: number;
  /** One line for the findings list, written by the service. */
  summary: string;
  /** Whether it passes the run's threshold after correction for multiple testing. */
  significant: boolean;
  cell: Cell;
  period: Period;
  /** One source per variable. A cascade has two: the leading one, then the following one. */
  variables: Variable[];
  preprocessing: Preprocessing;
  method: { test: string; parameters: Record<string, string | number | boolean | null> };
  /** Present when kind is "trend". */
  result?: TrendResult;
  /** Present when kind is "change_point". */
  change_point?: ChangePoint;
  /** Present when kind is "cascade". */
  cascade?: Cascade;
  /** Present when kind is "contradiction". */
  contradiction?: Contradiction;
  links: FindingLink[];
  score: Score;
  standing: { status: 'exploratory' | 'retested'; limitations: string[] };
}

export interface Preprocessing {
  regridding: string;
  aggregation: string;
  anomaly: string;
  missing_data_rule: string;
  months_present: number;
  months_expected: number;
  coverage: number;
}

export interface TrendResult {
  direction: 'increasing' | 'decreasing' | 'none';
  slope_per_decade: number;
  units: string;
  ci_low: number;
  ci_high: number;
  ci_level: number;
  /** Before correction for multiple testing. */
  p_value: number;
  /** After it. This is the one compared with the threshold. */
  p_adjusted: number;
}

export interface ChangePoint {
  date: string;
  date_low: string;
  date_high: string;
  level_before: number;
  level_after: number;
  shift: number;
  units: string;
}

/** An association in time between two variables in one cell. Never a claim of cause. */
export interface Cascade {
  leading_variable: VariableId;
  following_variable: VariableId;
  lag_months: number;
  correlation: number;
  correlations_by_lag?: number[];
  p_value: number;
  p_adjusted: number;
}

export interface Contradiction {
  variable: VariableId;
  paired_cell: Cell;
  slope_per_decade: number;
  paired_slope_per_decade: number;
  units: string;
  distance_km?: number;
  confounders: string[];
}

export interface FindingLink {
  finding_id: string;
  kind: FindingKind;
  lag_months?: number;
  correlation?: number;
  cell_id?: string;
}

/** Every part runs from 0 to 1 and higher is better. */
export interface Score {
  total: number;
  effect_size: number;
  certainty: number;
  coverage: number;
  robustness: number;
  cross_system: number;
}

/** One cell and one variable, as parallel arrays of the same length. */
export interface Series {
  cell_id: string;
  variable: VariableId;
  units: string;
  dates: string[];
  raw: (number | null)[];
  anomaly: (number | null)[];
}

/**
 * A globe layer: the record of every test for one variable. The arrays are indexed
 * [row][column], row 0 the southernmost and column 0 the westernmost. A cell is either
 * scanned, with a slope and a p-value, or skipped, with a reason.
 */
export interface GridLayer {
  variable: VariableId;
  units: string;
  rows: number;
  columns: number;
  lat_min: number;
  lat_max: number;
  lon_min: number;
  lon_max: number;
  slope_per_decade: (number | null)[][];
  p_adjusted: (number | null)[][];
  skip_reason: (SkipReason | null)[][];
}

export interface RunRecord {
  id: string;
  started: string;
  finished: string;
  duration_seconds: number;
  code_version: string;
  settings: {
    grid: { cell_degrees: number; rows: number; columns: number; lat_min: number; lon_min: number };
    period: Period;
    variables: VariableId[];
    min_coverage: number;
    /** An adjusted p-value must be below this to count as significant. */
    threshold: number;
    multiple_testing: string;
    ci_level: number;
    max_lag_months?: number;
    score_weights: Record<string, number>;
    rank_cap?: { top_n: number; max_share: number };
  };
  counts: {
    cells_total: number;
    cells_scanned: number;
    tests_total: number;
    tests_run: number;
    tests_significant: number;
    tests_not_significant: number;
    tests_skipped: number;
    skipped_by_reason: Record<string, number>;
    by_variable: {
      variable: VariableId;
      tests_run: number;
      tests_significant: number;
      tests_not_significant: number;
      tests_skipped: number;
    }[];
    findings_total: number;
    findings_by_kind: Record<string, number>;
  };
}

// ----- The Detective: POST /ask -----

export interface AskTurn {
  role: 'user' | 'assistant';
  text: string;
}

export interface AskRequest {
  question: string;
  /** Earlier turns, oldest first. */
  history?: AskTurn[];
  /** The finding attached to the question. */
  finding_id?: string;
}

export interface AskResponse {
  contract_version: string;
  run_id?: string;
  status: 'answered' | 'inconclusive' | 'out_of_scope';
  answer: string;
  citations: { finding_id: string; supports?: string }[];
  limitations: string[];
  model?: string;
}

/** The shape of contract/fixture/ask.json: example questions with their canned answers. */
export interface AskFixture {
  contract_version: string;
  synthetic?: boolean;
  examples: { request: AskRequest; response: AskResponse }[];
}
