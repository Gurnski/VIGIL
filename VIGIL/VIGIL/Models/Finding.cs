using System.Text.Json;

namespace VIGIL.Models
{
    /// <summary>The values of <see cref="Finding.Kind"/>.</summary>
    public static class FindingKind
    {
        public const string Trend = "trend";
        public const string ChangePoint = "change_point";
        public const string Cascade = "cascade";
        public const string Contradiction = "contradiction";
    }

    /// <summary>
    ///  One finding from the scan. One type covers all four kinds: the block that matches
    ///  <see cref="Kind"/> (<see cref="Result"/>, <see cref="ChangePoint"/>, <see cref="Cascade"/>
    ///  or <see cref="Contradiction"/>) is present and the other three are null.
    /// </summary>
    public sealed class Finding
    {
        public string Id { get; set; } = "";
        public string Kind { get; set; } = "";

        /// <summary>1 is the top of the list. The app sorts by this and never by the score.</summary>
        public int Rank { get; set; }

        /// <summary>One line for the findings list, written by the service.</summary>
        public string Summary { get; set; } = "";

        /// <summary>Whether it passes the run's threshold after correction for multiple testing.</summary>
        public bool Significant { get; set; }

        public Cell Cell { get; set; } = new();
        public Period Period { get; set; } = new();

        /// <summary>One source per variable. A cascade has two: the leading one, then the following one.</summary>
        public List<Variable> Variables { get; set; } = new();

        public Preprocessing Preprocessing { get; set; } = new();
        public Method Method { get; set; } = new();
        public TrendResult? Result { get; set; }
        public ChangePoint? ChangePoint { get; set; }
        public Cascade? Cascade { get; set; }
        public Contradiction? Contradiction { get; set; }
        public List<FindingLink> Links { get; set; } = new();
        public Score Score { get; set; } = new();
        public Standing Standing { get; set; } = new();
    }

    /// <summary>What was done to the data before the test.</summary>
    public sealed class Preprocessing
    {
        public string Regridding { get; set; } = "";
        public string Aggregation { get; set; } = "";
        public string Anomaly { get; set; } = "";
        public string MissingDataRule { get; set; } = "";
        public int MonthsPresent { get; set; }
        public int MonthsExpected { get; set; }
        public double Coverage { get; set; }
    }

    /// <summary>The test and its settings. Free text: the app shows it and never branches on it.</summary>
    public sealed class Method
    {
        public string Test { get; set; } = "";

        /// <summary>A mix of strings, numbers and booleans.</summary>
        public Dictionary<string, JsonElement> Parameters { get; set; } = new();
    }

    /// <summary>The <c>result</c> block of a trend.</summary>
    public sealed class TrendResult
    {
        /// <summary>"increasing" or "decreasing" when significant, otherwise "none".</summary>
        public string Direction { get; set; } = "";

        public double SlopePerDecade { get; set; }
        public string Units { get; set; } = "";
        public double CiLow { get; set; }
        public double CiHigh { get; set; }
        public double CiLevel { get; set; }

        /// <summary>Before correction for multiple testing.</summary>
        public double PValue { get; set; }

        /// <summary>After it. This is the one compared with the threshold.</summary>
        public double PAdjusted { get; set; }
    }

    public sealed class ChangePoint
    {
        /// <summary>The month the series changes level.</summary>
        public DateOnly Date { get; set; }

        public DateOnly DateLow { get; set; }
        public DateOnly DateHigh { get; set; }
        public double LevelBefore { get; set; }
        public double LevelAfter { get; set; }
        public double Shift { get; set; }
        public string Units { get; set; } = "";
    }

    /// <summary>An association in time between two variables in one cell. Never a claim of cause.</summary>
    public sealed class Cascade
    {
        public string LeadingVariable { get; set; } = "";
        public string FollowingVariable { get; set; } = "";
        public int LagMonths { get; set; }
        public double Correlation { get; set; }

        /// <summary>The correlation at every lag tested, starting at lag 0. Optional.</summary>
        public List<double>? CorrelationsByLag { get; set; }

        public double PValue { get; set; }
        public double PAdjusted { get; set; }
    }

    /// <summary>Two nearby cells with significant trends of opposite sign in the same variable.</summary>
    public sealed class Contradiction
    {
        public string Variable { get; set; } = "";
        public Cell PairedCell { get; set; } = new();
        public double SlopePerDecade { get; set; }
        public double PairedSlopePerDecade { get; set; }
        public string Units { get; set; } = "";
        public double? DistanceKm { get; set; }
        public List<string> Confounders { get; set; } = new();
    }

    /// <summary>A pointer to a related finding. Links are written on both ends.</summary>
    public sealed class FindingLink
    {
        public string FindingId { get; set; } = "";
        public string Kind { get; set; } = "";
        public int? LagMonths { get; set; }
        public double? Correlation { get; set; }
        public string? CellId { get; set; }
    }

    /// <summary>The ranking score and its parts. Each runs from 0 to 1 and higher is better.</summary>
    public sealed class Score
    {
        public double Total { get; set; }
        public double EffectSize { get; set; }
        public double Certainty { get; set; }
        public double Coverage { get; set; }
        public double Robustness { get; set; }
        public double CrossSystem { get; set; }
    }

    public sealed class Standing
    {
        /// <summary>"exploratory" or "retested".</summary>
        public string Status { get; set; } = "";

        public List<string> Limitations { get; set; } = new();
    }
}
