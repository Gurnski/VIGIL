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
