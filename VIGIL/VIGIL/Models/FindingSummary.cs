namespace VIGIL.Models
{
    /// <summary>
    ///  The few fields of a finding that the map and the chat need: enough to place it,
    ///  name it and ask about it. The full finding is defined in <c>contract/README.md</c>.
    /// </summary>
    public sealed class FindingSummary
    {
        public string Id { get; set; } = "";

        /// <summary>"trend", "change_point", "cascade" or "contradiction".</summary>
        public string Kind { get; set; } = "";

        public int Rank { get; set; }

        /// <summary>One line written by the service.</summary>
        public string Summary { get; set; } = "";

        public bool Significant { get; set; }
        public string CellId { get; set; } = "";
        public double LatMin { get; set; }
        public double LatMax { get; set; }
        public double LonMin { get; set; }
        public double LonMax { get; set; }

        public double Latitude => (LatMin + LatMax) / 2;
        public double Longitude => (LonMin + LonMax) / 2;

        /// <summary>The kind as words, for labels: "Change point", "Trend".</summary>
        public string KindLabel => Kind switch
        {
            "trend" => "Trend",
            "change_point" => "Change point",
            "cascade" => "Cascade",
            "contradiction" => "Contradiction",
            _ => Kind
        };
    }
}
