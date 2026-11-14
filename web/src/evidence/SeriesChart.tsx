import { useEffect, useMemo, useRef, useState } from 'react';
import type { PointerEvent } from 'react';
import { signed } from './numbers';

/** A change in level to mark on the chart: the date, its uncertainty range and the level either side. */
export interface ChangePointMark {
  date: string;
  dateLow: string;
  dateHigh: string;
  levelBefore: number;
  levelAfter: number;
}

export interface SeriesChartProps {
  /** One date per month, YYYY-MM-DD. */
  dates: string[];
  /** The value for each date, or null for a missing month. */
  values: (number | null)[];
  units: string;
  /** Used in the chart's description for screen readers. */
  label: string;
  /**
   * The slope the service reported, per decade. Drawn as a dashed guide through the middle of
   * the data. Only the slope is the service's: the line's height is set here so that it sits
   * on the series, and it is a guide for the eye, not a result.
   */
  guideSlopePerDecade?: number;
  changePoint?: ChangePointMark;
}

const Height = 190;
const Margin = { left: 46, right: 10, top: 10, bottom: 22 };
const MillisecondsPerDecade = 10 * 365.25 * 86400000;
const MonthNames = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

function time(date: string): number {
  return Date.parse(date + 'T00:00:00Z');
}

function median(values: number[]): number {
  const sorted = [...values].sort((a, b) => a - b);
  const middle = sorted.length >> 1;
  return sorted.length % 2 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
}

/** Round tick values covering a range: steps of 1, 2 or 5 times a power of ten. */
function ticks(low: number, high: number, about: number): number[] {
  const rough = (high - low) / about;
  const power = Math.pow(10, Math.floor(Math.log10(rough)));
  const step = [1, 2, 5, 10].map((m) => m * power).find((s) => s >= rough) ?? rough;
  const list: number[] = [];
  for (let value = Math.ceil(low / step) * step; value <= high + step * 1e-9; value += step) {
    list.push(Math.abs(value) < step * 1e-9 ? 0 : value);
  }
  return list;
}

function tickText(value: number, step: number): string {
  const decimals = Math.max(0, Math.min(6, -Math.floor(Math.log10(step) + 1e-9)));
  return value.toFixed(decimals);
}

/** One monthly series as a line, with a hover read-out. The web twin of the app's TimeSeriesChart. */
export function SeriesChart({ dates, values, units, label, guideSlopePerDecade, changePoint }: SeriesChartProps) {
  const hostRef = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(0);
  const [hover, setHover] = useState<number | null>(null);

  useEffect(() => {
    const host = hostRef.current;
    if (!host) {
      return;
    }
    const observer = new ResizeObserver(() => setWidth(Math.floor(host.clientWidth)));
    observer.observe(host);
    return () => observer.disconnect();
  }, []);

  const plot = useMemo(() => {
    const times = dates.map(time);
    const present: { t: number; v: number; index: number }[] = [];
    values.forEach((value, index) => {
      if (value !== null && Number.isFinite(value) && Number.isFinite(times[index])) {
        present.push({ t: times[index], v: value, index });
      }
    });
    if (present.length < 2 || width <= Margin.left + Margin.right + 20) {
      return null;
    }

    const t0 = times[0];
    const t1 = times[times.length - 1];
    // The guide passes through the median of what is left once the slope is taken out.
    const guide =
      guideSlopePerDecade === undefined
        ? null
        : (() => {
            const slope = guideSlopePerDecade / MillisecondsPerDecade;
            const height = median(present.map((p) => p.v - slope * (p.t - t0)));
            return { start: height, end: height + slope * (t1 - t0) };
          })();

    const extremes = present.map((p) => p.v);
    if (guide) {
      extremes.push(guide.start, guide.end);
    }
    if (changePoint) {
      extremes.push(changePoint.levelBefore, changePoint.levelAfter);
    }
    let low = Math.min(...extremes);
    let high = Math.max(...extremes);
    if (high === low) {
      high += 1;
      low -= 1;
    }
    const pad = (high - low) * 0.06;
    low -= pad;
    high += pad;

    const left = Margin.left;
    const right = width - Margin.right;
    const top = Margin.top;
    const bottom = Height - Margin.bottom;
    const x = (t: number) => left + ((t - t0) / (t1 - t0)) * (right - left);
    const y = (v: number) => bottom - ((v - low) / (high - low)) * (bottom - top);

    // The line breaks at a missing month, not bridged across it.
    let path = '';
    let previous = -2;
    for (const point of present) {
      path += `${point.index === previous + 1 ? 'L' : 'M'}${x(point.t).toFixed(1)},${y(point.v).toFixed(1)}`;
      previous = point.index;
    }

    const yTicks = ticks(low, high, 4);
    const yStep = yTicks.length > 1 ? yTicks[1] - yTicks[0] : 1;
    const firstYear = new Date(t0).getUTCFullYear();
    const lastYear = new Date(t1).getUTCFullYear();
    const everyYears = [1, 2, 5, 10, 20].find((n) => ((lastYear - firstYear) / n) * 44 <= right - left) ?? 20;
    const xTicks: number[] = [];
    for (let year = Math.ceil(firstYear / everyYears) * everyYears; year <= lastYear; year += everyYears) {
      xTicks.push(year);
    }

    return { present, x, y, path, low, high, left, right, top, bottom, yTicks, yStep, xTicks, guide, t0, t1 };
  }, [dates, values, width, guideSlopePerDecade, changePoint]);

  const onPointerMove = (event: PointerEvent<SVGSVGElement>) => {
    if (!plot) {
      return;
    }
    const pointerX = event.clientX - event.currentTarget.getBoundingClientRect().left;
    let nearest = plot.present[0];
    for (const point of plot.present) {
      if (Math.abs(plot.x(point.t) - pointerX) < Math.abs(plot.x(nearest.t) - pointerX)) {
        nearest = point;
      }
    }
    setHover(nearest.index);
  };

  const hovered = plot && hover !== null ? plot.present.find((point) => point.index === hover) ?? null : null;
  const readOut = hovered
    ? (() => {
        const date = new Date(hovered.t);
        return `${MonthNames[date.getUTCMonth()]} ${date.getUTCFullYear()}: ${signed(hovered.v)} ${units}`;
      })()
    : `Anomaly, ${units}`;

  return (
    <div className="evidence-chart" ref={hostRef}>
      <p className="evidence-chart-readout numeric" aria-hidden="true">
        {readOut}
      </p>
      {plot ? (
        <svg
          width={width}
          height={Height}
          role="img"
          aria-label={label}
          onPointerMove={onPointerMove}
          onPointerLeave={() => setHover(null)}
        >
          {changePoint && (
            <rect
              className="evidence-chart-band"
              x={plot.x(time(changePoint.dateLow))}
              y={plot.top}
              width={Math.max(1, plot.x(time(changePoint.dateHigh)) - plot.x(time(changePoint.dateLow)))}
              height={plot.bottom - plot.top}
            />
          )}
          {plot.yTicks.map((value) => (
            <g key={value}>
              <line
                className={value === 0 ? 'evidence-chart-zero' : 'evidence-chart-grid'}
                x1={plot.left}
                x2={plot.right}
                y1={Math.round(plot.y(value)) + 0.5}
                y2={Math.round(plot.y(value)) + 0.5}
              />
              <text className="evidence-chart-tick" x={plot.left - 6} y={plot.y(value)} textAnchor="end" dominantBaseline="middle">
                {tickText(value, plot.yStep)}
              </text>
            </g>
          ))}
          {plot.xTicks.map((year) => (
            <text key={year} className="evidence-chart-tick" x={plot.x(Date.UTC(year, 0, 1))} y={Height - 6} textAnchor="middle">
              {year}
            </text>
          ))}
          <path className="evidence-chart-line" d={plot.path} />
          {plot.guide && (
            <line className="evidence-chart-guide" x1={plot.left} x2={plot.right} y1={plot.y(plot.guide.start)} y2={plot.y(plot.guide.end)} />
          )}
          {changePoint && (
            <>
              <line
                className="evidence-chart-guide"
                x1={plot.left}
                x2={plot.x(time(changePoint.date))}
                y1={plot.y(changePoint.levelBefore)}
                y2={plot.y(changePoint.levelBefore)}
              />
              <line
                className="evidence-chart-guide"
                x1={plot.x(time(changePoint.date))}
                x2={plot.right}
                y1={plot.y(changePoint.levelAfter)}
                y2={plot.y(changePoint.levelAfter)}
              />
              <line
                className="evidence-chart-date"
                x1={Math.round(plot.x(time(changePoint.date))) + 0.5}
                x2={Math.round(plot.x(time(changePoint.date))) + 0.5}
                y1={plot.top}
                y2={plot.bottom}
              />
            </>
          )}
          {hovered && (
            <circle className="evidence-chart-point" cx={plot.x(hovered.t)} cy={plot.y(hovered.v)} r={3.5} />
          )}
        </svg>
      ) : (
        <div style={{ height: Height }} />
      )}
    </div>
  );
}
