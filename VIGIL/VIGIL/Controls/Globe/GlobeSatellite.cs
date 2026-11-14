using System.Numerics;

namespace VIGIL.Controls.Globe
{
    /// <summary>
    ///  A satellite on a circular orbit, placed against the real clock: the orbit is fixed
    ///  against the stars and the Earth turns underneath it. The globe draws the orbit as a
    ///  ring at the satellite's altitude, the satellite on that ring, and a fading trail of
    ///  ground track behind it.
    /// </summary>
    public sealed class GlobeSatellite
    {
        private const double EarthMu = 398600.4418;   // km³/s²

        public GlobeSatellite(string label, double inclinationDegrees, double altitudeKm, Color colour)
        {
            if (altitudeKm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(altitudeKm), "A satellite needs a positive altitude.");
            }
            Label = label;
            InclinationDegrees = inclinationDegrees;
            AltitudeKm = altitudeKm;
            Colour = colour;
        }

        public string Label { get; set; }
        public Color Colour { get; set; }
        public string Detail { get; set; } = "";
        public double InclinationDegrees { get; }
        public double AltitudeKm { get; }

        /// <summary>Right ascension of the ascending node in degrees, used when <see cref="AscendingNodeLocalTime"/> is null.</summary>
        public double AscendingNodeDegrees { get; set; }

        /// <summary>
        ///  Local solar time, in hours, at which a sun-synchronous orbit crosses the equator
        ///  northbound. Terra's is 22:30 (it crosses southbound at 10:30). When set, the
        ///  orbit plane follows the sun, so it sits where it really would at this time of year.
        /// </summary>
        public double? AscendingNodeLocalTime { get; set; }

        /// <summary>Degrees along the orbit from the ascending node at <see cref="EpochUtc"/>.</summary>
        public double PhaseAtEpochDegrees { get; set; }

        public DateTime EpochUtc { get; set; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        ///  Speeds up motion along the orbit only. The orbit plane, the Earth's rotation and the
        ///  sun stay on the real clock. 1 is real time, where a low orbit barely moves on screen.
        /// </summary>
        public double TimeScale { get; set; } = 1;

        /// <summary>Draws the last half orbit of ground track under the satellite: where its instruments have just looked.</summary>
        public bool ShowGroundTrack { get; set; }

        /// <summary>Distance from the Earth's centre in Earth radii.</summary>
        public float OrbitRadius => (float)(1 + AltitudeKm / GlobeCamera.EarthRadiusKm);

        /// <summary>Time for one orbit, from Kepler's third law.</summary>
        public TimeSpan Period
        {
            get
            {
                double a = GlobeCamera.EarthRadiusKm + AltitudeKm;
                return TimeSpan.FromSeconds(2 * Math.PI * Math.Sqrt(a * a * a / EarthMu));
            }
        }

        /// <summary>Radians along the orbit from the ascending node at <paramref name="utc"/>.</summary>
        public double PhaseAt(DateTime utc)
        {
            double orbits = (utc - EpochUtc).TotalSeconds * TimeScale / Period.TotalSeconds;
            return PhaseAtEpochDegrees * Math.PI / 180 + 2 * Math.PI * (orbits - Math.Floor(orbits));
        }

        /// <summary>
        ///  World-space position at <paramref name="phase"/> radians along the orbit, with the
        ///  Earth turned as it is at <paramref name="utc"/>. Length is <see cref="OrbitRadius"/>.
        /// </summary>
        public Vector3 PositionAt(DateTime utc, double phase)
        {
            double node = NodeDegrees(utc) * Math.PI / 180;
            double inclination = InclinationDegrees * Math.PI / 180;
            double cosPhase = Math.Cos(phase), sinPhase = Math.Sin(phase);

            // Equatorial frame: x towards the vernal equinox, z to the north pole.
            double x = Math.Cos(node) * cosPhase - Math.Sin(node) * sinPhase * Math.Cos(inclination);
            double y = Math.Sin(node) * cosPhase + Math.Cos(node) * sinPhase * Math.Cos(inclination);
            double z = sinPhase * Math.Sin(inclination);

            // Turn with the Earth, then map to the globe's axes (y up, longitude 0 on +x, east on −z).
            double sidereal = SunPosition.GreenwichSiderealDegrees(utc) * Math.PI / 180;
            double xe = x * Math.Cos(sidereal) + y * Math.Sin(sidereal);
            double ye = -x * Math.Sin(sidereal) + y * Math.Cos(sidereal);
            return new Vector3((float)xe, (float)z, (float)-ye) * OrbitRadius;
        }

        /// <summary>Where the satellite is at <paramref name="utc"/>.</summary>
        public Vector3 PositionAt(DateTime utc) => PositionAt(utc, PhaseAt(utc));

        private double NodeDegrees(DateTime utc)
        {
            if (AscendingNodeLocalTime is not double localTime)
            {
                return AscendingNodeDegrees;
            }
            // Local solar time is 12:00 under the sun, and each hour is 15° of right ascension.
            return SunPosition.Equatorial(utc).RightAscension + (localTime - 12) * 15;
        }
    }
}
