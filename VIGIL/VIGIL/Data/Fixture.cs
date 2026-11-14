using System.Reflection;
using System.Text.Json;
using VIGIL.Models;

namespace VIGIL.Data
{
    /// <summary>
    ///  The contract's fixture, embedded in the exe: fake findings and four example questions
    ///  with their answers. The app builds against it until the service returns real results,
    ///  and falls back on it when the service cannot be reached. Everything from here is
    ///  made up, and the app says so wherever it shows it.
    /// </summary>
    public static class Fixture
    {
        private static readonly Lazy<IReadOnlyList<FindingSummary>> findings = new(LoadFindings);
        private static readonly Lazy<IReadOnlyList<(AskRequest Request, AskResponse Response)>> examples = new(LoadExamples);

        /// <summary>The fixture's findings, in rank order.</summary>
        public static IReadOnlyList<FindingSummary> Findings => findings.Value;

        /// <summary>The example questions, for the chat's suggestions.</summary>
        public static IReadOnlyList<string> Questions => examples.Value.Select(e => e.Request.Question).ToList();

        public static FindingSummary? FindFinding(string id) => Findings.FirstOrDefault(f => f.Id == id);

        /// <summary>The canned answer to one of the example questions, or null for any other question.</summary>
        public static AskResponse? AnswerTo(string question)
        {
            string asked = Normalise(question);
            foreach (var (request, response) in examples.Value)
            {
                if (Normalise(request.Question) == asked)
                {
                    return response;
                }
            }
            return null;
        }

        private static string Normalise(string text) => text.Trim().TrimEnd('?', '.', '!').ToLowerInvariant();

        private static Stream Open(string name)
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceStream("VIGIL.Fixture." + name)
                ?? throw new InvalidOperationException("The fixture file " + name + " is not embedded in the app.");
        }

        private static IReadOnlyList<FindingSummary> LoadFindings()
        {
            using var stream = Open("snapshot.json");
            using var document = JsonDocument.Parse(stream);
            var list = new List<FindingSummary>();
            foreach (var finding in document.RootElement.GetProperty("findings").EnumerateArray())
            {
                var cell = finding.GetProperty("cell");
                list.Add(new FindingSummary
                {
                    Id = finding.GetProperty("id").GetString() ?? "",
                    Kind = finding.GetProperty("kind").GetString() ?? "",
                    Rank = finding.GetProperty("rank").GetInt32(),
                    Summary = finding.GetProperty("summary").GetString() ?? "",
                    Significant = finding.GetProperty("significant").GetBoolean(),
                    CellId = cell.GetProperty("id").GetString() ?? "",
                    LatMin = cell.GetProperty("lat_min").GetDouble(),
                    LatMax = cell.GetProperty("lat_max").GetDouble(),
                    LonMin = cell.GetProperty("lon_min").GetDouble(),
                    LonMax = cell.GetProperty("lon_max").GetDouble()
                });
            }
            return list.OrderBy(f => f.Rank).ToList();
        }

        private static IReadOnlyList<(AskRequest, AskResponse)> LoadExamples()
        {
            using var stream = Open("ask.json");
            using var document = JsonDocument.Parse(stream);
            var list = new List<(AskRequest, AskResponse)>();
            foreach (var example in document.RootElement.GetProperty("examples").EnumerateArray())
            {
                var request = example.GetProperty("request").Deserialize<AskRequest>(DetectiveClient.Json);
                var response = example.GetProperty("response").Deserialize<AskResponse>(DetectiveClient.Json);
                if (request != null && response != null)
                {
                    list.Add((request, response));
                }
            }
            return list;
        }
    }
}
