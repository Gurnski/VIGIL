using System.Text.Json;
using VIGIL.Models;

namespace VIGIL.Data
{
    /// <summary>
    ///  Turns result contract JSON into <see cref="ResultsDocument"/>. Until the Python service
    ///  is serving real results, <see cref="ReadSnapshot"/> gives the fixture that is built into
    ///  the exe from <c>contract/fixture/snapshot.json</c>.
    /// </summary>
    public static class ResultsReader
    {
        private const string SnapshotResource = "VIGIL.Fixture.snapshot.json";

        // The contract's snake_case names map straight onto the models' PascalCase properties.
        // Fields the models don't know are skipped, which is what the contract asks of readers.
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };

        /// <summary>Reads a results document, with its findings put in rank order.</summary>
        public static ResultsDocument Read(Stream json)
        {
            ResultsDocument? document;
            try
            {
                document = JsonSerializer.Deserialize<ResultsDocument>(json, Options);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("The results could not be read: " + ex.Message, ex);
            }

            if (document == null || document.ContractVersion.Length == 0)
            {
                throw new InvalidDataException("The results are not a VIGIL results document: contract_version is missing.");
            }

            document.Findings = document.Findings.OrderBy(finding => finding.Rank).ToList();
            return document;
        }

        /// <summary>The name of a results file the app looks for in its own folder.</summary>
        public const string BesideName = "snapshot.json";

        /// <summary>
        ///  A stored scan sitting beside the app, or null when there is none that can be read. The
        ///  download carries one, so a person who only has the exe and that file still sees the
        ///  real findings. It is a results document like any other and says itself whether it is made up.
        /// </summary>
        public static ResultsDocument? ReadBeside(string? folder = null)
        {
            string path = Path.Combine(folder ?? AppContext.BaseDirectory, BesideName);
            if (!File.Exists(path))
            {
                return null;
            }
            try
            {
                using var stream = File.OpenRead(path);
                return Read(stream);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                // A file that cannot be read is treated as not there: the app then opens on its own snapshot, which says it is made up.
                return null;
            }
        }

        /// <summary>The snapshot the app keeps for running without the service.</summary>
        public static ResultsDocument ReadSnapshot()
        {
            using var stream = typeof(ResultsReader).Assembly.GetManifestResourceStream(SnapshotResource)
                ?? throw new InvalidDataException("The results snapshot is missing from the app.");
            return Read(stream);
        }
    }
}
