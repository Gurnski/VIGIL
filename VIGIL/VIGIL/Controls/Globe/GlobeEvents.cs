namespace VIGIL.Controls.Globe
{
    public sealed class MarkerEventArgs : EventArgs
    {
        public MarkerEventArgs(GlobeMarker marker)
        {
            Marker = marker;
        }

        public GlobeMarker Marker { get; }
    }

    public sealed class SatelliteEventArgs : EventArgs
    {
        public SatelliteEventArgs(GlobeSatellite satellite)
        {
            Satellite = satellite;
        }

        public GlobeSatellite Satellite { get; }
    }

    /// <summary>Raised when the planet's surface is clicked away from any marker.</summary>
    public sealed class SurfaceClickEventArgs : EventArgs
    {
        public SurfaceClickEventArgs(double latitude, double longitude)
        {
            Latitude = latitude;
            Longitude = longitude;
        }

        public double Latitude { get; }
        public double Longitude { get; }
    }

    /// <summary>Raised when a Shift+drag selection box is completed.</summary>
    public sealed class RegionEventArgs : EventArgs
    {
        public RegionEventArgs(GlobeRegion region)
        {
            Region = region;
        }

        public GlobeRegion Region { get; }
    }

    /// <summary>
    ///  Raised when the pen is lifted in pen mode. <see cref="Outline"/> is the drawn path,
    ///  thinned, with continuous longitudes; it closes from its last point to its first.
    /// </summary>
    public sealed class ZoneDrawnEventArgs : EventArgs
    {
        public ZoneDrawnEventArgs(IReadOnlyList<(double Latitude, double Longitude)> outline)
        {
            Outline = outline;
        }

        public IReadOnlyList<(double Latitude, double Longitude)> Outline { get; }
    }

    /// <summary>Raised when the surface inside a zone is clicked.</summary>
    public sealed class ZoneEventArgs : EventArgs
    {
        public ZoneEventArgs(GlobeZone zone)
        {
            Zone = zone;
        }

        public GlobeZone Zone { get; }
    }

    /// <summary>Raised when a <see cref="GlobeControl.FlyTo(double, double, double?, TimeSpan?)"/> flight lands.</summary>
    public sealed class FlightEventArgs : EventArgs
    {
        public FlightEventArgs(double latitude, double longitude, double zoom)
        {
            Latitude = latitude;
            Longitude = longitude;
            Zoom = zoom;
        }

        public double Latitude { get; }
        public double Longitude { get; }
        public double Zoom { get; }
    }
}
