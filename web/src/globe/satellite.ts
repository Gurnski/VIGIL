// Satellites on circular orbits, placed against the real clock. A port of the desktop app's
// GlobeSatellite.cs and SunPosition.cs: the orbit is fixed against the stars and the Earth
// turns underneath it.
//
// What is real and what is not: each orbit's height, tilt and period are the mission's own,
// and a sun-synchronous orbit's plane sits where it really is today. Where the satellite is
// along its orbit is not tracked from live orbit data, so the dot shows how the satellite
// moves, not where it is right now. The label on the globe says so.

const EarthRadiusKm = 6371;
const EarthMu = 398600.4418; // km³/s²
const J2000 = Date.UTC(2000, 0, 1, 12, 0, 0);
const DefaultEpoch = Date.UTC(2026, 0, 1, 0, 0, 0);
const radians = Math.PI / 180;

export interface Satellite {
  id: string;
  label: string;
  /** Shown on hover: what it measures for VIGIL. */
  detail: string;
  inclinationDegrees: number;
  altitudeKm: number;
  /**
   * Local solar time, in hours, at which a sun-synchronous orbit crosses the equator
   * northbound. When set, the orbit plane follows the sun as the real one does.
   */
  ascendingNodeLocalTime?: number;
  /** Right ascension of the ascending node in degrees, used when there is no local time. */
  ascendingNodeDegrees?: number;
  /** Degrees along the orbit from the ascending node at the epoch. */
  phaseAtEpochDegrees?: number;
}

/**
 * The satellites behind two of the scan's four variables. The other two, MERRA-2 and GLDAS,
 * are models, not single spacecraft, so they have no orbit to draw.
 */
export const missionSatellites: Satellite[] = [
  {
    id: 'terra',
    label: 'Terra',
    detail: 'Carries MODIS, the source of the vegetation (NDVI) record.',
    inclinationDegrees: 98.2,
    altitudeKm: 705,
    // Terra crosses the equator southbound at 10:30, so northbound at 22:30.
    ascendingNodeLocalTime: 22.5,
  },
  {
    id: 'gpm',
    label: 'GPM Core Observatory',
    detail: 'The reference satellite of GPM IMERG, the source of the precipitation record.',
    inclinationDegrees: 65,
    altitudeKm: 407,
    // Not sun-synchronous: its plane drifts round the Earth, so this starting angle is arbitrary.
    ascendingNodeDegrees: 40,
    phaseAtEpochDegrees: 120,
  },
];

export interface OrbitPoint {
  lat: number;
  lng: number;
  /** Height above the surface in Earth radii, the unit the globe library uses. */
  altitude: number;
}

function daysSinceJ2000(time: number): number {
  return (time - J2000) / 86400000;
}

/** The sun's right ascension in degrees (US Naval Observatory approximation). */
function sunRightAscension(time: number): number {
  const days = daysSinceJ2000(time);
  const meanAnomaly = (357.529 + 0.98560028 * days) * radians;
  const meanLongitude = 280.459 + 0.98564736 * days;
  const eclipticLongitude = (meanLongitude + 1.915 * Math.sin(meanAnomaly) + 0.02 * Math.sin(2 * meanAnomaly)) * radians;
  const obliquity = (23.439 - 0.00000036 * days) * radians;
  return Math.atan2(Math.cos(obliquity) * Math.sin(eclipticLongitude), Math.cos(eclipticLongitude)) / radians;
}

/** How far the Earth has turned against the stars, in degrees. */
function greenwichSiderealDegrees(time: number): number {
  const degrees = (280.46061837 + 360.98564736629 * daysSinceJ2000(time)) % 360;
  return degrees < 0 ? degrees + 360 : degrees;
}

/** Seconds for one orbit, from Kepler's third law. */
export function periodSeconds(satellite: Satellite): number {
  const a = EarthRadiusKm + satellite.altitudeKm;
  return 2 * Math.PI * Math.sqrt((a * a * a) / EarthMu);
}

/** Radians along the orbit from the ascending node at a time (milliseconds since 1970, UTC). */
export function phaseAt(satellite: Satellite, time: number): number {
  const orbits = (time - DefaultEpoch) / 1000 / periodSeconds(satellite);
  return (satellite.phaseAtEpochDegrees ?? 0) * radians + 2 * Math.PI * (orbits - Math.floor(orbits));
}

function nodeDegrees(satellite: Satellite, time: number): number {
  if (satellite.ascendingNodeLocalTime === undefined) {
    return satellite.ascendingNodeDegrees ?? 0;
  }
  // Local solar time is 12:00 under the sun, and each hour is 15° of right ascension.
  return sunRightAscension(time) + (satellite.ascendingNodeLocalTime - 12) * 15;
}

/** The point on the orbit at a phase, with the Earth turned as it is at that time. */
export function orbitPoint(satellite: Satellite, time: number, phase: number): OrbitPoint {
  const node = nodeDegrees(satellite, time) * radians;
  const inclination = satellite.inclinationDegrees * radians;
  const cosPhase = Math.cos(phase);
  const sinPhase = Math.sin(phase);

  // Equatorial frame: x towards the vernal equinox, z to the north pole.
  const x = Math.cos(node) * cosPhase - Math.sin(node) * sinPhase * Math.cos(inclination);
  const y = Math.sin(node) * cosPhase + Math.cos(node) * sinPhase * Math.cos(inclination);
  const z = sinPhase * Math.sin(inclination);

  // Turn with the Earth, which gives a longitude on the map.
  const sidereal = greenwichSiderealDegrees(time) * radians;
  const xe = x * Math.cos(sidereal) + y * Math.sin(sidereal);
  const ye = -x * Math.sin(sidereal) + y * Math.cos(sidereal);

  return {
    lat: Math.asin(Math.max(-1, Math.min(1, z))) / radians,
    lng: Math.atan2(ye, xe) / radians,
    altitude: satellite.altitudeKm / EarthRadiusKm,
  };
}

/** Where the satellite is at a time. */
export function positionAt(satellite: Satellite, time: number): OrbitPoint {
  return orbitPoint(satellite, time, phaseAt(satellite, time));
}
