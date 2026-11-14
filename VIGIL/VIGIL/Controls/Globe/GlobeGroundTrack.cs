namespace VIGIL.Controls.Globe
{
    /// <summary>
    ///  A satellite's ground track: a polyline of latitude/longitude points, with an optional
    ///  animated satellite dot that loops along it.
    /// </summary>
    public sealed class GlobeGroundTrack
    {
        public GlobeGroundTrack(IReadOnlyList<(double Latitude, double Longitude)> points, Color colour)
        {
            if (points.Count < 2)
            {
                throw new ArgumentException("A ground track needs at least two points.", nameof(points));
            }
            Points = points.ToArray();
            Colour = colour;
        }

        public IReadOnlyList<(double Latitude, double Longitude)> Points { get; }
        public Color Colour { get; set; }
        public string Label { get; set; } = "";
        public bool ShowSatellite { get; set; }

        /// <summary>Seconds the satellite takes to travel the whole track.</summary>
        public double LoopSeconds { get; set; } = 90;

        /// <summary>Position along the track at a fraction in [0, 1), interpolated across the dateline.</summary>
        public (double Latitude, double Longitude) At(double fraction)
        {
            fraction -= Math.Floor(fraction);
            double position = fraction * (Points.Count - 1);
            int index = Math.Min((int)position, Points.Count - 2);
            double t = position - index;

            var (lat0, lon0) = Points[index];
            var (lat1, lon1) = Points[index + 1];
            double dLon = lon1 - lon0;
            if (dLon > 180) dLon -= 360;
            if (dLon < -180) dLon += 360;
            return (lat0 + (lat1 - lat0) * t, lon0 + dLon * t);
        }
    }
}
