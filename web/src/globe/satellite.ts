// Satellites on circular orbits, placed against the real clock. A port of the desktop app's
// GlobeSatellite.cs and SunPosition.cs: the orbit is fixed against the stars and the Earth
// turns underneath it.
//
// What is real and what is not: each orbit's height, tilt and period are the mission's own,
// and a sun-synchronous orbit's plane sits where it really is today. Where the satellite is
// along its orbit is not tracked from live orbit data, so the dot shows how the satellite
// moves, not where it is right now. The label on the globe says so.

import aquaPicture from '../../../assets/satellites/aqua.png';
import gpmPicture from '../../../assets/satellites/gpm.png';
import graceFoPicture from '../../../assets/satellites/grace-fo.jpg';
import smapPicture from '../../../assets/satellites/smap.jpg';
import suomiNppPicture from '../../../assets/satellites/suomi-npp.jpg';
import terraPicture from '../../../assets/satellites/terra.png';

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
  /** Who runs the mission. */
  operator: string;
  launched: string;
  /** What it carries, the instrument VIGIL's source depends on first. */
  instrument: string;
  /** A public domain NASA picture of it. Credits are in assets/satellites/README.md. */
  picture: string;
  /** True when its data is in the first scan, false when it is behind a source kept for cross-checks. */
  inScan: boolean;
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

/** The line to show wherever the satellites are explained. */
export const satelliteCaveat = "Each orbit is the mission's real one. Places along the orbits are for illustration, not tracked live.";

/**
 * The satellites behind VIGIL's data. The desktop app has the same list in
 * VIGIL/VIGIL/Controls/Globe/MissionSatellites.cs: keep the two in step. MERRA-2 and GLDAS
 * are models, not single spacecraft, so they have no orbit to draw.
 */
export const missionSatellites: Satellite[] = [
  // In the first scan.
  {
    id: 'terra',
    label: 'Terra',
    detail: 'Carries MODIS, the source of the vegetation (NDVI) record.',
    operator: 'NASA',
    launched: '18 December 1999',
    instrument: 'MODIS, with ASTER, CERES, MISR and MOPITT',
    picture: terraPicture,
    inScan: true,
    inclinationDegrees: 98.2,
    altitudeKm: 705,
    // Terra crosses the equator southbound at 10:30, so northbound at 22:30.
    ascendingNodeLocalTime: 22.5,
  },
  {
    id: 'gpm',
    label: 'GPM Core Observatory',
    detail: 'The reference satellite of GPM IMERG, the source of the precipitation record.',
    operator: 'NASA and JAXA',
    launched: '27 February 2014',
    instrument: 'GMI microwave imager and DPR radar',
    picture: gpmPicture,
    inScan: true,
    inclinationDegrees: 65,
    altitudeKm: 407,
    // Not sun-synchronous: its plane drifts round the Earth, so this starting angle is arbitrary.
    ascendingNodeDegrees: 40,
    phaseAtEpochDegrees: 120,
  },

  // Behind the sources the brief keeps for cross-checks. Not in the scan.
  {
    id: 'aqua',
    label: 'Aqua',
    detail: "Carries a second MODIS, for checking a vegetation trend against Terra's.",
    operator: 'NASA',
    launched: '4 May 2002',
    instrument: 'MODIS, with AIRS, AMSU and CERES',
    picture: aquaPicture,
    inScan: false,
    inclinationDegrees: 98.2,
    altitudeKm: 705,
    ascendingNodeLocalTime: 13.5,
    phaseAtEpochDegrees: 200,
  },
  {
    id: 'suomi-npp',
    label: 'Suomi NPP',
    detail: 'Carries VIIRS, which continues the MODIS vegetation record.',
    operator: 'NASA and NOAA',
    launched: '28 October 2011',
    instrument: 'VIIRS, with CrIS, ATMS, OMPS and CERES',
    picture: suomiNppPicture,
    inScan: false,
    inclinationDegrees: 98.7,
    altitudeKm: 824,
    ascendingNodeLocalTime: 13.5,
    phaseAtEpochDegrees: 75,
  },
  {
    id: 'smap',
    label: 'SMAP',
    detail: 'Measures soil moisture from orbit, for comparing against the GLDAS model.',
    operator: 'NASA',
    launched: '31 January 2015',
    instrument: 'L-band radiometer',
    picture: smapPicture,
    inScan: false,
    inclinationDegrees: 98.1,
    altitudeKm: 685,
    ascendingNodeLocalTime: 18,
    phaseAtEpochDegrees: 290,
  },
  {
    id: 'grace-fo',
    label: 'GRACE-FO',
    // Two spacecraft about 220 km apart on one orbit, drawn as one. The plane drifts, so the angle is arbitrary.
    detail: 'A pair of satellites that weigh the water stored in a region through its pull on them.',
    operator: 'NASA and GFZ',
    launched: '22 May 2018',
    instrument: 'Microwave and laser ranging between the two spacecraft',
    picture: graceFoPicture,
    inScan: false,
    inclinationDegrees: 89,
    altitudeKm: 490,
    ascendingNodeDegrees: 160,
    phaseAtEpochDegrees: 30,
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

/** The orbit in words: "705 km up, tilted 98.2°, one lap in about 99 minutes". */
export function orbitText(satellite: Satellite): string {
  const minutes = Math.round(periodSeconds(satellite) / 60);
  return `${satellite.altitudeKm} km up, tilted ${satellite.inclinationDegrees}°, one lap in about ${minutes} minutes`;
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
