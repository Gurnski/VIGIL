namespace VIGIL.Models
{
    /// <summary>
    ///  The service's answer about presenting. See "Presenting" in <c>contract/README.md</c>.
    ///  The app only needs to know whether it took: what is on show is the app's own to say.
    /// </summary>
    public sealed class PresentState
    {
        public bool Presenting { get; set; }

        /// <summary>The run the service holds. Viewers follow only when it is the run they have on screen.</summary>
        public string? RunId { get; set; }

        /// <summary>Goes up by one each time what is on show changes.</summary>
        public int Sequence { get; set; }

        /// <summary>Whether the service was started to listen beyond its own machine. Null when it cannot tell.</summary>
        public bool? OpenToNetwork { get; set; }
    }
}
