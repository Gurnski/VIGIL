namespace VIGIL.Models
{
    /// <summary>
    ///  A results document from the result contract (see <c>contract/README.md</c>). Every call
    ///  that returns results sends one of these holding only the parts that call is for, so any
    ///  part may be missing: a missing list is empty and a missing run is null.
    /// </summary>
    public sealed class ResultsDocument
    {
        public string ContractVersion { get; set; } = "";

        /// <summary>True when the numbers are made up, as in the fixture.</summary>
        public bool Synthetic { get; set; }

        public RunRecord? Run { get; set; }
        public List<Variable> Variables { get; set; } = new();

        /// <summary>Findings in rank order.</summary>
        public List<Finding> Findings { get; set; } = new();

        public List<Series> Series { get; set; } = new();

        /// <summary>One globe layer per variable.</summary>
        public List<GridLayer> Grids { get; set; } = new();
    }

    /// <summary>A scanned variable and the NASA product it comes from.</summary>
    public sealed class Variable
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Units { get; set; } = "";
        public string Mission { get; set; } = "";
        public string Product { get; set; } = "";
        public string? Field { get; set; }
        public string SourceUrl { get; set; } = "";
        public string? NativeResolution { get; set; }
    }

    /// <summary>One cell of the scan grid, carrying its own bounds.</summary>
    public sealed class Cell
    {
        public string Id { get; set; } = "";
        public int Row { get; set; }
        public int Column { get; set; }
        public double LatMin { get; set; }
        public double LatMax { get; set; }
        public double LonMin { get; set; }
        public double LonMax { get; set; }

        public double CentreLatitude => (LatMin + LatMax) / 2;
        public double CentreLongitude => (LonMin + LonMax) / 2;
    }

    /// <summary>A span of months. <see cref="End"/> is the first day of the last month.</summary>
    public sealed class Period
    {
        public DateOnly Start { get; set; }
        public DateOnly End { get; set; }
        public string Step { get; set; } = "";
    }

    /// <summary>One cell and one variable, as parallel arrays of the same length.</summary>
    public sealed class Series
    {
        public string CellId { get; set; } = "";
        public string Variable { get; set; } = "";
        public string Units { get; set; } = "";
        public List<DateOnly> Dates { get; set; } = new();

        /// <summary>The value on the scan grid, or null for a missing month.</summary>
        public List<double?> Raw { get; set; } = new();

        /// <summary>The value minus that calendar month's mean over the period, or null.</summary>
        public List<double?> Anomaly { get; set; } = new();
    }

    /// <summary>
    ///  A globe layer: the record of every test for one variable. The arrays are indexed
    ///  [row][column], row 0 the southernmost and column 0 the westernmost. A cell is either
    ///  scanned, with a slope and a p-value, or skipped, with a reason.
    /// </summary>
    public sealed class GridLayer
    {
        public string Variable { get; set; } = "";
        public string Units { get; set; } = "";
        public int Rows { get; set; }
        public int Columns { get; set; }
        public double LatMin { get; set; }
        public double LatMax { get; set; }
        public double LonMin { get; set; }
        public double LonMax { get; set; }
        public double?[][] SlopePerDecade { get; set; } = Array.Empty<double?[]>();
        public double?[][] PAdjusted { get; set; } = Array.Empty<double?[]>();

        /// <summary>Null where the cell was scanned, otherwise "ocean", "ice" or "coverage".</summary>
        public string?[][] SkipReason { get; set; } = Array.Empty<string?[]>();
    }
}
