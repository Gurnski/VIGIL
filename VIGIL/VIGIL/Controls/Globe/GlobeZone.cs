namespace VIGIL.Controls.Globe
{
    /// <summary>
    ///  A hand-drawn area on the globe: a closed outline of any shape, drawn as a curved line
    ///  on the surface with no fill, so the layer under it stays readable. Its longitudes are
    ///  continuous, running past ±180 where the outline crosses the date line.
    /// </summary>
    public sealed class GlobeZone
    {
        public GlobeZone(IReadOnlyList<(double Latitude, double Longitude)> outline, Color colour)
        {
            Outline = outline;
            Colour = colour;
        }

        /// <summary>The outline, closed from its last point back to its first.</summary>
        public IReadOnlyList<(double Latitude, double Longitude)> Outline { get; }

        public Color Colour { get; set; }
        public string Label { get; set; } = "";

        /// <summary>Drawn with a heavier line, and with its <see cref="CellEdges"/>.</summary>
        public bool Emphasised { get; set; }

        /// <summary>
        ///  The stepped edge of the grid cells the zone takes in, drawn as a fainter line when
        ///  the zone is emphasised. It shows the area the numbers really cover, which is never
        ///  quite the outline that was drawn.
        /// </summary>
        public IReadOnlyList<((double Latitude, double Longitude) From, (double Latitude, double Longitude) To)> CellEdges { get; set; }
            = Array.Empty<((double, double), (double, double))>();

        public object? Tag { get; set; }
    }
}
