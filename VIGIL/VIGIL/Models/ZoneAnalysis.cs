namespace VIGIL.Models
{
    /// <summary>
    ///  The service's answer to <c>POST /zone</c>: for one drawn zone, a series per variable
    ///  and the result of the scan's trend test on it. See "A zone" in <c>contract/README.md</c>.
    ///  A zone result is not a finding: it has no cell, no rank and no score, and it is never
    ///  attached to a question for the Detective.
    /// </summary>
    public sealed class ZoneAnalysis
    {
        public string ContractVersion { get; set; } = "";

        /// <summary>True when the numbers are made up.</summary>
        public bool Synthetic { get; set; }

        /// <summary>The stored scan the service holds. The figures belong beside that scan and no other.</summary>
        public string? RunId { get; set; }

        public ZoneExtent Zone { get; set; } = new();
        public Period Period { get; set; } = new();
        public List<Variable> Variables { get; set; } = new();

        /// <summary>One entry per variable in the scan, tested or not.</summary>
        public List<ZoneSummary> Summaries { get; set; } = new();
    }

    /// <summary>What the service took the zone to be: the grid cells whose centres the outline holds.</summary>
    public sealed class ZoneExtent
    {
        public List<string> Cells { get; set; } = new();
        public int CellCount { get; set; }

        /// <summary>The area of those cells.</summary>
        public double AreaKm2 { get; set; }

        public double LatMin { get; set; }
        public double LatMax { get; set; }
    }

    /// <summary>One variable over the zone. When <see cref="Tested"/> is false, <see cref="SkipReason"/> says why.</summary>
    public sealed class ZoneSummary
    {
        public string Variable { get; set; } = "";
        public string Units { get; set; } = "";

        /// <summary>How many of the zone's cells went into the series.</summary>
        public int CellsUsed { get; set; }

        /// <summary>The zone's cells left out, by the scan's reason: ocean, ice or coverage.</summary>
        public Dictionary<string, int> CellsSkipped { get; set; } = new();

        public bool Tested { get; set; }

        /// <summary>"no_cells" when none of the zone's cells were scanned for this variable, "coverage" when the series is too thin to test.</summary>
        public string? SkipReason { get; set; }

        /// <summary>
        ///  Whether the p-value is below the scan's threshold. One test is made per variable, so
        ///  nothing is corrected, and the zone was chosen by eye: the limitations say both.
        /// </summary>
        public bool Significant { get; set; }

        /// <summary>Null when the variable was not tested, or was tested and has no trend to report.</summary>
        public TrendResult? Result { get; set; }

        public Preprocessing? Preprocessing { get; set; }
        public Method? Method { get; set; }
        public string Summary { get; set; } = "";

        /// <summary>
        ///  A few words from the service for beside the number, when the result should not be taken
        ///  at face value: "Snow is rare here". Null when there is none. The first limitation says why.
        /// </summary>
        public string? Caution { get; set; }

        public List<string> Limitations { get; set; } = new();
        public ZoneSeries? Series { get; set; }
    }

    /// <summary>The zone's one series for a variable, as parallel arrays of the same length.</summary>
    public sealed class ZoneSeries
    {
        public string Units { get; set; } = "";
        public List<DateOnly> Dates { get; set; } = new();
        public List<double?> Raw { get; set; } = new();
        public List<double?> Anomaly { get; set; } = new();
    }
}
