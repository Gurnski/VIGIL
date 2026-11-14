using VIGIL.Theming;

namespace VIGIL.Controls.Globe
{
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
    /// </summary>
    public static class MissionSatellites
    {
        /// <summary>The line to show wherever the satellites are explained.</summary>
        public const string Caveat = "Each orbit is the mission's real one. Places along the orbits are for illustration, not tracked live.";

        /// <summary>
        ///  A new set of satellites. Those whose data is in the first scan take the stronger
        ///  colour; those behind sources kept for cross-checks take the quieter one.
        /// </summary>
        public static IReadOnlyList<GlobeSatellite> Create()
        {
            return new[]
            {
                // In the first scan.
                // Terra crosses the equator southbound at 10:30, so northbound at 22:30.
                InScan("Terra", "Carries MODIS, the source of the vegetation (NDVI) record.", 98.2, 705, nodeLocalTime: 22.5),
                // Not sun-synchronous: its plane drifts round the Earth, so this starting angle is arbitrary.
                InScan("GPM Core Observatory", "The reference satellite of GPM IMERG, the source of the precipitation record.", 65, 407, nodeDegrees: 40, phase: 120),

                // Behind the sources the brief keeps for cross-checks. Not in the scan.
                CrossCheck("Aqua", "Carries a second MODIS, for checking a vegetation trend against Terra's.", 98.2, 705, nodeLocalTime: 13.5, phase: 200),
                CrossCheck("Suomi NPP", "Carries VIIRS, which continues the MODIS vegetation record.", 98.7, 824, nodeLocalTime: 13.5, phase: 75),
                CrossCheck("SMAP", "Measures soil moisture from orbit, for comparing against the GLDAS model.", 98.1, 685, nodeLocalTime: 18, phase: 290),
                // Two spacecraft about 220 km apart on one orbit, drawn as one. The plane drifts, so the angle is arbitrary.
                CrossCheck("GRACE-FO", "A pair of satellites that weigh the water stored in a region through its pull on them.", 89, 490, nodeDegrees: 160, phase: 30)
            };
        }

        private static GlobeSatellite InScan(string label, string detail, double inclination, double altitudeKm,
            double? nodeLocalTime = null, double nodeDegrees = 0, double phase = 0)
        {
            return Build(label, detail + " In the first scan.", inclination, altitudeKm, Theme.Positive, nodeLocalTime, nodeDegrees, phase);
        }

        private static GlobeSatellite CrossCheck(string label, string detail, double inclination, double altitudeKm,
            double? nodeLocalTime = null, double nodeDegrees = 0, double phase = 0)
        {
            return Build(label, detail + " Not in the scan.", inclination, altitudeKm, Theme.TextSecondary, nodeLocalTime, nodeDegrees, phase);
        }

        private static GlobeSatellite Build(string label, string detail, double inclination, double altitudeKm, Color colour,
            double? nodeLocalTime, double nodeDegrees, double phase)
        {
            return new GlobeSatellite(label, inclination, altitudeKm, colour)
            {
                Detail = detail,
                AscendingNodeLocalTime = nodeLocalTime,
                AscendingNodeDegrees = nodeDegrees,
                PhaseAtEpochDegrees = phase
            };
        }
    }
}
