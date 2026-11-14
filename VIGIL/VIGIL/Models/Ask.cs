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
    }

    /// <summary>A finding an answer rests on, and what it backs up.</summary>
    public sealed class AskCitation
    {
        public string FindingId { get; set; } = "";
        public string? Supports { get; set; }
    }
}
