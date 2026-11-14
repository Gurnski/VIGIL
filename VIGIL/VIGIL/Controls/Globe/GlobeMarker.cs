namespace VIGIL.Controls.Globe
{
    /// <summary>
    ///  A labelled point on the globe, such as a station or a data site. Drawn as a glowing dot
    ///  that fades out as it turns away over the horizon.
    /// </summary>
    public sealed class GlobeMarker
    {
        public GlobeMarker(double latitude, double longitude, string label, Color colour)
        {
            Latitude = latitude;
            Longitude = longitude;
            Label = label;
            Colour = colour;
        }

        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string Label { get; set; }

        /// <summary>Second line of the hover tooltip, for example the variable and its trend.</summary>
        public string Detail { get; set; } = "";

        public Color Colour { get; set; }

        /// <summary>Anything the app wants to carry with the marker, handed back on click.</summary>
        public object? Tag { get; set; }
    }
}
