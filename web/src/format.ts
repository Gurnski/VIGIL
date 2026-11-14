import type { Cell } from './contract';

/** A contract id as a label: "Change point" for change_point, "Soil moisture" for soil_moisture. */
export function labelText(id: string): string {
  const words = id.replace(/_/g, ' ');
  return words.length === 0 ? '' : words[0].toUpperCase() + words.slice(1);
}

export function cellCentre(cell: Cell): { lat: number; lng: number } {
  return { lat: (cell.lat_min + cell.lat_max) / 2, lng: (cell.lon_min + cell.lon_max) / 2 };
}

/** The centre of a cell as "8.8°S 51.3°W". */
export function placeText(cell: Cell): string {
  const { lat, lng } = cellCentre(cell);
  return `${Math.abs(lat).toFixed(1)}°${lat < 0 ? 'S' : 'N'} ${Math.abs(lng).toFixed(1)}°${lng < 0 ? 'W' : 'E'}`;
}

/** Whole numbers with thousands separators, British style: 9,064. */
export function count(value: number): string {
  return value.toLocaleString('en-GB');
}
