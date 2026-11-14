import { useEffect } from 'react';
import type { MutableRefObject } from 'react';
import type { GlobeMethods } from 'react-globe.gl';
import { BufferAttribute, BufferGeometry, Color, Line, LineBasicMaterial, LineDashedMaterial } from 'three';
import { missionSatellites, orbitPoint, phaseAt } from './satellite';
import type { Satellite } from './satellite';

const SatelliteColour = '#5dbe8f'; // mirrors --positive, the colour the desktop app gives its satellite
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
 * labelled dot (a DOM element from `elements`, keyed by satellite id) moved each frame.
 */
export function useSatellites(
  globeRef: MutableRefObject<GlobeMethods | undefined>,
  ready: boolean,
  shown: boolean,
  elements: MutableRefObject<Map<string, HTMLElement>>,
): void {
  useEffect(() => {
    const globe = globeRef.current;
    if (!ready || !shown || !globe) {
      return;
    }
    const scene = globe.scene();
    const radius = globe.getGlobeRadius();
    const colour = new Color(SatelliteColour);

    const drawn: Drawn[] = missionSatellites.map((satellite) => {
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
    const tick = () => {
      frame = requestAnimationFrame(tick);
      const now = Date.now();
      const redrawRings = now - ringsDrawnAt >= RingRefreshMs;
      if (redrawRings) {
        ringsDrawnAt = now;
      }
      const camera = globe.camera().position;

      for (const { satellite, ring, nadir } of drawn) {
        const phase = phaseAt(satellite, now);
        const here = orbitPoint(satellite, now, phase);
        const position = globe.getCoords(here.lat, here.lng, here.altitude);
        const ground = globe.getCoords(here.lat, here.lng, 0);

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
            const place = globe.getCoords(point.lat, point.lng, point.altitude);
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
        const screen = globe.getScreenCoords(here.lat, here.lng, here.altitude);
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
  }, [globeRef, ready, shown, elements]);
}
