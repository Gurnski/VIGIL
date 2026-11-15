import { useEffect } from 'react';
import type { MutableRefObject } from 'react';
import { BufferAttribute, BufferGeometry, Color, Line, LineBasicMaterial, LineDashedMaterial } from 'three';
import { orbitPoint, phaseAt } from './satellite';
import type { Satellite } from './satellite';
import type { GlobeStage, ScreenPoint } from './stage';

// A satellite whose data is in the scan takes the stronger colour, as in the desktop app.
const InScanColour = '#5dbe8f'; // mirrors --positive
const CrossCheckColour = '#a6a5a0'; // mirrors --text-secondary
const RingSegments = 180;
/** The ring is brightest just behind the satellite and fades towards the stretch ahead of it. */
const RingOpacityBehind = 0.85;
const RingOpacityAhead = 0.12;
/** The Earth turns a quarter of a degree a minute under the orbit, so the ring is redrawn this often. */
const RingRefreshMs = 2000;

interface Drawn {
  satellite: Satellite;
  ring: Line<BufferGeometry, LineBasicMaterial>;
  nadir: Line<BufferGeometry, LineDashedMaterial>;
}

/**
 * Draws each satellite as the desktop globe does: its orbit as a ring at the right height,
 * hidden where the Earth is in the way, a dotted line down to the point beneath it, and a
 * labelled dot (a DOM element from `elements`, keyed by satellite id) moved each frame. They
 * are drawn round the globe only: the caller does not show them while it is unrolled flat.
 */
export function useSatellites(
  stage: GlobeStage | null,
  shown: boolean,
  elements: MutableRefObject<Map<string, HTMLElement>>,
  missionSatellites: Satellite[],
): void {
  useEffect(() => {
    if (!shown || !stage) {
      return;
    }
    // In the group the library animates, so the orbits open with the globe.
    const scene = stage.group;
    const radius = stage.radius;
    const drawn: Drawn[] = missionSatellites.map((satellite) => {
      const colour = new Color(satellite.inScan ? InScanColour : CrossCheckColour);
      const ringGeometry = new BufferGeometry();
      ringGeometry.setAttribute('position', new BufferAttribute(new Float32Array((RingSegments + 1) * 3), 3));
      const colours = new Float32Array((RingSegments + 1) * 4);
      for (let i = 0; i <= RingSegments; i++) {
        const along = i / RingSegments;
        colours.set([colour.r, colour.g, colour.b, RingOpacityBehind + (RingOpacityAhead - RingOpacityBehind) * along], i * 4);
      }
      ringGeometry.setAttribute('color', new BufferAttribute(colours, 4));
      const ring = new Line(ringGeometry, new LineBasicMaterial({ vertexColors: true, transparent: true, depthWrite: false }));
      // The points move every refresh, so the bounds three.js would cull against go stale.
      ring.frustumCulled = false;

      const nadirGeometry = new BufferGeometry();
      nadirGeometry.setAttribute('position', new BufferAttribute(new Float32Array(6), 3));
      const nadir = new Line(
        nadirGeometry,
        new LineDashedMaterial({
          color: colour,
          dashSize: radius * 0.012,
          gapSize: radius * 0.012,
          transparent: true,
          opacity: 0.7,
          depthWrite: false,
        }),
      );
      nadir.frustumCulled = false;

      scene.add(ring, nadir);
      return { satellite, ring, nadir };
    });

    let frame = 0;
    let ringsDrawnAt = 0;
    const position = { x: 0, y: 0, z: 0 };
    const ground = { x: 0, y: 0, z: 0 };
    const place = { x: 0, y: 0, z: 0 };
    const screen: ScreenPoint = { x: 0, y: 0, visible: false };
    const tick = () => {
      frame = requestAnimationFrame(tick);
      const now = Date.now();
      const redrawRings = now - ringsDrawnAt >= RingRefreshMs;
      if (redrawRings) {
        ringsDrawnAt = now;
      }
      const camera = stage.view.position;

      for (const { satellite, ring, nadir } of drawn) {
        const phase = phaseAt(satellite, now);
        const here = orbitPoint(satellite, now, phase);
        stage.coords(here.lat, here.lng, here.altitude, position);
        stage.coords(here.lat, here.lng, 0, ground);

        const line = nadir.geometry.getAttribute('position') as BufferAttribute;
        line.setXYZ(0, position.x, position.y, position.z);
        line.setXYZ(1, ground.x, ground.y, ground.z);
        line.needsUpdate = true;
        nadir.computeLineDistances();

        if (redrawRings) {
          // The ring starts at the satellite and runs backwards round the orbit.
          const points = ring.geometry.getAttribute('position') as BufferAttribute;
          for (let i = 0; i <= RingSegments; i++) {
            const point = orbitPoint(satellite, now, phase - (i / RingSegments) * 2 * Math.PI);
            stage.coords(point.lat, point.lng, point.altitude, place);
            points.setXYZ(i, place.x, place.y, place.z);
          }
          points.needsUpdate = true;
        }

        const element = elements.current.get(satellite.id);
        if (!element) {
          continue;
        }
        // Hidden when the Earth is between the camera and the satellite: the nearest point of
        // the line of sight to the Earth's centre is inside the globe.
        const dx = position.x - camera.x;
        const dy = position.y - camera.y;
        const dz = position.z - camera.z;
        const along = Math.max(0, Math.min(1, -(camera.x * dx + camera.y * dy + camera.z * dz) / (dx * dx + dy * dy + dz * dz)));
        const nearest = Math.hypot(camera.x + dx * along, camera.y + dy * along, camera.z + dz * along);
        if (nearest < radius) {
          element.style.visibility = 'hidden';
          continue;
        }
        stage.screen(here.lat, here.lng, here.altitude, screen);
        element.style.visibility = 'visible';
        element.style.transform = `translate(${screen.x.toFixed(1)}px, ${screen.y.toFixed(1)}px) translate(-50%, -50%)`;
      }
    };
    tick();

    return () => {
      cancelAnimationFrame(frame);
      for (const { ring, nadir } of drawn) {
        scene.remove(ring, nadir);
        ring.geometry.dispose();
        ring.material.dispose();
        nadir.geometry.dispose();
        nadir.material.dispose();
      }
    };
  }, [stage, shown, elements, missionSatellites]);
}
