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
import type { Cell, Finding, ResultsDocument } from '../contract';
import { cellCentre, labelText } from '../format';
import { scaleLimit } from './colourScale';
import { GlobeLegend } from './GlobeLegend';
import { paintLayer } from './layerTexture';
import { missionSatellites } from './satellite';
import { useSatellites } from './useSatellites';
import './globe.css';

export interface GlobeViewProps {
  results: ResultsDocument;
  /** The variable whose layer is painted, or null for none. */
  layerVariable: string | null;
  showLayer: boolean;
  /** Paint only the cells whose adjusted p-value is below the run's threshold. */
  significantOnly: boolean;
  /** The chosen finding: outline its cell and fly to it when it changes. */
  selected: Finding | null;
  /** Draw the satellites behind the data on their orbits. */
  showSatellites: boolean;
  /** A finding's marker was clicked. */
  onFindingClick: (finding: Finding) => void;
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
const DefaultThreshold = 0.05;
const FlightAltitude = 0.6;
const FlightMilliseconds = 1200;
const AutoRotateSpeed = 0.35;
/** Said wherever a satellite is named: the orbit is the real one, the place along it is not tracked. */
const SatelliteCaveat = 'The orbit is its real one. Its place along the orbit is shown for illustration, not tracked live.';

interface Marker {
  cellId: string;
  lat: number;
  lng: number;
  label: string;
  significant: boolean;
  /** The top-ranked finding in the cell: what a click selects. */
  finding: Finding;
}

/** The cell's bounds as a closed path of [lat, lng] corners. */
function cellOutline(cell: Cell): [number, number][] {
  return [
    [cell.lat_min, cell.lon_min],
    [cell.lat_min, cell.lon_max],
    [cell.lat_max, cell.lon_max],
    [cell.lat_max, cell.lon_min],
    [cell.lat_min, cell.lon_min],
  ];
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

/** The 3D globe: the trend layer, a marker per cell with findings, and the selected cell's outline. */
export function GlobeView({ results, layerVariable, showLayer, significantOnly, selected, showSatellites, onFindingClick }: GlobeViewProps) {
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
  const limit = useMemo(() => (grid ? scaleLimit(grid.slope_per_decade) : 1), [grid]);
  const threshold = results.run?.settings.threshold ?? DefaultThreshold;
  const layerCanvas = useMemo(
    () => (grid ? paintLayer(grid, limit, significantOnly, threshold) : null),
    [grid, limit, significantOnly, threshold],
  );

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
  useEffect(() => {
    if (layerMeshRef.current) {
      layerMeshRef.current.visible = layerShown;
    }
  }, [ready, layerShown]);

  // ----- Markers: one per cell that has a finding -----

  const markers = useMemo(() => {
    const byCell = new Map<string, Finding[]>();
    const ranked = [...(results.findings ?? [])].sort((a, b) => a.rank - b.rank).slice(0, MaxMarkedFindings);
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
        significant: here.some((finding) => finding.significant),
        finding: top,
      });
    }
    return list;
  }, [results.findings]);

  // Markers are plain DOM buttons placed over the canvas each frame, so they are real buttons
  // for the keyboard and screen readers and are only moved when the camera has.
  const markerElements = useRef(new Map<string, HTMLButtonElement>());
  useEffect(() => {
    const globe = globeRef.current;
    if (!ready || !globe) {
      return;
    }
    const radius = globe.getGlobeRadius();
    const placed = markers.map((marker) => ({
      marker,
      position: globe.getCoords(marker.lat, marker.lng, MarkerAltitude),
    }));
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
      for (const { marker, position } of placed) {
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
        const screen = globe.getScreenCoords(marker.lat, marker.lng, MarkerAltitude);
        element.style.visibility = 'visible';
        element.style.transform = `translate(${screen.x.toFixed(1)}px, ${screen.y.toFixed(1)}px) translate(-50%, -50%)`;
      }
    };
    place();
    return () => cancelAnimationFrame(frame);
  }, [ready, markers, size]);

  // ----- Satellites -----

  const satelliteElements = useRef(new Map<string, HTMLElement>());
  useSatellites(globeRef, ready, showSatellites, satelliteElements);

  // ----- Selection: outline the cell (and a contradiction's paired cell) and fly there -----

  const outlines = useMemo(() => {
    if (!selected) {
      return [];
    }
    const cells = [selected.cell];
    if (selected.contradiction?.paired_cell) {
      cells.push(selected.contradiction.paired_cell);
    }
    return cells.map(cellOutline);
  }, [selected]);

  useEffect(() => {
    if (!ready || !selected) {
      return;
    }
    stopRotation();
    globeRef.current?.pointOfView({ ...cellCentre(selected.cell), altitude: FlightAltitude }, FlightMilliseconds);
  }, [ready, selected, stopRotation]);

  const selectedCellId = selected?.cell.id ?? null;

  return (
    <div className="globe-view" ref={hostRef}>
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
          pathColor={() => AccentColour}
          pathStroke={OutlineStroke}
          pathPointAlt={OutlineAltitude}
          pathTransitionDuration={0}
        />
      )}
      {ready && (
        <div className="globe-markers">
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
                (marker.cellId === selectedCellId ? ' globe-marker-selected' : '')
              }
              title={marker.finding.summary}
              aria-label={`${marker.label}: ${marker.finding.summary}`}
              onClick={() => onFindingClick(marker.finding)}
            >
              <span className="globe-marker-label">{marker.label}</span>
            </button>
          ))}
          {showSatellites &&
            missionSatellites.map((satellite) => (
              <div
                key={satellite.id}
                ref={(element) => {
                  if (element) {
                    satelliteElements.current.set(satellite.id, element);
                  } else {
                    satelliteElements.current.delete(satellite.id);
                  }
                }}
                className="globe-satellite"
                title={satellite.detail + ' ' + SatelliteCaveat}
              >
                <span className="globe-marker-label">{satellite.label}</span>
              </div>
            ))}
        </div>
      )}
      {layerShown && grid && <GlobeLegend title={`${labelText(grid.variable)} trend, ${grid.units}`} limit={limit} />}
    </div>
  );
}
