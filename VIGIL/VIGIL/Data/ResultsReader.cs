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
        private const string SnapshotResource = "VIGIL.Contract.Snapshot.json";

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

        /// <summary>The snapshot the app keeps for running without the service.</summary>
        public static ResultsDocument ReadSnapshot()
        {
            using var stream = typeof(ResultsReader).Assembly.GetManifestResourceStream(SnapshotResource)
                ?? throw new InvalidDataException("The results snapshot is missing from the app.");
            return Read(stream);
        }
    }
}
