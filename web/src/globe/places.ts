// Places worth going to on the globe: Earth-science regions, each a box in latitude and
// longitude with a line about why it matters. The same list as the desktop app's
// VIGIL/VIGIL/Controls/Globe/Places.cs: keep the two in step. It is in the code, so finding a
// place asks nothing of the network.

export interface Place {
  name: string;
  lat_min: number;
  lat_max: number;
  /** West and east edges. A place that crosses the date line has its west edge the larger, as the Pacific Ocean does. */
  lon_min: number;
  lon_max: number;
  note: string;
}

export const places: Place[] = [
  { name: 'Amazon basin', lat_min: -16, lat_max: 5, lon_min: -75, lon_max: -45, note: "Tropical forest cover, NDVI and deforestation fronts." },
  { name: 'Greenland', lat_min: 59, lat_max: 84, lon_min: -73, lon_max: -11, note: "Ice-sheet surface melt and mass loss." },
  { name: 'Arctic Ocean', lat_min: 70, lat_max: 85, lon_min: -180, lon_max: 180, note: "Sea-ice extent and the fastest warming on the planet." },
  { name: 'Antarctica', lat_min: -85, lat_max: -63, lon_min: -180, lon_max: 180, note: "Ice shelves, sea ice and the polar vortex." },
  { name: 'Sahel', lat_min: 10, lat_max: 20, lon_min: -18, lon_max: 40, note: "Rainfall recovery and greening at the desert's edge." },
  { name: 'Sahara', lat_min: 17, lat_max: 32, lon_min: -17, lon_max: 36, note: "Dust sources and the world's largest hot desert." },
  { name: 'Southern Ocean', lat_min: -70, lat_max: -45, lon_min: -180, lon_max: 180, note: "Carbon uptake and sea-surface temperature trends." },
  { name: 'Himalayas', lat_min: 26, lat_max: 37, lon_min: 72, lon_max: 98, note: "Glacier retreat and snow cover." },
  { name: 'Tibetan Plateau', lat_min: 27, lat_max: 40, lon_min: 75, lon_max: 105, note: "Permafrost, lakes and the Asian monsoon's engine." },
  { name: 'Congo basin', lat_min: -10, lat_max: 6, lon_min: 10, lon_max: 32, note: "The second-largest tropical forest." },
  { name: 'Great Barrier Reef', lat_min: -25, lat_max: -10, lon_min: 142, lon_max: 154, note: "Coral bleaching with sea-surface temperature." },
  { name: 'Aral Sea', lat_min: 43, lat_max: 47, lon_min: 57, lon_max: 62, note: "Water extent lost to irrigation." },
  { name: 'Lake Chad', lat_min: 12, lat_max: 14.5, lon_min: 13, lon_max: 16, note: "Shrinking lake area in a drying region." },
  { name: 'Lake Victoria', lat_min: -3.5, lat_max: 1, lon_min: 31, lon_max: 35.5, note: "Africa's largest lake and its level changes." },
  { name: 'Caspian Sea', lat_min: 36, lat_max: 48, lon_min: 46, lon_max: 55, note: "Falling sea level in the largest inland water body." },
  { name: 'Mediterranean', lat_min: 30, lat_max: 46, lon_min: -6, lon_max: 37, note: "A warming hotspot and summer drought." },
  { name: 'Europe', lat_min: 35, lat_max: 71, lon_min: -11, lon_max: 40, note: "Heatwaves and land-surface temperature trends." },
  { name: 'United Kingdom', lat_min: 49.5, lat_max: 61, lon_min: -11, lon_max: 2, note: "The team's home islands." },
  { name: 'Plymouth', lat_min: 50.2, lat_max: 50.6, lon_min: -4.4, lon_max: -3.9, note: "University of Plymouth, home of the VIGIL team." },
  { name: 'Siberia', lat_min: 50, lat_max: 76, lon_min: 60, lon_max: 170, note: "Permafrost thaw, wildfire and boreal forest." },
  { name: 'Alaska', lat_min: 54, lat_max: 72, lon_min: -170, lon_max: -130, note: "Glaciers, permafrost and a lengthening fire season." },
  { name: 'Hudson Bay', lat_min: 51, lat_max: 65, lon_min: -95, lon_max: -77, note: "Seasonal sea ice and polar bear habitat." },
  { name: 'Patagonia', lat_min: -56, lat_max: -39, lon_min: -76, lon_max: -63, note: "Ice fields and the Southern Patagonian glaciers." },
  { name: 'Andes', lat_min: -40, lat_max: 10, lon_min: -80, lon_max: -62, note: "Tropical and mid-latitude glaciers." },
  { name: 'Borneo', lat_min: -4.5, lat_max: 7.5, lon_min: 108, lon_max: 119.5, note: "Peatland, oil palm and forest loss." },
  { name: 'Indonesia', lat_min: -11, lat_max: 6, lon_min: 95, lon_max: 141, note: "Fires, peat and sea-level rise across the archipelago." },
  { name: 'Australia', lat_min: -44, lat_max: -10, lon_min: 112, lon_max: 154, note: "Drought, bushfire and the Murray-Darling basin." },
  { name: 'California', lat_min: 32, lat_max: 42, lon_min: -125, lon_max: -114, note: "Drought, snowpack and wildfire." },
  { name: 'Gulf of Mexico', lat_min: 18, lat_max: 31, lon_min: -98, lon_max: -80, note: "Sea-surface temperature and hurricane intensification." },
  { name: 'Bay of Bengal', lat_min: 5, lat_max: 23, lon_min: 80, lon_max: 95, note: "Cyclones and the Ganges-Brahmaputra delta." },
  { name: 'Nile delta', lat_min: 30, lat_max: 31.7, lon_min: 29.5, lon_max: 32.5, note: "Subsidence and sea-level rise on a crowded coast." },
  { name: 'Mauna Loa', lat_min: 19.3, lat_max: 19.8, lon_min: -155.8, lon_max: -155.3, note: "The Keeling CO₂ record since 1958." },
  { name: 'Svalbard', lat_min: 76, lat_max: 81, lon_min: 9, lon_max: 34, note: "Sea-ice season length in the warming Barents region." },
  { name: 'Iceland', lat_min: 63, lat_max: 67, lon_min: -25, lon_max: -13, note: "Glacier retreat and volcanic ash." },
  { name: 'Madagascar', lat_min: -26, lat_max: -12, lon_min: 43, lon_max: 51, note: "Deforestation and drought in the south." },
  { name: 'Arabian Peninsula', lat_min: 12, lat_max: 32, lon_min: 34, lon_max: 60, note: "Dust, extreme heat and groundwater loss." },
  { name: 'Great Lakes', lat_min: 41, lat_max: 49.5, lon_min: -93, lon_max: -75, note: "Lake levels and ice cover." },
  { name: 'Mississippi basin', lat_min: 29, lat_max: 49, lon_min: -113, lon_max: -78, note: "Floods, agriculture and the Gulf dead zone." },
  { name: 'North Atlantic', lat_min: 30, lat_max: 65, lon_min: -70, lon_max: 0, note: "Ocean heat content and the Gulf Stream." },
  { name: 'Pacific Ocean', lat_min: -40, lat_max: 40, lon_min: 150, lon_max: -110, note: "El Niño and La Niña." },
  { name: 'Indian Ocean', lat_min: -40, lat_max: 25, lon_min: 40, lon_max: 110, note: "The Indian Ocean Dipole and the monsoon." },
];

/** The four the desktop app keeps as buttons, offered before anything is typed. */
export const presetPlaces = ['Amazon basin', 'Greenland', 'Sahel', 'Antarctica']
  .map((name) => places.find((place) => place.name === name))
  .filter((place): place is Place => place !== undefined);

/** Places whose names hold what was typed, those that start with it first, then by name. */
export function searchPlaces(query: string, limit = 8): Place[] {
  const typed = query.trim().toLowerCase();
  if (typed.length === 0) {
    return [];
  }
  return places
    .filter((place) => place.name.toLowerCase().includes(typed))
    .sort((a, b) => {
      const first = Number(!a.name.toLowerCase().startsWith(typed)) - Number(!b.name.toLowerCase().startsWith(typed));
      return first !== 0 ? first : a.name.localeCompare(b.name);
    })
    .slice(0, limit);
}

/** The place as a box the globe can frame. Its east edge runs on past 180 where it crosses the date line. */
export function placeBox(place: Place): { lat_min: number; lat_max: number; lon_min: number; lon_max: number } {
  const east = place.lon_max < place.lon_min ? place.lon_max + 360 : place.lon_max;
  return { lat_min: place.lat_min, lat_max: place.lat_max, lon_min: place.lon_min, lon_max: east };
}
