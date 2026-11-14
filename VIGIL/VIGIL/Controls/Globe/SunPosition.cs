namespace VIGIL.Controls.Globe
{
    /// <summary>
    ///  Where the sun is directly overhead at a given moment, so the globe's day/night
    ///  terminator matches the real one. Uses the US Naval Observatory's approximate solar
    ///  coordinates (good to a fraction of a degree, which is plenty for lighting).
    /// </summary>
    public static class SunPosition
    {
        private static readonly DateTime J2000 = new(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        /// <summary>Latitude and longitude in degrees of the subsolar point at <paramref name="utc"/>.</summary>
        public static (double Latitude, double Longitude) SubsolarPoint(DateTime utc)
        {
            var (rightAscension, declination) = Equatorial(utc);

            // The sun is overhead where the local sidereal time equals its right ascension.
            double longitude = rightAscension - GreenwichSiderealDegrees(utc);
            longitude = ((longitude % 360) + 540) % 360 - 180;
            return (declination, longitude);
        }

        /// <summary>The sun's right ascension and declination in degrees at <paramref name="utc"/>.</summary>
        public static (double RightAscension, double Declination) Equatorial(DateTime utc)
        {
            double days = DaysSinceJ2000(utc);

            double meanAnomaly = ToRadians(357.529 + 0.98560028 * days);
            double meanLongitude = 280.459 + 0.98564736 * days;
            double eclipticLongitude = ToRadians(meanLongitude + 1.915 * Math.Sin(meanAnomaly) + 0.020 * Math.Sin(2 * meanAnomaly));
            double obliquity = ToRadians(23.439 - 0.00000036 * days);

            double rightAscension = Math.Atan2(Math.Cos(obliquity) * Math.Sin(eclipticLongitude), Math.Cos(eclipticLongitude));
            double declination = Math.Asin(Math.Sin(obliquity) * Math.Sin(eclipticLongitude));
            return (rightAscension * 180 / Math.PI, declination * 180 / Math.PI);
        }

        /// <summary>
        ///  Greenwich mean sidereal time in degrees: how far the Earth has turned against the
        ///  stars, which turns sky (inertial) longitudes into map longitudes.
        /// </summary>
        public static double GreenwichSiderealDegrees(DateTime utc)
        {
            double degrees = (280.46061837 + 360.98564736629 * DaysSinceJ2000(utc)) % 360;
            return degrees < 0 ? degrees + 360 : degrees;
        }

        private static double DaysSinceJ2000(DateTime utc) => (utc.ToUniversalTime() - J2000).TotalDays;

        private static double ToRadians(double degrees) => degrees * Math.PI / 180;
    }
}
