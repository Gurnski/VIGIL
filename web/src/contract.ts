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
  standing: {
    /** "retested" only when a second product confirmed the trend. Otherwise "exploratory", whatever else a re-test found. */
    status: 'exploratory' | 'retested';
    limitations: string[];
    /** Present when a re-test on a second product was tried. */
    retest?: Retest;
  };
}

export type RetestOutcome = 'confirmed' | 'same_direction' | 'opposite' | 'not_possible';

/** The second product a trend was tested on again: another source for the same variable. */
export interface RetestProduct {
  mission: string;
  product: string;
  field?: string;
  units: string;
  source_url: string;
  native_resolution?: string;
}

/**
 * What a re-test on a second product found. Only direction and significance are compared with
 * the finding, never size: the second record is in its own units, which may not be the finding's.
 * Everything after `product` is absent when the outcome is "not_possible".
 */
export interface Retest {
  outcome: RetestOutcome;
  product: RetestProduct;
  /** The sign of the second record's Sen slope. */
  direction?: 'increasing' | 'decreasing';
  slope_per_decade?: number;
  ci_low?: number;
  ci_high?: number;
  ci_level?: number;
  /** Units of the second record's slope and interval. */
  units?: string;
  /** The second record's own p-value. One test per finding, not corrected for many tests. */
  p_value?: number;
  months_present?: number;
  months_expected?: number;
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
    /** Present when the run covered part of the grid, as POST /analyse does. The counts then relate to it. */
    region?: { row: number; column: number; rows: number; columns: number };
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
  /** Present when the re-test step has run. */
  retest?: {
    /** The rule, in words. */
    rule: string;
    /** One entry per variable re-tested. */
    by_variable: RetestTally[];
  };
}

/** How one variable's trend findings came out when tested again on its second product. */
export interface RetestTally {
  variable: VariableId;
  product: RetestProduct;
  /** What the two products share. */
  in_common: string;
  /** Trend findings re-tested. The four outcomes add up to this. */
  findings: number;
  confirmed: number;
  same_direction: number;
  opposite: number;
  not_possible: number;
  /** Cells the scan tested and found nothing in. */
  other_cells: number;
  /** How many of those the second product could be tested in. */
  other_cells_tested: number;
  /** In how many of those it has a significant trend of its own: the rate to hold the confirmations against. */
  other_cells_significant: number;
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
  /** What the Detective looked up on the way to the answer, in order. Absent when it looked nothing up. */
  steps?: AskStep[];
}

/** The latitude and longitude box a search named. */
export interface AskStepBox {
  lat_min: number;
  lat_max: number;
  lon_min: number;
  lon_max: number;
}

/**
 * One tool call the Detective made. A finding in a step was read by the Detective: only the
 * findings in `citations` back what it said.
 */
export interface AskStep {
  /** "search_findings", "get_finding" or "get_run_summary". */
  tool: string;
  /** The tool's arguments, as the Detective gave them. */
  input: Record<string, unknown>;
  /** One plain line saying what was asked for. */
  summary: string;
  /** The findings that call returned. Empty for the run summary. */
  finding_ids: string[];
  /** For a search: how many findings matched in all, which can be more than were returned. */
  matching?: number;
  /** For a search that named a place. */
  box?: AskStepBox;
  /** True when the tool refused the request. */
  failed?: boolean;
}

/** The shape of contract/fixture/ask.json: example questions with their canned answers. */
export interface AskFixture {
  contract_version: string;
  synthetic?: boolean;
  examples: { request: AskRequest; response: AskResponse }[];
}

// ----- A variable year by year: years/<variable>.json beside the page, or GET /years -----

/**
 * Each scanned cell's mean anomaly in each calendar year. For looking at how a change arrived:
 * no test is run on it and no finding rests on it.
 */
export interface YearsDocument {
  contract_version: string;
  synthetic?: boolean;
  /** The run the values belong to. A front end shows them only beside that run. */
  run_id: string;
  variable: VariableId;
  /** Units of the values. */
  units: string;
  /** The calendar years, in order. */
  years: number[];
  /** How many months of data a year needs to be given a value. */
  min_months?: number;
  rows: number;
  columns: number;
  lat_min: number;
  lat_max: number;
  lon_min: number;
  lon_max: number;
  /** Where a colour scale centred on zero can end, over every year. For drawing only. */
  scale_max: number | null;
  /** values[year][row][column], rows south to north. Null where the cell was skipped or the year has too few months. */
  values: (number | null)[][][];
}

// ----- Presenting: GET /present -----

/** A zone a presenter is showing: a name and an outline of [latitude, longitude] points. */
export interface PresentZone {
  name: string;
  polygon: [number, number][];
}

export interface PresentState {
  /** Whether anyone is presenting. When false, `show` is null. */
  presenting: boolean;
  /** The run the service holds. A viewer follows only when this is the run it has on screen. */
  run_id: string | null;
  /** Goes up by one each time what is on show changes. */
  sequence: number;
  show: {
    finding_id: string | null;
    variable: VariableId | null;
    zone: PresentZone | null;
  } | null;
}

// ----- A zone: POST /zone -----

/** One variable over a zone: the one series that stands for the zone and the trend test on it. */
export interface ZoneSummary {
  variable: VariableId;
  /** Units of the series. */
  units: string;
  cells_used: number;
  cells_skipped: Record<string, number>;
  /** Whether the trend test ran. */
  tested: boolean;
  /** Null when it ran. Otherwise "no_cells" or "coverage". */
  skip_reason: string | null;
  /** Below the scan's threshold. Not "significant after correction": one test, on a zone chosen by eye. */
  significant: boolean;
  /** Null when the test did not run, or ran on a flat series and has nothing to report. */
  result: TrendResult | null;
  preprocessing: Preprocessing | null;
  method: { test: string; parameters: Record<string, string | number | boolean | null> } | null;
  summary: string;
  /** A few words to show beside the number when it should not be taken at face value, or null. */
  caution: string | null;
  limitations: string[];
  /** As "A series", for the zone and with no cell id. Null when no cell was used. */
  series: { units: string; dates: string[]; raw: (number | null)[]; anomaly: (number | null)[] } | null;
}

/** The service's answer about a zone. It is not a finding: no cell, id, rank or score. */
export interface ZoneAnalysis {
  contract_version: string;
  synthetic?: boolean;
  /** The stored scan the figures belong beside, and no other. */
  run_id: string;
  zone: {
    /** The ids of the cells the service took the zone to be. */
    cells: string[];
    cell_count: number;
    area_km2: number;
    lat_min: number;
    lat_max: number;
  };
  period: Period;
  variables: Variable[];
  /** One entry per variable, tested or not. */
  summaries: ZoneSummary[];
}
