namespace VIGIL.Controls.Globe
{
    /// <summary>A named region with a lat/lon bounding box the globe can fly to.</summary>
    public sealed class Place
    {
        public Place(string name, double latitudeMin, double latitudeMax, double longitudeMin, double longitudeMax, string description)
        {
            Name = name;
            LatitudeMin = latitudeMin;
            LatitudeMax = latitudeMax;
            LongitudeMin = longitudeMin;
            LongitudeMax = longitudeMax;
            Description = description;
        }

        public string Name { get; }
        public double LatitudeMin { get; }
        public double LatitudeMax { get; }
        public double LongitudeMin { get; }
        public double LongitudeMax { get; }

        /// <summary>Why the place matters for Earth system trends, in a line.</summary>
        public string Description { get; }

        public double CentreLatitude => (LatitudeMin + LatitudeMax) / 2;

        public double CentreLongitude
        {
            get
            {
                double span = LongitudeMax - LongitudeMin;
                if (span < 0) span += 360;
                double centre = LongitudeMin + span / 2;
                return centre > 180 ? centre - 360 : centre;
            }
        }
    }

    /// <summary>
    ///  Built-in list of regions that come up in Earth science, so <c>FlyTo("Amazon")</c> works
    ///  without the caller knowing coordinates. Boxes are rough and only meant for framing.
    /// </summary>
    public static class Places
    {
        public static IReadOnlyList<Place> All { get; } = new[]
        {
            new Place("Amazon basin", -16, 5, -75, -45, "Tropical forest cover, NDVI and deforestation fronts."),
            new Place("Greenland", 59, 84, -73, -11, "Ice-sheet surface melt and mass loss."),
            new Place("Arctic Ocean", 70, 85, -180, 180, "Sea-ice extent and the fastest warming on the planet."),
            new Place("Antarctica", -85, -63, -180, 180, "Ice shelves, sea ice and the polar vortex."),
            new Place("Sahel", 10, 20, -18, 40, "Rainfall recovery and greening at the desert's edge."),
            new Place("Sahara", 17, 32, -17, 36, "Dust sources and the world's largest hot desert."),
            new Place("Southern Ocean", -70, -45, -180, 180, "Carbon uptake and sea-surface temperature trends."),
            new Place("Himalayas", 26, 37, 72, 98, "Glacier retreat and snow cover."),
            new Place("Tibetan Plateau", 27, 40, 75, 105, "Permafrost, lakes and the Asian monsoon's engine."),
            new Place("Congo basin", -10, 6, 10, 32, "The second-largest tropical forest."),
            new Place("Great Barrier Reef", -25, -10, 142, 154, "Coral bleaching with sea-surface temperature."),
            new Place("Aral Sea", 43, 47, 57, 62, "Water extent lost to irrigation."),
            new Place("Lake Chad", 12, 14.5, 13, 16, "Shrinking lake area in a drying region."),
            new Place("Lake Victoria", -3.5, 1, 31, 35.5, "Africa's largest lake and its level changes."),
            new Place("Caspian Sea", 36, 48, 46, 55, "Falling sea level in the largest inland water body."),
            new Place("Mediterranean", 30, 46, -6, 37, "A warming hotspot and summer drought."),
            new Place("Europe", 35, 71, -11, 40, "Heatwaves and land-surface temperature trends."),
            new Place("United Kingdom", 49.5, 61, -11, 2, "The team's home islands."),
            new Place("Plymouth", 50.2, 50.6, -4.4, -3.9, "University of Plymouth, home of the VIGIL team."),
            new Place("Siberia", 50, 76, 60, 170, "Permafrost thaw, wildfire and boreal forest."),
            new Place("Alaska", 54, 72, -170, -130, "Glaciers, permafrost and a lengthening fire season."),
            new Place("Hudson Bay", 51, 65, -95, -77, "Seasonal sea ice and polar bear habitat."),
            new Place("Patagonia", -56, -39, -76, -63, "Ice fields and the Southern Patagonian glaciers."),
            new Place("Andes", -40, 10, -80, -62, "Tropical and mid-latitude glaciers."),
            new Place("Borneo", -4.5, 7.5, 108, 119.5, "Peatland, oil palm and forest loss."),
            new Place("Indonesia", -11, 6, 95, 141, "Fires, peat and sea-level rise across the archipelago."),
            new Place("Australia", -44, -10, 112, 154, "Drought, bushfire and the Murray-Darling basin."),
            new Place("California", 32, 42, -125, -114, "Drought, snowpack and wildfire."),
            new Place("Gulf of Mexico", 18, 31, -98, -80, "Sea-surface temperature and hurricane intensification."),
            new Place("Bay of Bengal", 5, 23, 80, 95, "Cyclones and the Ganges-Brahmaputra delta."),
            new Place("Nile delta", 30, 31.7, 29.5, 32.5, "Subsidence and sea-level rise on a crowded coast."),
            new Place("Mauna Loa", 19.3, 19.8, -155.8, -155.3, "The Keeling CO₂ record since 1958."),
            new Place("Svalbard", 76, 81, 9, 34, "Sea-ice season length in the warming Barents region."),
            new Place("Iceland", 63, 67, -25, -13, "Glacier retreat and volcanic ash."),
            new Place("Madagascar", -26, -12, 43, 51, "Deforestation and drought in the south."),
            new Place("Arabian Peninsula", 12, 32, 34, 60, "Dust, extreme heat and groundwater loss."),
            new Place("Great Lakes", 41, 49.5, -93, -75, "Lake levels and ice cover."),
            new Place("Mississippi basin", 29, 49, -113, -78, "Floods, agriculture and the Gulf dead zone."),
            new Place("North Atlantic", 30, 65, -70, 0, "Ocean heat content and the Gulf Stream."),
            new Place("Pacific Ocean", -40, 40, 150, -110, "El Niño and La Niña."),
            new Place("Indian Ocean", -40, 25, 40, 110, "The Indian Ocean Dipole and the monsoon.")
        };

        /// <summary>
        ///  Best match for a name: exact first, then a name that starts with the query, then
        ///  one that contains it. Case doesn't matter. Null when nothing matches.
        /// </summary>
        public static Place? Find(string query)
        {
            string q = (query ?? "").Trim();
            if (q.Length == 0)
            {
                return null;
            }
            return All.FirstOrDefault(p => p.Name.Equals(q, StringComparison.OrdinalIgnoreCase))
                ?? All.FirstOrDefault(p => p.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase))
                ?? All.FirstOrDefault(p => p.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Every place whose name contains the query, best matches first.</summary>
        public static IEnumerable<Place> Search(string query, int limit = 8)
        {
            string q = (query ?? "").Trim();
            if (q.Length == 0)
            {
                return Array.Empty<Place>();
            }
            return All
                .Where(p => p.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(p => p.Name)
                .Take(limit);
        }
    }
}
