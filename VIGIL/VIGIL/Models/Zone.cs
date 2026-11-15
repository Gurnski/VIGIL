namespace VIGIL.Models
{
    /// <summary>
    ///  A point on a zone's outline. Longitude carries on past ±180 where the outline crosses
    ///  the date line, so two points next to each other in the list are next to each other on the map.
    /// </summary>
    public sealed class ZonePoint
    {
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }

    /// <summary>
    ///  An area the user drew on the globe and saved. The outline is closed by a straight line
    ///  from its last point back to its first. A zone belongs to the app, not to a scan: which
    ///  grid cells it takes in is worked out against whichever results are on screen.
    /// </summary>
    public sealed class Zone
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public DateTime Created { get; set; } = DateTime.UtcNow;
        public List<ZonePoint> Points { get; set; } = new();

        /// <summary>The outline as latitude and longitude pairs, the form the geometry and the globe take.</summary>
        public List<(double Latitude, double Longitude)> Outline() =>
            Points.Select(point => (point.Latitude, point.Longitude)).ToList();
    }
}
