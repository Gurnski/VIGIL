import { useMemo } from 'react';
import { cssColourAt, formatScaleValue } from './colourScale';

interface GlobeLegendProps {
  title: string;
  /** The scale runs from -limit to +limit. */
  limit: number;
}

const BarStops = 33;

/** Explains the layer's colours: a title, the scale as a bar, and the values at its ends and middle. */
export function GlobeLegend({ title, limit }: GlobeLegendProps) {
  const bar = useMemo(() => {
    const colours: string[] = [];
    for (let i = 0; i < BarStops; i++) {
      colours.push(cssColourAt(i / (BarStops - 1)));
    }
    return `linear-gradient(to right, ${colours.join(', ')})`;
  }, []);

  return (
    <div className="globe-legend">
      <div className="globe-legend-title">{title}</div>
      <div className="globe-legend-bar" style={{ backgroundImage: bar }} />
      <div className="globe-legend-values numeric">
        <span>{formatScaleValue(-limit, limit)}</span>
        <span>{formatScaleValue(0, limit)}</span>
        <span>{formatScaleValue(limit, limit)}</span>
      </div>
    </div>
  );
}
