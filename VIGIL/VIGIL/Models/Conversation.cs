namespace VIGIL.Models
{
    /// <summary>A saved chat with the Detective: its messages in order.</summary>
    public sealed class Conversation
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = "";
        public DateTime Created { get; set; } = DateTime.UtcNow;
        public DateTime Updated { get; set; } = DateTime.UtcNow;
        public List<ChatMessage> Messages { get; set; } = new();
    }

    public enum ChatRole
    {
        User,
        Assistant
    }

    /// <summary>Where an assistant message came from.</summary>
    public enum AnswerSource
    {
        /// <summary>The Detective service answered.</summary>
        Service,
        /// <summary>The service could not be reached and a canned answer from the fixture was shown.</summary>
        Sample,
        /// <summary>The service could not be reached and there was nothing to show. Not an answer.</summary>
        NotConnected
    }

    /// <summary>One message in a conversation.</summary>
    public sealed class ChatMessage
    {
        public ChatRole Role { get; set; }
        public string Text { get; set; } = "";
        public DateTime Time { get; set; } = DateTime.UtcNow;

        // A question may carry the finding it was asked about.
        public string? ContextFindingId { get; set; }
        public string? ContextSummary { get; set; }

        // An answer carries the rest of the contract's response.
        public string? Status { get; set; }
        public List<AskCitation> Citations { get; set; } = new();
        public List<string> Limitations { get; set; } = new();
        public string? Model { get; set; }
        public AnswerSource Source { get; set; }

        /// <summary>What the Detective looked up on the way to the answer. Only its citations back what it said.</summary>
        public List<AskStep> Steps { get; set; } = new();
    }
}
