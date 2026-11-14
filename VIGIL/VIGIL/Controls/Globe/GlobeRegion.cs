namespace VIGIL.Controls.Globe
{
    /// <summary>
    ///  A latitude/longitude bounding box drawn on the globe as an outline with a translucent
    ///  fill. The fill is shaded per pixel so it hugs the sphere; the outline is a curved polyline.
    ///  Longitudes may cross the dateline (for example 150 to -150).
    /// </summary>
    public sealed class GlobeRegion
    {
        public GlobeRegion(double latitudeMin, double latitudeMax, double longitudeMin, double longitudeMax, Color colour)
        {
            LatitudeMin = Math.Min(latitudeMin, latitudeMax);
            LatitudeMax = Math.Max(latitudeMin, latitudeMax);
            LongitudeMin = longitudeMin;
            LongitudeMax = longitudeMax;
            Colour = colour;
        }

        public double LatitudeMin { get; }
        public double LatitudeMax { get; }
        public double LongitudeMin { get; }
        public double LongitudeMax { get; }
        public Color Colour { get; set; }
        public string Label { get; set; } = "";

        /// <summary>Opacity of the fill inside the box, 0 to 1.</summary>
        public float FillOpacity { get; set; } = 0.2f;

        public object? Tag { get; set; }

        /// <summary>Width of the box in degrees, going east from LongitudeMin (0 to 360).</summary>
        public double LongitudeSpan
        {
            get
            {
                double span = LongitudeMax - LongitudeMin;
                if (span <= 0)
                {
                    span += 360;
                }
                return Math.Min(span, 360);
            }
        }
    }
}
