namespace VIGIL.Models
{
    /// <summary>
    ///  The service's answer to <c>GET /health</c>: that it is up, which scan it holds and what
    ///  it can do with it.
    /// </summary>
    public sealed class ServiceHealth
    {
        public string Status { get; set; } = "";

        /// <summary>The run the service holds.</summary>
        public string? RunId { get; set; }

        /// <summary>True when the service holds the made-up fixture and not a scan.</summary>
        public bool Synthetic { get; set; }

        /// <summary>Whether it has the gridded data, which zones, Recompute and the years need.</summary>
        public bool ScanData { get; set; }

        /// <summary>Whether it has a key for the Detective.</summary>
        public bool Detective { get; set; }
    }
}
