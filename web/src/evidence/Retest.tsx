import type { Finding, Retest } from '../contract';
import { labelText, sourceLinkText } from '../format';
import { pValue, percent, signed } from './numbers';
import { Facts, Section } from './parts';
import type { Fact } from './parts';

// A trend tested again on a second product for the same variable. The service compares only
// direction and significance, and this view does the same: it never sets the two slopes side
// by side unless they are in exactly the same units.

/** "NASA GISS GISTEMP v4, product gistemp1200_GHCNv4_ERSSTv5". */
function productText(retest: Retest): string {
  return `${retest.product.mission}, product ${retest.product.product}`;
}

/** What the re-test found, in plain words. No numbers: those are listed beside it. */
function outcomeText(retest: Retest): string {
  switch (retest.outcome) {
    case 'confirmed':
      return 'Confirmed. The second record has a trend in the same direction that is significant on its own.';
    case 'same_direction':
      return 'Not confirmed. The second record leans the same way, but its trend is not significant on its own, so the finding stays exploratory.';
    case 'opposite':
      return 'Not confirmed. The second record trends the other way. Treat this finding with caution.';
    case 'not_possible':
      return 'The re-test could not be made. The second record has too little data in this cell.';
    default:
      return labelText(String(retest.outcome));
  }
}

/** The finding's standing as its badge says it. */
export function standingText(finding: Finding): string {
  return finding.standing.status === 'retested' ? 'Re-tested' : labelText(finding.standing.status);
}

/**
 * The line directly under the badges. It names the second product wherever the badge says
 * "Re-tested", and carries the caution when the second record went the other way.
 */
export function RetestLine({ finding }: { finding: Finding }) {
  const { status, retest } = finding.standing;
  if (!retest) {
    return status === 'retested' ? (
      <p className="evidence-retest-line">
        Re-tested, but these results do not name the product it was re-tested on. See the limitations.
      </p>
    ) : null;
  }
  const product = productText(retest);
  switch (retest.outcome) {
    case 'confirmed':
      return <p className="evidence-retest-line">Re-tested on {product}: a significant trend in the same direction.</p>;
    case 'same_direction':
      return (
        <p className="evidence-retest-line">
          Tested again on {product}: the same direction, but not significant on its own.
        </p>
      );
    case 'opposite':
      return (
        <p className="evidence-retest-line evidence-retest-caution">
          Tested again on {product}: the second record trends the other way. Treat this finding with caution.
        </p>
      );
    case 'not_possible':
      return <p className="evidence-retest-line">A re-test on {product} was not possible: too little data in this cell.</p>;
    default:
      return <p className="evidence-retest-line">Tested again on {product}.</p>;
  }
}

/** The second product, what the re-test found on it, and the second record's own trend. */
export function RetestSection({ finding }: { finding: Finding }) {
  const retest = finding.standing.retest;
  if (!retest) {
    return null;
  }
  const { product } = retest;
  const units = retest.units ?? '';
  const facts: Fact[] = [];
  if (retest.direction) {
    facts.push({ term: 'Direction of the second record', value: labelText(retest.direction) });
  }
  if (retest.slope_per_decade !== undefined) {
    facts.push({ term: 'Trend in the second record', value: `${signed(retest.slope_per_decade)} ${units}`.trim() });
  }
  if (retest.ci_low !== undefined && retest.ci_high !== undefined) {
    facts.push({
      term: retest.ci_level !== undefined ? `${percent(retest.ci_level)} interval` : 'Interval',
      value: `${signed(retest.ci_low)} to ${signed(retest.ci_high)} ${units}`.trim(),
    });
  }
  if (retest.p_value !== undefined) {
    facts.push({ term: 'Its own p-value, not corrected', value: pValue(retest.p_value) });
  }
  if (retest.months_present !== undefined && retest.months_expected !== undefined) {
    facts.push({ term: 'Months with a value', value: `${retest.months_present} of ${retest.months_expected}` });
  }

  // The two slopes go side by side only when they are in exactly the same units.
  const scan = finding.result;
  const comparable = scan !== undefined && retest.slope_per_decade !== undefined && retest.units === scan.units;
  if (comparable && scan) {
    facts.push({ term: "The scan's own trend, in the same units", value: `${signed(scan.slope_per_decade)} ${scan.units}` });
  }

  return (
    <Section title="Re-tested on a second product">
      <p className="evidence-retest-outcome">{outcomeText(retest)}</p>
      <div className="evidence-source evidence-retest-product">
        <p className="evidence-source-name">{product.mission}</p>
        <p>
          Product {product.product}
          {product.field ? `, field ${product.field}` : ''}
        </p>
        {product.native_resolution && <p>Native resolution: {product.native_resolution}</p>}
        <p>
          <a href={product.source_url} target="_blank" rel="noreferrer">
            {sourceLinkText(product.source_url)}
          </a>
        </p>
      </div>
      {facts.length > 0 && <Facts facts={facts} />}
      {scan && retest.slope_per_decade !== undefined && !comparable && (
        <p className="evidence-note">
          The second record is in {units || 'its own units'}, not the scan's {scan.units}, so the sizes of the two
          trends cannot be compared.
        </p>
      )}
      <p className="evidence-note">
        Only direction and significance are compared, never size. One test was made for this finding and it is not
        corrected for many tests. A confirmation is evidence, not proof: no second product is fully independent of
        the first. What the two have in common is listed under the limitations.
      </p>
    </Section>
  );
}
