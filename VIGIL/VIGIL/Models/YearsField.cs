namespace VIGIL.Models
{
    /// <summary>
    ///  The service's answer to <c>GET /years</c>: each scanned cell's mean anomaly in each
    ///  calendar year. See "A variable year by year" in <c>contract/README.md</c>. It is for
    ///  looking at how a change arrived. No test is run on it and no finding rests on it.
    /// </summary>
    public sealed class YearsField
    {
        public string ContractVersion { get; set; } = "";

        /// <summary>True when the numbers are made up.</summary>
        public bool Synthetic { get; set; }

        /// <summary>The stored scan the service holds. The values belong beside that scan and no other.</summary>
        public string? RunId { get; set; }

        public string Variable { get; set; } = "";
        public string Units { get; set; } = "";
        public List<int> Years { get; set; } = new();

        /// <summary>How many months of data a year needs to be given a value.</summary>
        public int MinMonths { get; set; }

        public int Rows { get; set; }
        public int Columns { get; set; }
        public double LatMin { get; set; }
        public double LatMax { get; set; }
        public double LonMin { get; set; }
        public double LonMax { get; set; }

        /// <summary>Where a colour scale centred on zero can end. For drawing only. Null when there are no values.</summary>
        public double? ScaleMax { get; set; }

        /// <summary><c>Values[year][row][column]</c>, rows south to north. Null where the scan skipped the cell or the year is too thin.</summary>
        public double?[][][] Values { get; set; } = Array.Empty<double?[][]>();
    }
}
