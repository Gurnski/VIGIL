using System.Globalization;
using VIGIL.Theming;

namespace VIGIL.Controls.Globe
{
    /// <summary>
    ///  What the app says about a satellite when it is clicked. Carried in the satellite's
    ///  <see cref="GlobeSatellite.Tag"/>.
    /// </summary>
    public sealed record MissionInfo(string Operator, string Launched, string Instrument, string Role, bool InScan, string PictureResource)
    {
        /// <summary>True when a second product the scan's trends were re-tested on comes from this satellite.</summary>
        public bool Retests { get; init; }

        /// <summary>Where the satellite stands with the results on screen, in a line.</summary>
        public string Standing => InScan ? "Its data is in the scan"
            : Retests ? "Not in the scan: its record re-tests one the scan uses"
            : "Not in the scan: behind a source kept for cross-checks";
    }

    /// <summary>
    ///  The satellites behind VIGIL's data, for drawing on the globe. The web viewer has the
    ///  same list in <c>web/src/globe/satellite.ts</c>: keep the two in step.
    ///
    ///  What is real and what is not: each orbit's height, tilt and period are the mission's
    ///  own, and a sun-synchronous orbit's plane sits where it really is today. Where each
    ///  satellite is along its orbit is not tracked from live orbit data, so the dot shows how
    ///  it moves, not where it is right now. Say so wherever the satellites are explained.
    ///
    ///  MERRA-2 and GLDAS are models, not single spacecraft, so they have no orbit to draw.
    ///  The pictures are public domain NASA images: see <c>assets/satellites/README.md</c>.
    /// </summary>
    public static class MissionSatellites
    {
        /// <summary>The line to show wherever the satellites are explained.</summary>
        public const string Caveat = "Each orbit is the mission's real one. Places along the orbits are for illustration, not tracked live.";

        private static readonly CultureInfo British = CultureInfo.GetCultureInfo("en-GB");
        private static readonly Dictionary<string, Image?> pictures = new();

        /// <summary>
        ///  A new set of satellites for the results on screen. A satellite is in the scan when one
        ///  of the scan's variables names it as its mission, and it re-tests when a second product
        ///  does. So the same list is right for the fixture, for a scan of four variables and for
        ///  one of eight. Those in the scan take the stronger colour and the rest the quieter one.
        ///  With no results given, each keeps the standing written beside it below.
        /// </summary>
        public static IReadOnlyList<GlobeSatellite> Create(IReadOnlyCollection<string>? scanMissions = null, IReadOnlyCollection<string>? retestMissions = null)
        {
            return All().Select(satellite =>
            {
                var info = (MissionInfo)satellite.Tag!;
                bool Named(IReadOnlyCollection<string>? missions) =>
                    missions != null && missions.Any(mission => mission.Contains(satellite.Label.Split(' ')[0], StringComparison.OrdinalIgnoreCase));
                var standing = scanMissions == null ? info : info with { InScan = Named(scanMissions), Retests = !Named(scanMissions) && Named(retestMissions) };
                satellite.Tag = standing;
                satellite.Colour = standing.InScan ? Theme.Positive : Theme.TextSecondary;
                return satellite;
            }).ToList();
        }

        private static GlobeSatellite[] All()
        {
            return new[]
            {
                // Terra crosses the equator southbound at 10:30, so northbound at 22:30.
                Build("Terra", 98.2, 705, nodeLocalTime: 22.5, info: new MissionInfo(
                    "NASA", "18 December 1999", "MODIS, with ASTER, CERES, MISR and MOPITT",
                    "Carries MODIS, which VIGIL reads for vegetation (NDVI) and for snow cover.", true, "terra.png")),
                // Not sun-synchronous: its plane drifts round the Earth, so this starting angle is arbitrary.
                Build("GPM Core Observatory", 65, 407, nodeDegrees: 40, phase: 120, info: new MissionInfo(
                    "NASA and JAXA", "27 February 2014", "GMI microwave imager and DPR radar",
                    "The reference satellite of GPM IMERG, the source of the precipitation record.", true, "gpm.png")),

                Build("Aqua", 98.2, 705, nodeLocalTime: 13.5, phase: 200, info: new MissionInfo(
                    "NASA", "4 May 2002", "MODIS, with AIRS, AMSU and CERES",
                    "Carries a second MODIS, which VIGIL reads for sea surface temperature and chlorophyll, and for a second vegetation record to test Terra's against.", false, "aqua.png")),
                // Behind the sources the brief keeps for cross-checks.
                Build("Suomi NPP", 98.7, 824, nodeLocalTime: 13.5, phase: 75, info: new MissionInfo(
                    "NASA and NOAA", "28 October 2011", "VIIRS, with CrIS, ATMS, OMPS and CERES",
                    "Carries VIIRS, which continues the MODIS vegetation record.", false, "suomi-npp.jpg")),
                Build("SMAP", 98.1, 685, nodeLocalTime: 18, phase: 290, info: new MissionInfo(
                    "NASA", "31 January 2015", "L-band radiometer",
                    "Measures soil moisture from orbit, for comparing against the GLDAS model.", false, "smap.jpg")),
                // Two spacecraft about 220 km apart on one orbit, drawn as one. The plane drifts, so the angle is arbitrary.
                Build("GRACE-FO", 89, 490, nodeDegrees: 160, phase: 30, info: new MissionInfo(
                    "NASA and GFZ", "22 May 2018", "Microwave and laser ranging between the two spacecraft",
                    "A pair of satellites that weigh the water stored in a region through its pull on them.", false, "grace-fo.jpg"))
            };
        }

        /// <summary>The orbit in words: "705 km up, tilted 98.2°, one lap in about 99 minutes".</summary>
        public static string OrbitText(GlobeSatellite satellite)
        {
            return string.Format(British, "{0:0} km up, tilted {1:0.#}°, one lap in about {2:0} minutes",
                satellite.AltitudeKm, satellite.InclinationDegrees, satellite.Period.TotalMinutes);
        }

        /// <summary>The satellite's picture from the embedded assets, or null if it is missing. Kept for the life of the app.</summary>
        public static Image? Picture(MissionInfo info)
        {
            if (!pictures.TryGetValue(info.PictureResource, out var picture))
            {
                using var stream = typeof(MissionSatellites).Assembly.GetManifestResourceStream("VIGIL.Assets.Satellites." + info.PictureResource);
                if (stream != null)
                {
                    // Copied, because an image read from a stream needs that stream for as long as it lives.
                    using var loaded = Image.FromStream(stream);
                    picture = new Bitmap(loaded);
                }
                pictures[info.PictureResource] = picture;
            }
            return picture;
        }

        private static GlobeSatellite Build(string label, double inclination, double altitudeKm, MissionInfo info,
            double? nodeLocalTime = null, double nodeDegrees = 0, double phase = 0)
        {
            return new GlobeSatellite(label, inclination, altitudeKm, info.InScan ? Theme.Positive : Theme.TextSecondary)
            {
                Detail = info.Role,
                AscendingNodeLocalTime = nodeLocalTime,
                AscendingNodeDegrees = nodeDegrees,
                PhaseAtEpochDegrees = phase,
                Tag = info
            };
        }
    }
}
