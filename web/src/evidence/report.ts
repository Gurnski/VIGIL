import type { Finding, RunRecord } from '../contract';

/**
 * A finding written out as plain text, for pasting into a report or an email: what it is, its
 * figures as the results document holds them, what a re-test found, the method and its
 * settings, the sources and every limitation. The web twin of the desktop app's
 * VIGIL/VIGIL/Data/EvidenceReport.cs: keep the two in step. Nothing is rounded, so the text can
 * be checked against the document, and nothing is worked out here.
 */
export function evidenceReport(finding: Finding, run: RunRecord | undefined, synthetic: boolean): string {
  const lines: string[] = [];
  const line = (text = '') => lines.push(text);
  const month = (date: string) => date.slice(0, 7);
  const { cell, period, result, change_point: step, cascade, contradiction: pair, preprocessing, score, standing, method } = finding;

  line(`VIGIL finding ${finding.id}`);
  line(finding.summary);
  if (synthetic) {
    line('These figures are from the synthetic fixture. They are made up.');
  }
  line();
  line(`Kind: ${finding.kind}, rank ${finding.rank}${finding.significant ? ', significant after correction' : ', not significant'}`);
  line(`Cell: ${cell.id}, latitude ${cell.lat_min} to ${cell.lat_max}, longitude ${cell.lon_min} to ${cell.lon_max}`);
  line(`Period: ${month(period.start)} to ${month(period.end)}, ${period.step}`);
  if (run) {
    line(`Scan: ${run.id}, code ${run.code_version}, threshold ${run.settings.threshold}`);
  }

  if (result) {
    line();
    line(`Trend: ${result.slope_per_decade} ${result.units} (${result.direction})`);
    line(`Interval at ${result.ci_level}: ${result.ci_low} to ${result.ci_high}`);
    line(`p-value: ${result.p_value} before correction, ${result.p_adjusted} after`);
  }
  if (step) {
    line();
    line(`Change point: ${month(step.date)}, between ${month(step.date_low)} and ${month(step.date_high)}`);
    line(`Level: ${step.level_before} before, ${step.level_after} after, a shift of ${step.shift} ${step.units}`);
  }
  if (cascade) {
    line();
    line(
      `Cascade: ${cascade.following_variable} trails ${cascade.leading_variable} by ${cascade.lag_months} month(s). An association, not a cause.`,
    );
    line(`Correlation at that lag: ${cascade.correlation}, p-value ${cascade.p_value} before correction, ${cascade.p_adjusted} after`);
  }
  if (pair) {
    line();
    line(
      `Contradiction in ${pair.variable}: ${pair.slope_per_decade} ${pair.units} in this cell, ${pair.paired_slope_per_decade} in cell ${pair.paired_cell.id}` +
        (pair.distance_km !== undefined ? `, ${pair.distance_km} km away` : ''),
    );
  }

  line();
  line(`Data: ${preprocessing.months_present} of ${preprocessing.months_expected} months, coverage ${preprocessing.coverage}`);
  line(
    `Score: total ${score.total}, effect size ${score.effect_size}, certainty ${score.certainty}, coverage ${score.coverage}, ` +
      `robustness ${score.robustness}, cross-system ${score.cross_system}`,
  );

  line();
  const retest = standing.retest;
  if (retest) {
    line(`Re-tested on ${retest.product.mission}, product ${retest.product.product} (${retest.product.source_url}): ${retest.outcome}`);
    if (retest.slope_per_decade !== undefined && retest.p_value !== undefined) {
      line(`Its own trend: ${retest.slope_per_decade} ${retest.units ?? ''}, p-value ${retest.p_value} (one test, not corrected for many tests)`);
    }
  } else {
    line(`Standing: ${standing.status}`);
  }

  line();
  line(`Method: ${method.test}`);
  for (const [name, value] of Object.entries(method.parameters)) {
    line(`  ${name}: ${value === null ? '' : String(value)}`);
  }
  line(`Regridding: ${preprocessing.regridding}`);
  line(`Aggregation: ${preprocessing.aggregation}`);
  line(`Anomaly: ${preprocessing.anomaly}`);
  line(`Missing data: ${preprocessing.missing_data_rule}`);

  line();
  for (const variable of finding.variables) {
    line(
      `Source for ${variable.id}: ${variable.mission}, product ${variable.product}${variable.field ? `, field ${variable.field}` : ''}, ${variable.source_url}`,
    );
  }

  line();
  line('What this cannot say:');
  for (const limitation of standing.limitations) {
    line(`  ${limitation}`);
  }
  for (const confounder of pair?.confounders ?? []) {
    line(`  ${confounder}`);
  }

  if (finding.links.length > 0) {
    line();
    line(`Linked findings: ${finding.links.map((link) => link.finding_id).join(', ')}`);
  }
  return lines.join('\r\n') + '\r\n';
}

/**
 * Puts text on the clipboard. The clipboard API needs a secure page (https or localhost), so
 * on any other page the old way through a hidden text box is tried. Resolves to whether it worked.
 */
export async function copyText(text: string): Promise<boolean> {
  if (navigator.clipboard?.writeText) {
    try {
      await navigator.clipboard.writeText(text);
      return true;
    } catch {
      // Refused, or the page lost focus: try the other way.
    }
  }
  const box = document.createElement('textarea');
  box.value = text;
  box.setAttribute('readonly', '');
  box.style.position = 'fixed';
  box.style.opacity = '0';
  document.body.appendChild(box);
  box.select();
  let copied = false;
  try {
    copied = document.execCommand('copy');
  } catch {
    copied = false;
  }
  box.remove();
  return copied;
}
