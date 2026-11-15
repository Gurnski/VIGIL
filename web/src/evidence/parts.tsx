import type { ReactNode } from 'react';

// The small building blocks the evidence view is made of.

export interface Fact {
  term: string;
  value: ReactNode;
}

/** A list of terms, each with what the service reported for it. */
export function Facts({ facts }: { facts: Fact[] }) {
  return (
    <dl className="evidence-facts">
      {facts.map((fact) => (
        <div key={fact.term}>
          <dt>{fact.term}</dt>
          <dd className="numeric">{fact.value}</dd>
        </div>
      ))}
    </dl>
  );
}

export function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="evidence-section">
      <h4>{title}</h4>
      {children}
    </section>
  );
}
