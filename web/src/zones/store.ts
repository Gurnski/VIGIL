import type { ZonePoint } from './geometry';

// Saved zones, kept in the browser's localStorage. A zone is only a name and an outline. It
// belongs to no scan: which grid cells it takes in is worked out against the results on screen.

export interface SavedZone {
  id: string;
  name: string;
  /** When it was saved, as an ISO timestamp. */
  created: string;
  /** The outline, closed from its last point back to its first. Longitudes are continuous. */
  points: ZonePoint[];
}

const storageKey = 'vigil.zones';

function isPoint(value: unknown): value is ZonePoint {
  return Array.isArray(value) && value.length === 2 && value.every((part) => typeof part === 'number' && Number.isFinite(part));
}

/** The zones saved in this browser. Anything that is not a zone is left out, never repaired. */
export function loadZones(): SavedZone[] {
  try {
    const stored: unknown = JSON.parse(window.localStorage.getItem(storageKey) ?? '[]');
    if (!Array.isArray(stored)) {
      return [];
    }
    return stored.filter(
      (zone): zone is SavedZone =>
        typeof zone === 'object' &&
        zone !== null &&
        typeof (zone as SavedZone).id === 'string' &&
        typeof (zone as SavedZone).name === 'string' &&
        Array.isArray((zone as SavedZone).points) &&
        (zone as SavedZone).points.length >= 3 &&
        (zone as SavedZone).points.every(isPoint),
    );
  } catch {
    // No storage, or something else's text under the key: start with no zones.
    return [];
  }
}

/** Writes the zones. False when the browser would not keep them, as in some private windows. */
export function storeZones(zones: SavedZone[]): boolean {
  try {
    window.localStorage.setItem(storageKey, JSON.stringify(zones));
    return true;
  } catch {
    return false;
  }
}

export function newZoneId(): string {
  return `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 8)}`;
}
