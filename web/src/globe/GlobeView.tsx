import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import Globe from 'react-globe.gl';
import type { GlobeMethods } from 'react-globe.gl';
import {
  CanvasTexture,
  LinearFilter,
  Mesh,
  MeshBasicMaterial,
  NearestFilter,
  SphereGeometry,
  SRGBColorSpace,
} from 'three';
import earthUrl from '../../../assets/BlueMarbleDay.jpg';
import type { Cell, Finding, ResultsDocument, YearsDocument } from '../contract';
import { cellCentre, labelText } from '../format';
import { scaleLimit } from './colourScale';
import { maskedField, trendField, yearField } from './field';
import type { LayerField } from './field';
import { GlobeLegend } from './GlobeLegend';
import { YearBar } from './YearBar';
import { paintLayer } from './layerTexture';
import { buildRelief, reliefAltitudeAt } from './relief';
import { satelliteCaveat, satellitesFor } from './satellite';
import { SatelliteCard } from './SatelliteCard';
import { useSatellites } from './useSatellites';
import './globe.css';

export interface GlobeViewProps {
  results: ResultsDocument;
  /** The variable whose layer is painted, or null for none. */
  layerVariable: string | null;
  showLayer: boolean;
  /** Paint only the cells whose adjusted p-value is below the run's threshold. */
  significantOnly: boolean;
  /** Draw the layer's cells as columns, each as tall as its trend is large. Only with the layer shown. */
  relief: boolean;
  /**
   * The chosen variable year by year. When given, the layer shows one calendar year in place
   * of the trend, and a bar over the bottom of the globe chooses which. Null for the trend.
   */
  years: YearsDocument | null;
  /** Keeps the layer to some cells, as a chosen zone does. Null for every cell. */
  cellMask?: ((row: number, column: number) => boolean) | null;
  /** The chosen finding: outline its cell and fly to it when it changes. */
  selected: Finding | null;
  /** Draw the satellites behind the data on their orbits. */
  showSatellites: boolean;
  /** A finding's marker was clicked. */
  onFindingClick: (finding: Finding) => void;
  /** Findings and a place pointed out from elsewhere in the viewer, or null. The globe flies there when it changes. */
  highlight: GlobeHighlight | null;
  onClearHighlight: () => void;
  /** Goes back to the Detective from the note about a highlight. Null when the Detective is already in view. */
  onBackToDetective: (() => void) | null;
  /** A place to go to, as a box. Make a new object each time, even for the same place, and the globe flies there again. */
  goTo?: GlobeBox | null;
  /** A zone to draw as an outline with its name, or null. The globe goes to it when it changes. */
  zone?: GlobeZone | null;
  /**
   * The points of an outline being drawn, or null when none is. While it is given, a click or
   * tap on the globe that did not drag lays a point, and a bar over the globe has the buttons
   * that undo, finish and cancel.
   */
  sketch?: [number, number][] | null;
  onSketchPoint?: (lat: number, lng: number) => void;
  onSketchUndo?: () => void;
  onSketchFinish?: () => void;
  onSketchCancel?: () => void;
}

/** An area of any shape: a name and an outline of [latitude, longitude] points, closed from the last back to the first. */
export interface GlobeZone {
  name: string;
  polygon: [number, number][];
  /**
   * The stepped edge of the grid cells the zone takes in, as lines of two points each. Drawn
   * dashed beside the outline, so the area the numbers cover is on screen with the outline that was drawn.
   */
  edges?: [[number, number], [number, number]][];
}

/** An area in latitude and longitude. */
export interface GlobeBox {
  lat_min: number;
  lat_max: number;
  lon_min: number;
  lon_max: number;
}

/**
 * Something another part of the viewer asks the globe to point out: the findings an answer
 * cites, or what the Detective read in one of its lookups and where it searched. Make a new
 * object each time, even for the same thing, and the globe flies to it again.
 */
export interface GlobeHighlight {
  /** "cited" findings back an answer. "read" findings were only looked at, and are drawn more quietly. */
  tone: 'cited' | 'read';
  /** What the marks are, in a few words. */
  title: string;
  /** More about them, such as the lookup's own summary. */
  caption?: string;
  findings: Finding[];
  /** A place that was searched: outlined with a dashed line, as the rings round findings that were only read are dashed. */
  box?: GlobeBox;
}

// Colours used inside three.js, which cannot read CSS variables.
const BackgroundColour = '#131415'; // mirrors --background
const AccentColour = '#e9e6df'; // mirrors --accent

const LayerOpacity = 0.7;
/** The layer floats just clear of the surface so the two never fight for the same depth. */
const LayerRadiusFactor = 1.002;
const OutlineAltitude = 0.004;
/** Outline thickness in degrees of arc: thin beside a 2.5° cell. */
const OutlineStroke = 0.18;
const MarkerAltitude = 0.006;
const MaxMarkedFindings = 200;
/** How many markers keep their label on show. More than this and the labels hide each other. */
const LabelledMarkers = 24;
const LabelledMarkersInZone = 3;
/** A marker label's place beside its dot, in pixels: the gap between them, half the label's height, and the room kept at the view's edge. */
const LabelGap = 4;
const LabelHalfHeight = 8;
const LabelEdge = 4;
const DefaultThreshold = 0.05;
const FlightAltitude = 0.6;
const FlightMilliseconds = 1200;
const AutoRotateSpeed = 0.35;
/** The furthest the camera pulls back to take in a highlight: the altitude the globe opens at. */
const MaxFlightAltitude = 2.5;
/** Roughly how many degrees of arc fit across a square view for each unit of altitude, with room to spare. */
const DegreesPerAltitude = 35;
const MaxHighlightMarks = 200;
const MaxHighlightOutlines = 20;
/** A searched box is drawn point by point, so its sides follow the parallels and meridians. */
const BoxStepDegrees = 1;
const BoxDashLength = 0.012;
const BoxDashGap = 0.008;
/** The key of the zone's name among the elements placed over the globe. */
const ZoneLabelKey = 'zone:label';
/** A press that moves less than this many pixels before it lifts is a click, not a drag of the globe. */
const ClickSlopPixels = 6;
/** A finger is less steady than a mouse, so a tap may wander further and still be a tap. */
const TapSlopPixels = 12;

/** A line drawn on the surface, or over the columns when the relief is on. */
interface Outline {
  /** Latitude, longitude and altitude in globe radii. */
  points: [number, number, number][];
  colour: string;
  /** Thickness in degrees of arc. */
  stroke: number;
  dashed: boolean;
}

/** A ring round a cell the highlight points out. */
interface HighlightMark {
  key: string;
  lat: number;
  lng: number;
  /** The top-ranked highlighted finding in the cell: what a click selects. */
  finding: Finding;
  /** How many highlighted findings are in the cell. */
  findings: number;
}

interface Marker {
  cellId: string;
  lat: number;
  lng: number;
  label: string;
  /** Whether the label is always shown. The rest show theirs under the pointer, as on the desktop. */
  labelled: boolean;
  significant: boolean;
  /** The top-ranked finding in the cell: what a click selects. */
  finding: Finding;
}

// How the globe reads a point of an outline. Declared once, so the paths are not rebuilt on every draw.
const pathLat = (point: number[]) => point[0];
const pathLng = (point: number[]) => point[1];
const pathAltitude = (point: number[]) => point[2];

/** The cell's bounds as a closed path of corners, at one altitude. */
function cellOutline(cell: Cell, altitude: number): [number, number, number][] {
  return [
    [cell.lat_min, cell.lon_min, altitude],
    [cell.lat_min, cell.lon_max, altitude],
    [cell.lat_max, cell.lon_max, altitude],
    [cell.lat_max, cell.lon_min, altitude],
    [cell.lat_min, cell.lon_min, altitude],
  ];
}

/** The box's bounds as a closed path, with a point every degree so no side takes a short cut. */
function boxOutline(box: GlobeBox): [number, number, number][] {
  const points: [number, number, number][] = [];
  const run = (from: [number, number], to: [number, number]) => {
    const steps = Math.max(1, Math.ceil(Math.max(Math.abs(to[0] - from[0]), Math.abs(to[1] - from[1])) / BoxStepDegrees));
    for (let i = 0; i < steps; i++) {
      points.push([from[0] + ((to[0] - from[0]) * i) / steps, from[1] + ((to[1] - from[1]) * i) / steps, OutlineAltitude]);
    }
  };
  const corners: [number, number][] = [
    [box.lat_min, box.lon_min],
    [box.lat_min, box.lon_max],
    [box.lat_max, box.lon_max],
    [box.lat_max, box.lon_min],
  ];
  corners.forEach((corner, index) => run(corner, corners[(index + 1) % corners.length]));
  points.push([corners[0][0], corners[0][1], OutlineAltitude]);
  return points;
}

/**
 * A zone's outline as a closed path. Its edges are straight lines in latitude and longitude,
 * so each is drawn point by point and none takes a short cut over the globe.
 */
function zoneOutline(polygon: [number, number][]): [number, number, number][] {
  const points: [number, number, number][] = [];
  polygon.forEach((from, index) => {
    const to = polygon[(index + 1) % polygon.length];
    const steps = Math.max(1, Math.ceil(Math.max(Math.abs(to[0] - from[0]), Math.abs(to[1] - from[1])) / BoxStepDegrees));
    for (let i = 0; i < steps; i++) {
      points.push([from[0] + ((to[0] - from[0]) * i) / steps, from[1] + ((to[1] - from[1]) * i) / steps, OutlineAltitude]);
    }
  });
  points.push([polygon[0][0], polygon[0][1], OutlineAltitude]);
  return points;
}

/** A straight line in latitude and longitude between two places, point by point, so it follows its parallel or meridian. */
function straightLine(from: [number, number], to: [number, number], last: boolean): [number, number, number][] {
  const points: [number, number, number][] = [];
  const steps = Math.max(1, Math.ceil(Math.max(Math.abs(to[0] - from[0]), Math.abs(to[1] - from[1])) / BoxStepDegrees));
  for (let i = 0; i < steps; i++) {
    points.push([from[0] + ((to[0] - from[0]) * i) / steps, from[1] + ((to[1] - from[1]) * i) / steps, OutlineAltitude]);
  }
  if (last) {
    points.push([to[0], to[1], OutlineAltitude]);
  }
  return points;
}

/** The box a zone's outline fits in. Its longitudes run on past 180 where the outline crosses the date line. */
function zoneBox(polygon: [number, number][]): GlobeBox {
  const lats = polygon.map((point) => point[0]);
  const lons = polygon.map((point) => point[1]);
  return { lat_min: Math.min(...lats), lat_max: Math.max(...lats), lon_min: Math.min(...lons), lon_max: Math.max(...lons) };
}

/** The angle between two places as seen from the Earth's centre, in degrees. Only used to frame the view. */
function degreesApart(a: { lat: number; lng: number }, b: { lat: number; lng: number }): number {
  const radians = Math.PI / 180;
  const cosine =
    Math.sin(a.lat * radians) * Math.sin(b.lat * radians) +
    Math.cos(a.lat * radians) * Math.cos(b.lat * radians) * Math.cos((a.lng - b.lng) * radians);
  return Math.acos(Math.min(1, Math.max(-1, cosine))) / radians;
}

/**
 * Where the camera goes for a highlight: over the middle of its box, or over its first finding,
 * far enough back to take in the rest. `aspect` is the view's width over its height.
 */
function highlightView(highlight: GlobeHighlight, aspect: number): { lat: number; lng: number; altitude: number } | null {
  const fit = DegreesPerAltitude * Math.min(1, Math.max(0.4, aspect));
  const altitude = (span: number) => Math.min(MaxFlightAltitude, Math.max(FlightAltitude, span / fit));
  const { box } = highlight;
  if (box) {
    const lat = (box.lat_min + box.lat_max) / 2;
    const width = (box.lon_max - box.lon_min) * Math.cos((lat * Math.PI) / 180);
    return { lat, lng: (box.lon_min + box.lon_max) / 2, altitude: altitude(Math.max(box.lat_max - box.lat_min, width)) };
  }
  const first = highlight.findings[0];
  if (!first) {
    return null;
  }
  const centre = cellCentre(first.cell);
  let furthest = 0;
  for (const finding of highlight.findings) {
    furthest = Math.max(furthest, degreesApart(centre, cellCentre(finding.cell)));
  }
  return { ...centre, altitude: altitude(2 * furthest) };
}

/**
 * The Blue Marble image is 8192 pixels wide, more than some phones can hold as one texture.
 * Returns the image's own address where it fits, or a scaled-down copy where it does not.
 */
function useEarthImage(): string | null {
  const [url, setUrl] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    let objectUrl: string | null = null;

    const probe = document.createElement('canvas').getContext('webgl');
    const maxSize: number = probe ? probe.getParameter(probe.MAX_TEXTURE_SIZE) : 4096;
    probe?.getExtension('WEBGL_lose_context')?.loseContext();

    const image = new Image();
    image.onload = () => {
      if (cancelled) {
        return;
      }
      if (image.naturalWidth <= maxSize) {
        setUrl(earthUrl);
        return;
      }
      const canvas = document.createElement('canvas');
      canvas.width = maxSize;
      canvas.height = Math.round((image.naturalHeight * maxSize) / image.naturalWidth);
      canvas.getContext('2d')?.drawImage(image, 0, 0, canvas.width, canvas.height);
      canvas.toBlob(
        (blob) => {
          if (blob && !cancelled) {
            objectUrl = URL.createObjectURL(blob);
            setUrl(objectUrl);
          }
        },
        'image/jpeg',
        0.9,
      );
    };
    image.onerror = () => !cancelled && setUrl(earthUrl);
    image.src = earthUrl;

    return () => {
      cancelled = true;
      if (objectUrl) {
        URL.revokeObjectURL(objectUrl);
      }
    };
  }, []);

  return url;
}

/**
 * The 3D globe: the trend layer, a marker per cell with findings, the selected cell's outline,
 * and whatever the Detective has pointed out.
 */
export function GlobeView({
  results,
  layerVariable,
  showLayer,
  significantOnly,
  relief,
  years,
  cellMask = null,
  selected,
  showSatellites,
  onFindingClick,
  highlight,
  onClearHighlight,
  onBackToDetective,
  goTo = null,
  zone = null,
  sketch = null,
  onSketchPoint,
  onSketchUndo,
  onSketchFinish,
  onSketchCancel,
}: GlobeViewProps) {
  const hostRef = useRef<HTMLDivElement>(null);
  const globeRef = useRef<GlobeMethods>();
  const [size, setSize] = useState({ width: 0, height: 0 });
  const [ready, setReady] = useState(false);
  const earthImage = useEarthImage();

  useEffect(() => {
    const host = hostRef.current;
    if (!host) {
      return;
    }
    const observer = new ResizeObserver(() => {
      const width = Math.floor(host.clientWidth);
      const height = Math.floor(host.clientHeight);
      setSize((old) => (old.width === width && old.height === height ? old : { width, height }));
    });
    observer.observe(host);
    return () => observer.disconnect();
  }, []);

  // ----- Auto-rotation: slow, and over for good once the user touches the globe or picks a finding -----

  const stopRotation = useCallback(() => {
    const controls = globeRef.current?.controls();
    if (controls) {
      controls.autoRotate = false;
    }
  }, []);

  useEffect(() => {
    const controls = globeRef.current?.controls();
    if (!ready || !controls) {
      return;
    }
    controls.autoRotate = true;
    controls.autoRotateSpeed = AutoRotateSpeed;
    controls.addEventListener('start', stopRotation);
    return () => controls.removeEventListener('start', stopRotation);
  }, [ready, stopRotation]);

  // ----- Trend layer -----

  const grid = useMemo(
    () => (layerVariable == null ? null : results.grids?.find((layer) => layer.variable === layerVariable) ?? null),
    [results.grids, layerVariable],
  );
  // Set from every scanned cell, so the scale stays put when "significant only" is switched.
  const trendLimit = useMemo(() => (grid ? scaleLimit(grid.slope_per_decade) : 1), [grid]);
  const threshold = results.run?.settings.threshold ?? DefaultThreshold;

  // ----- The layer year by year: one calendar year in place of the trend -----

  // Shown only when the years on hand are the chosen layer's. While another variable's are on
  // their way the trend stays on the globe.
  const yearsShown = years && grid && years.variable === grid.variable ? years : null;
  const [yearIndex, setYearIndex] = useState(0);
  const [playing, setPlaying] = useState(false);
  const yearsList = yearsShown?.years.join(',') ?? '';
  const lastYearsList = useRef('');
  useEffect(() => {
    // Another variable keeps the chosen year when it has the same years. Otherwise the bar starts again.
    if (yearsList !== '' && lastYearsList.current !== '' && yearsList !== lastYearsList.current) {
      setYearIndex(0);
    }
    if (yearsList !== '') {
      lastYearsList.current = yearsList;
    }
    setPlaying(false);
  }, [yearsList]);
  const year = yearsShown ? Math.min(yearIndex, yearsShown.years.length - 1) : 0;

  // What is drawn, flat or as relief: the chosen year, or the trend. Kept to a zone's cells when one is chosen.
  const field = useMemo<LayerField | null>(() => {
    if (!grid) {
      return null;
    }
    const whole = yearsShown ? yearField(yearsShown, year) : trendField(grid, trendLimit, significantOnly, threshold);
    return cellMask ? maskedField(whole, cellMask) : whole;
  }, [grid, yearsShown, year, trendLimit, significantOnly, threshold, cellMask]);
  const limit = field?.limit ?? 1;
  const layerCanvas = useMemo(() => (field ? paintLayer(field) : null), [field]);

  const layerMeshRef = useRef<Mesh<SphereGeometry, MeshBasicMaterial> | null>(null);

  // The sphere that carries the layer, built like three-globe's own globe mesh (a SphereGeometry
  // turned so the prime meridian faces +Z) so an equirectangular image lands on the same places.
  useEffect(() => {
    const globe = globeRef.current;
    if (!ready || !globe) {
      return;
    }
    const geometry = new SphereGeometry(globe.getGlobeRadius() * LayerRadiusFactor, 96, 48);
    const material = new MeshBasicMaterial({
      transparent: true,
      opacity: LayerOpacity,
      premultipliedAlpha: true,
      depthWrite: false,
    });
    const mesh = new Mesh(geometry, material);
    mesh.rotation.y = -Math.PI / 2;
    mesh.visible = false;
    globe.scene().add(mesh);
    layerMeshRef.current = mesh;
    return () => {
      globe.scene().remove(mesh);
      geometry.dispose();
      material.map?.dispose();
      material.dispose();
      layerMeshRef.current = null;
    };
  }, [ready]);

  useEffect(() => {
    const mesh = layerMeshRef.current;
    if (!ready || !mesh) {
      return;
    }
    mesh.material.map?.dispose();
    mesh.material.map = null;
    if (layerCanvas) {
      const texture = new CanvasTexture(layerCanvas);
      texture.colorSpace = SRGBColorSpace;
      // Nearest keeps each cell a crisp block close up; no mipmaps, which would smear cells together.
      texture.magFilter = NearestFilter;
      texture.minFilter = LinearFilter;
      texture.generateMipmaps = false;
      // Premultiplied, so clear cells do not darken the edges of their coloured neighbours.
      texture.premultiplyAlpha = true;
      mesh.material.map = texture;
    }
    mesh.material.needsUpdate = true;
  }, [ready, layerCanvas]);

  const layerShown = showLayer && grid != null;
  // The relief is the layer drawn another way, so it takes the flat layer's place.
  const reliefShown = layerShown && relief;
  useEffect(() => {
    if (layerMeshRef.current) {
      layerMeshRef.current.visible = layerShown && !reliefShown;
    }
  }, [ready, layerShown, reliefShown]);

  // ----- Relief: the same cells as columns, in one mesh -----

  useEffect(() => {
    const globe = globeRef.current;
    if (!ready || !globe || !reliefShown || !field) {
      return;
    }
    const geometry = buildRelief(field, (lat, lng, altitude) => globe.getCoords(lat, lng, altitude));
    // Unlit, so a column's top is the legend's own colour whichever way the globe is turned.
    const material = new MeshBasicMaterial({ vertexColors: true, transparent: true });
    const mesh = new Mesh(geometry, material);
    globe.scene().add(mesh);
    return () => {
      globe.scene().remove(mesh);
      geometry.dispose();
      material.dispose();
    };
  }, [ready, reliefShown, field]);

  /** How far above the surface something drawn on a place must sit to clear the column there. */
  const liftAt = useCallback(
    (lat: number, lng: number) => (reliefShown && field ? reliefAltitudeAt(field, lat, lng) : 0),
    [reliefShown, field],
  );

  // ----- Markers: one per cell that has a finding -----

  const markers = useMemo(() => {
    const byCell = new Map<string, Finding[]>();
    // A chosen zone keeps the markers to its cells, and the top of the ranking is then the top within it.
    const ranked = [...(results.findings ?? [])]
      .filter((finding) => !cellMask || cellMask(finding.cell.row, finding.cell.column))
      .sort((a, b) => a.rank - b.rank)
      .slice(0, MaxMarkedFindings);
    for (const finding of ranked) {
      const here = byCell.get(finding.cell.id);
      if (here) {
        here.push(finding);
      } else {
        byCell.set(finding.cell.id, [finding]);
      }
    }
    const list: Marker[] = [];
    for (const [cellId, here] of byCell) {
      const top = here[0];
      const rank = top.rank.toLocaleString('en-GB');
      list.push({
        cellId,
        ...cellCentre(top.cell),
        label: `#${rank}${here.length > 1 ? ` and ${(here.length - 1).toLocaleString('en-GB')} more` : ''}`,
        // Cells come out in rank order, so the first few are the top of the ranking.
        // Within a zone the markers sit side by side, so only the top few keep their labels.
        labelled: list.length < (cellMask ? LabelledMarkersInZone : LabelledMarkers),
        significant: here.some((finding) => finding.significant),
        finding: top,
      });
    }
    return list;
  }, [results.findings, cellMask]);

  // ----- Highlight: a ring round each cell that was pointed out, whatever its rank -----

  const highlightMarks = useMemo(() => {
    const byCell = new Map<string, HighlightMark>();
    for (const finding of highlight?.findings ?? []) {
      const here = byCell.get(finding.cell.id);
      if (!here) {
        if (byCell.size < MaxHighlightMarks) {
          byCell.set(finding.cell.id, { key: `mark:${finding.cell.id}`, ...cellCentre(finding.cell), finding, findings: 1 });
        }
        continue;
      }
      here.findings++;
      if (finding.rank < here.finding.rank) {
        here.finding = finding;
      }
    }
    return [...byCell.values()];
  }, [highlight]);

  const zoneLabel = useMemo(() => {
    if (!zone || zone.polygon.length < 3) {
      return null;
    }
    const box = zoneBox(zone.polygon);
    return { lat: box.lat_max, lng: (box.lon_min + box.lon_max) / 2 };
  }, [zone]);

  const selectedCell = selected?.cell.id ?? null;

  // Markers are plain DOM buttons placed over the canvas each frame, so they are real buttons
  // for the keyboard and screen readers and are only moved when the camera has.
  const markerElements = useRef(new Map<string, HTMLButtonElement>());
  useEffect(() => {
    const globe = globeRef.current;
    if (!ready || !globe) {
      return;
    }
    const radius = globe.getGlobeRadius();
    const placed = [
      ...markers.map((marker) => ({ cellId: marker.cellId, lat: marker.lat, lng: marker.lng })),
      ...highlightMarks.map((mark) => ({ cellId: mark.key, lat: mark.lat, lng: mark.lng })),
      // A zone's name sits at the middle of its northern edge.
      ...(zoneLabel ? [{ cellId: ZoneLabelKey, lat: zoneLabel.lat, lng: zoneLabel.lng }] : []),
      ...(sketch ?? []).map((point, index) => ({ cellId: `sketch:${index}`, lat: point[0], lng: point[1] })),
    ].map((marker) => {
      // With the relief on, a marker sits on top of its cell's column, not at its foot.
      const altitude = MarkerAltitude + liftAt(marker.lat, marker.lng);
      return { marker, altitude, position: globe.getCoords(marker.lat, marker.lng, altitude) };
    });
    // The markers whose labels are on show, in the order their labels are placed: the chosen
    // marker's first, then in rank order. A label's width is measured once, the first time it is placed.
    const labelled = new Map<string, { x: number; y: number; seen: boolean; half: number; width?: number; state?: string }>();
    const chosenMarker = markers.find((marker) => marker.cellId === selectedCell);
    for (const marker of chosenMarker ? [chosenMarker, ...markers] : markers) {
      if ((marker.labelled || marker === chosenMarker) && !labelled.has(marker.cellId)) {
        labelled.set(marker.cellId, { x: 0, y: 0, seen: false, half: marker === chosenMarker ? 9 : 7 });
      }
    }
    let frame = 0;
    let lastView = '';
    let settling = 0;
    const place = () => {
      frame = requestAnimationFrame(place);
      const camera = globe.camera().position;
      const view = `${camera.x.toFixed(3)},${camera.y.toFixed(3)},${camera.z.toFixed(3)},${size.width},${size.height}`;
      // The camera's matrices are only brought up to date when the scene is drawn, which can be
      // after this runs. So the frame after the last move is placed too: without it, markers
      // are left where the camera was one frame before it stopped, well off on a slow device.
      if (view !== lastView) {
        lastView = view;
        settling = 2;
      } else if (settling === 0) {
        return;
      } else {
        settling--;
      }
      globe.camera().updateMatrixWorld();
      for (const { marker, altitude, position } of placed) {
        const element = markerElements.current.get(marker.cellId);
        if (!element) {
          continue;
        }
        // In front of the horizon as seen from the camera, not round the back of the globe.
        const facing = position.x * camera.x + position.y * camera.y + position.z * camera.z;
        const length = Math.hypot(position.x, position.y, position.z);
        if (facing <= radius * length) {
          element.style.visibility = 'hidden';
          continue;
        }
        const screen = globe.getScreenCoords(marker.lat, marker.lng, altitude);
        element.style.visibility = 'visible';
        element.style.transform = `translate(${screen.x.toFixed(1)}px, ${screen.y.toFixed(1)}px) translate(-50%, -50%)`;
        const wanted = labelled.get(marker.cellId);
        if (wanted) {
          wanted.x = screen.x;
          wanted.y = screen.y;
          wanted.seen = true;
        }
      }

      // Labels, in the same pass and with no measuring after the first: the chosen marker's
      // first, then in rank order. The first placed wins. A label that would sit on one already
      // placed, or whose dot would cover one, gives way. One that would run off the right-hand
      // edge goes to the left of its dot.
      const taken: [number, number, number, number][] = [];
      const clash = (left: number, top: number, right: number, bottom: number) =>
        taken.some((box) => left < box[2] && right > box[0] && top < box[3] && bottom > box[1]);
      for (const [cellId, label] of labelled) {
        const element = markerElements.current.get(cellId);
        if (!element) {
          continue;
        }
        let state: 'right' | 'left' | 'off' = 'off';
        if (label.seen) {
          label.seen = false;
          if (label.width === undefined) {
            label.width = (element.querySelector('.globe-marker-label') as HTMLElement | null)?.offsetWidth ?? 0;
          }
          const half = label.half;
          // A label that is not drawn at all (its width is nothing) takes no room and needs no placing.
          if (label.width > 0 && !clash(label.x - half, label.y - half, label.x + half, label.y + half)) {
            const top = label.y - LabelHalfHeight;
            const bottom = label.y + LabelHalfHeight;
            let left = label.x + half + LabelGap;
            state = 'right';
            if (left + label.width > size.width - LabelEdge && label.x - half - LabelGap - label.width >= LabelEdge) {
              left = label.x - half - LabelGap - label.width;
              state = 'left';
            }
            if (clash(left, top, left + label.width, bottom)) {
              state = 'off';
            } else {
              taken.push([left, top, left + label.width, bottom], [label.x - half, label.y - half, label.x + half, label.y + half]);
            }
          }
        }
        if (state !== label.state) {
          label.state = state;
          element.classList.toggle('globe-marker-label-left', state === 'left');
          element.classList.toggle('globe-marker-label-on', state !== 'off');
          element.classList.toggle('globe-marker-label-off', state === 'off');
        }
      }
    };
    place();
    return () => cancelAnimationFrame(frame);
  }, [ready, markers, highlightMarks, zoneLabel, sketch, size, liftAt, selectedCell]);

  // ----- Satellites -----

  const satelliteElements = useRef(new Map<string, HTMLElement>());
  // The satellite whose card is open. Hiding the satellites closes it.
  const [openSatelliteId, setOpenSatelliteId] = useState<string | null>(null);
  // Which satellites are in the scan follows the results on screen, as on the desktop.
  const missionSatellites = useMemo(
    () =>
      satellitesFor(
        (results.variables ?? []).map((variable) => variable.mission),
        (results.run?.retest?.by_variable ?? []).map((entry) => entry.product.mission),
      ),
    [results],
  );
  const openSatellite = showSatellites ? missionSatellites.find((satellite) => satellite.id === openSatelliteId) ?? null : null;
  useSatellites(globeRef, ready, showSatellites, satelliteElements, missionSatellites);

  // ----- Selection: outline the cell (and a contradiction's paired cell) and fly there -----

  const outlines = useMemo(() => {
    const lines: Outline[] = [];
    // With the relief on, a cell's outline runs round the top of its column.
    const solid = (cell: Cell) => {
      const centre = cellCentre(cell);
      const altitude = OutlineAltitude + liftAt(centre.lat, centre.lng);
      lines.push({ points: cellOutline(cell, altitude), colour: AccentColour, stroke: OutlineStroke, dashed: false });
    };
    if (selected) {
      solid(selected.cell);
      if (selected.contradiction?.paired_cell) {
        solid(selected.contradiction.paired_cell);
      }
    }
    if (highlight?.tone === 'cited') {
      // The cells an answer rests on are outlined as a selection is. Cells that were only read get the ring alone.
      highlightMarks
        .filter((mark) => mark.finding.cell.id !== selected?.cell.id)
        .slice(0, MaxHighlightOutlines)
        .forEach((mark) => solid(mark.finding.cell));
    }
    if (highlight?.box) {
      const { box } = highlight;
      const span = Math.max(box.lat_max - box.lat_min, box.lon_max - box.lon_min);
      // Thicker for a larger box, which is seen from further away.
      lines.push({
        points: boxOutline(box),
        colour: AccentColour,
        stroke: Math.min(0.6, Math.max(OutlineStroke, span * 0.01)),
        dashed: true,
      });
    }
    if (zone && zone.polygon.length >= 3) {
      const box = zoneBox(zone.polygon);
      const span = Math.max(box.lat_max - box.lat_min, box.lon_max - box.lon_min);
      lines.push({
        points: zoneOutline(zone.polygon),
        colour: AccentColour,
        stroke: Math.min(0.6, Math.max(OutlineStroke, span * 0.01)),
        dashed: false,
      });
      for (const [from, to] of zone.edges ?? []) {
        lines.push({ points: straightLine(from, to, true), colour: AccentColour, stroke: OutlineStroke, dashed: true });
      }
    }
    if (sketch && sketch.length >= 2) {
      // The outline as far as it has been drawn. It is closed only when it is finished.
      const points: [number, number, number][] = [];
      for (let i = 0; i < sketch.length - 1; i++) {
        points.push(...straightLine(sketch[i], sketch[i + 1], i === sketch.length - 2));
      }
      lines.push({ points, colour: AccentColour, stroke: OutlineStroke * 1.5, dashed: false });
    }
    return lines;
  }, [selected, highlight, highlightMarks, zone, sketch, liftAt]);

  useEffect(() => {
    if (!ready || !selected) {
      return;
    }
    stopRotation();
    globeRef.current?.pointOfView({ ...cellCentre(selected.cell), altitude: FlightAltitude }, FlightMilliseconds);
  }, [ready, selected, stopRotation]);

  // A highlight is a new object every time something is pointed out, so each one flies once.
  const aspectRef = useRef(1);
  aspectRef.current = size.height > 0 ? size.width / size.height : 1;
  useEffect(() => {
    const view = ready && highlight ? highlightView(highlight, aspectRef.current) : null;
    if (!view) {
      return;
    }
    stopRotation();
    globeRef.current?.pointOfView(view, FlightMilliseconds);
  }, [ready, highlight, stopRotation]);

  // A place chosen by name: the globe frames its box.
  useEffect(() => {
    const view = ready && goTo ? highlightView({ tone: 'read', title: '', findings: [], box: goTo }, aspectRef.current) : null;
    if (view) {
      stopRotation();
      globeRef.current?.pointOfView(view, FlightMilliseconds);
    }
  }, [ready, goTo, stopRotation]);

  // The handlers are read as they are now, so laying a point does not take the listeners off and on.
  const sketching = sketch !== null;
  const onSketchPointRef = useRef(onSketchPoint);
  onSketchPointRef.current = onSketchPoint;
  useEffect(() => {
    const host = hostRef.current;
    const globe = globeRef.current;
    if (!ready || !host || !globe || !sketching) {
      return;
    }
    stopRotation();
    let pressed: { x: number; y: number; id: number } | null = null;
    const onDown = (event: PointerEvent) => {
      // Only a press on the globe itself. One on a button over it is that button's.
      pressed = event.target instanceof HTMLCanvasElement ? { x: event.clientX, y: event.clientY, id: event.pointerId } : null;
    };
    const onUp = (event: PointerEvent) => {
      const start = pressed;
      pressed = null;
      // Dragging turns the globe, as ever. Only a press that stayed put is a point.
      if (!start || start.id !== event.pointerId || Math.hypot(event.clientX - start.x, event.clientY - start.y) > (event.pointerType === 'touch' ? TapSlopPixels : ClickSlopPixels)) {
        return;
      }
      const bounds = host.getBoundingClientRect();
      const place = globe.toGlobeCoords(event.clientX - bounds.left, event.clientY - bounds.top);
      if (place) {
        onSketchPointRef.current?.(place.lat, place.lng);
      }
    };
    // Heard on the way down to the canvas, before the globe's own controls take the pointer.
    host.addEventListener('pointerdown', onDown, true);
    host.addEventListener('pointerup', onUp, true);
    return () => {
      host.removeEventListener('pointerdown', onDown, true);
      host.removeEventListener('pointerup', onUp, true);
    };
  }, [ready, sketching, stopRotation]);

  // A zone is flown to when it is first drawn. Declared after the flights above, so that a
  // finding and a zone shown together end on the zone, which holds the finding.
  useEffect(() => {
    if (!ready || !zone || zone.polygon.length < 3) {
      return;
    }
    const view = highlightView({ tone: 'read', title: '', findings: [], box: zoneBox(zone.polygon) }, aspectRef.current);
    if (view) {
      stopRotation();
      globeRef.current?.pointOfView(view, FlightMilliseconds);
    }
  }, [ready, zone, stopRotation]);

  const selectedCellId = selected?.cell.id ?? null;
  const marked = highlight?.findings.length ?? 0;

  return (
    <div className={layerShown && yearsShown ? 'globe-view globe-view-years' : 'globe-view'} ref={hostRef}>
      {size.width > 0 && size.height > 0 && (
        <Globe
          ref={globeRef}
          width={size.width}
          height={size.height}
          backgroundColor={BackgroundColour}
          globeImageUrl={earthImage}
          showAtmosphere={false}
          onGlobeReady={() => setReady(true)}
          pathsData={outlines}
          pathPoints="points"
          pathColor={(outline: object) => (outline as Outline).colour}
          pathStroke={(outline: object) => (outline as Outline).stroke}
          pathDashLength={(outline: object) => ((outline as Outline).dashed ? BoxDashLength : 1)}
          pathDashGap={(outline: object) => ((outline as Outline).dashed ? BoxDashGap : 0)}
          pathPointLat={pathLat}
          pathPointLng={pathLng}
          pathPointAlt={pathAltitude}
          pathTransitionDuration={0}
        />
      )}
      {ready && (
        <div
          className={
            'globe-markers' + (highlight ? ' globe-markers-highlighting' : '') + (sketching ? ' globe-markers-drawing' : '')
          }
        >
          {markers.map((marker) => (
            <button
              key={marker.cellId}
              type="button"
              ref={(element) => {
                if (element) {
                  markerElements.current.set(marker.cellId, element);
                } else {
                  markerElements.current.delete(marker.cellId);
                }
              }}
              className={
                'globe-marker' +
                (marker.significant ? ' globe-marker-significant' : '') +
                (marker.labelled ? '' : ' globe-marker-quiet') +
                (marker.cellId === selectedCellId ? ' globe-marker-selected' : '')
              }
              title={marker.finding.summary}
              aria-label={`${marker.label}: ${marker.finding.summary}`}
              onClick={() => onFindingClick(marker.finding)}
            >
              <span className="globe-marker-label">{marker.label}</span>
            </button>
          ))}
          {highlightMarks.map((mark) => (
            <button
              key={mark.key}
              type="button"
              ref={(element) => {
                if (element) {
                  markerElements.current.set(mark.key, element);
                } else {
                  markerElements.current.delete(mark.key);
                }
              }}
              className={highlight?.tone === 'cited' ? 'globe-mark' : 'globe-mark globe-mark-read'}
              title={mark.finding.summary}
              aria-label={`${highlight?.title ?? ''}: #${mark.finding.rank.toLocaleString('en-GB')}, ${mark.finding.summary}`}
              onClick={() => onFindingClick(mark.finding)}
            >
              {highlight?.tone === 'cited' && (
                <span className="globe-marker-label">#{mark.finding.rank.toLocaleString('en-GB')}</span>
              )}
            </button>
          ))}
          {(sketch ?? []).map((_, index) => {
            const place = (element: HTMLElement | null) => {
              if (element) {
                markerElements.current.set(`sketch:${index}`, element as unknown as HTMLButtonElement);
              } else {
                markerElements.current.delete(`sketch:${index}`);
              }
            };
            // The first point closes the outline once there are enough points to close.
            return index === 0 && sketch && sketch.length >= 3 ? (
              <button key={index} type="button" ref={place} className="globe-sketch-point globe-sketch-first" onClick={onSketchFinish} aria-label="Close the outline" title="Close the outline" />
            ) : (
              <span key={index} ref={place} className="globe-sketch-point" />
            );
          })}
          {zone && zoneLabel && (
            <span
              ref={(element) => {
                if (element) {
                  markerElements.current.set(ZoneLabelKey, element as unknown as HTMLButtonElement);
                } else {
                  markerElements.current.delete(ZoneLabelKey);
                }
              }}
              className="globe-zone-label"
            >
              {zone.name}
            </span>
          )}
          {showSatellites &&
            missionSatellites.map((satellite) => (
              <button
                type="button"
                key={satellite.id}
                ref={(element) => {
                  if (element) {
                    satelliteElements.current.set(satellite.id, element);
                  } else {
                    satelliteElements.current.delete(satellite.id);
                  }
                }}
                className={
                  'globe-satellite' +
                  (satellite.inScan ? '' : ' globe-satellite-quiet') +
                  (satellite.id === openSatelliteId ? ' globe-satellite-open' : '')
                }
                aria-label={`About ${satellite.label}`}
                onClick={() => setOpenSatelliteId(satellite.id)}
                title={`${satellite.detail} ${satellite.inScan ? 'In the scan.' : 'Not in the scan.'} ${satelliteCaveat}`}
              >
                <span className="globe-marker-label">{satellite.label}</span>
              </button>
            ))}
        </div>
      )}
      {openSatellite && <SatelliteCard satellite={openSatellite} onClose={() => setOpenSatelliteId(null)} />}
      {highlight && (
        <aside className="globe-shown" aria-label={highlight.title}>
          <p className="globe-shown-title">{highlight.title}</p>
          {highlight.caption && <p>{highlight.caption}</p>}
          <p className="numeric">
            {marked === 0
              ? 'No findings to ring.'
              : `${marked.toLocaleString('en-GB')} ${marked === 1 ? 'finding' : 'findings'} ringed${
                  highlightMarks.length < marked ? ` in ${highlightMarks.length.toLocaleString('en-GB')} ${highlightMarks.length === 1 ? 'cell' : 'cells'}` : ''
                }.`}
            {highlight.box ? ' The dashed outline is where it searched.' : ''}
          </p>
          <div className="globe-shown-actions">
            {onBackToDetective && (
              <button type="button" className="button" onClick={onBackToDetective}>
                Back to the Detective
              </button>
            )}
            <button type="button" className="button" onClick={onClearHighlight}>
              Clear
            </button>
          </div>
        </aside>
      )}
      {layerShown && grid && (
        <GlobeLegend
          title={
            yearsShown
              ? `${labelText(grid.variable)} in ${yearsShown.years[year]}, ${yearsShown.units}`
              : `${labelText(grid.variable)} trend, ${grid.units}`
          }
          limit={limit}
          // What the scale itself does not say. The same words as the desktop app's legend.
          note={
            yearsShown && reliefShown
              ? 'Against its normal. Height: how far from it.'
              : yearsShown
                ? "Each year against the cell's own normal"
                : reliefShown
                  ? 'Height: the size of the trend, either way'
                  : undefined
          }
        />
      )}
      {sketch && (
        <div className="globe-drawing" role="group" aria-label="Drawing a zone">
          <p className="numeric" role="status">
            {sketch.length === 0
              ? 'Click or tap the globe to lay the first point.'
              : sketch.length < 3
                ? `${sketch.length} of at least 3 points.`
                : `${sketch.length} points. The first point, or Finish, closes the outline.`}
          </p>
          <div className="globe-drawing-actions">
            <button type="button" className="button" onClick={onSketchUndo} disabled={sketch.length === 0}>
              Undo
            </button>
            <button type="button" className="button primary" onClick={onSketchFinish} disabled={sketch.length < 3}>
              Finish
            </button>
            <button type="button" className="button" onClick={onSketchCancel}>
              Cancel
            </button>
          </div>
        </div>
      )}
      {layerShown && yearsShown && !sketch && (
        <YearBar years={yearsShown.years} index={year} onIndex={setYearIndex} playing={playing} onPlaying={setPlaying} />
      )}
    </div>
  );
}
