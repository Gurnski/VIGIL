namespace VIGIL.Models
{
    /// <summary>The record of one scan: when it ran, what was fixed beforehand and what it did.</summary>
    public sealed class RunRecord
    {
        public string Id { get; set; } = "";
        public DateTimeOffset Started { get; set; }
        public DateTimeOffset Finished { get; set; }
        public double DurationSeconds { get; set; }

        /// <summary>The git commit the run was made from.</summary>
        public string CodeVersion { get; set; } = "";

        public RunSettings Settings { get; set; } = new();
        public RunCounts Counts { get; set; } = new();
    }

    /// <summary>Everything fixed before the scan.</summary>
    public sealed class RunSettings
    {
        public GridSettings Grid { get; set; } = new();

        /// <summary>Present when the run covered part of the grid, as POST /analyse does. The counts then relate to it.</summary>
        public GridRegion? Region { get; set; }

        public Period Period { get; set; } = new();

        /// <summary>Variable ids.</summary>
        public List<string> Variables { get; set; } = new();

        public double MinCoverage { get; set; }

        /// <summary>The level an adjusted p-value must be below to count as significant.</summary>
        public double Threshold { get; set; }

        public string MultipleTesting { get; set; } = "";
        public double CiLevel { get; set; }
        public int? MaxLagMonths { get; set; }
        public Dictionary<string, double> ScoreWeights { get; set; } = new();
        public RankCap? RankCap { get; set; }
    }

    /// <summary>The scan grid. Rows count from the south and columns from the west, both from zero.</summary>
    public sealed class GridSettings
    {
        public double CellDegrees { get; set; }
        public int Rows { get; set; }
        public int Columns { get; set; }
        public double LatMin { get; set; }
        public double LonMin { get; set; }
    }

    /// <summary>A block of cells on the scan grid. Row and Column are its south-west cell, counted on the full grid.</summary>
    public sealed class GridRegion
    {
        public int Row { get; set; }
        public int Column { get; set; }
        public int Rows { get; set; }
        public int Columns { get; set; }
    }

    /// <summary>Stops one variable filling the top of the list. See "rank" in the contract.</summary>
    public sealed class RankCap
    {
        public int TopN { get; set; }
        public double MaxShare { get; set; }
    }

    /// <summary>What the scan did. A test is one variable in one cell.</summary>
    public sealed class RunCounts
    {
        public int CellsTotal { get; set; }
        public int CellsScanned { get; set; }
        public int TestsTotal { get; set; }
        public int TestsRun { get; set; }
        public int TestsSignificant { get; set; }
        public int TestsNotSignificant { get; set; }
        public int TestsSkipped { get; set; }
        public Dictionary<string, int> SkippedByReason { get; set; } = new();
        public List<VariableCounts> ByVariable { get; set; } = new();
        public int FindingsTotal { get; set; }
        public Dictionary<string, int> FindingsByKind { get; set; } = new();
    }

    public sealed class VariableCounts
    {
        public string Variable { get; set; } = "";
        public int TestsRun { get; set; }
        public int TestsSignificant { get; set; }
        public int TestsNotSignificant { get; set; }
        public int TestsSkipped { get; set; }
    }
}
