namespace VIGIL.Models
{
    /// <summary>
    ///  What the app sends to <c>POST /ask</c>. Mirrors "Asking the AI Detective" in
    ///  <c>contract/README.md</c>. Property names are turned into snake_case on the wire.
    /// </summary>
    public sealed class AskRequest
    {
        public string Question { get; set; } = "";

        /// <summary>Earlier turns, oldest first.</summary>
        public List<AskTurn> History { get; set; } = new();

        /// <summary>The finding the question is about. Left out of the JSON when null.</summary>
        public string? FindingId { get; set; }
    }

    /// <summary>One earlier turn: <c>role</c> is "user" or "assistant".</summary>
    public sealed class AskTurn
    {
        public string Role { get; set; } = "";
        public string Text { get; set; } = "";
    }

    /// <summary>The Detective's answer, as the contract defines it.</summary>
    public sealed class AskResponse
    {
        public string ContractVersion { get; set; } = "";
        public string? RunId { get; set; }

        /// <summary>"answered", "inconclusive" or "out_of_scope".</summary>
        public string Status { get; set; } = "";

        public string Answer { get; set; } = "";
        public List<AskCitation> Citations { get; set; } = new();
        public List<string> Limitations { get; set; } = new();
        public string? Model { get; set; }

        /// <summary>What the Detective looked up on the way to the answer, in order. Empty when it looked nothing up.</summary>
        public List<AskStep> Steps { get; set; } = new();
    }

    /// <summary>
    ///  One tool call the Detective made. A step's findings were read by the Detective; only
    ///  the answer's citations back what it said.
    /// </summary>
    public sealed class AskStep
    {
        /// <summary>"search_findings", "get_finding" or "get_run_summary".</summary>
        public string Tool { get; set; } = "";

        /// <summary>One plain line saying what was asked for.</summary>
        public string Summary { get; set; } = "";

        public List<string> FindingIds { get; set; } = new();

        /// <summary>For a search: how many findings matched in all.</summary>
        public int? Matching { get; set; }

        /// <summary>For a search that named a place: where the Detective looked.</summary>
        public AskBox? Box { get; set; }

        /// <summary>True when the tool refused the request.</summary>
        public bool Failed { get; set; }
    }

    /// <summary>A latitude and longitude box.</summary>
    public sealed class AskBox
    {
        public double LatMin { get; set; }
        public double LatMax { get; set; }
        public double LonMin { get; set; }
        public double LonMax { get; set; }
    }

    /// <summary>A finding an answer rests on, and what it backs up.</summary>
    public sealed class AskCitation
    {
        public string FindingId { get; set; } = "";
        public string? Supports { get; set; }
    }
}
