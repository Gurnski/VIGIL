using System.Net.Http.Json;
using System.Text.Json;
using VIGIL.Models;

namespace VIGIL.Data
{
    /// <summary>Thrown when the analysis service cannot be reached, or answers with an error. The message is fit to show.</summary>
    public sealed class ServiceUnavailableException : Exception
    {
        public ServiceUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
    }

    /// <summary>
    ///  Calls the Python service for results: the stored scan (<c>GET /snapshot</c>), one
    ///  finding with its series (<c>GET /findings/{id}</c>), a fresh analysis of a region
    ///  (<c>POST /analyse</c>) and the series and trends over a drawn zone (<c>POST /zone</c>).
    ///  It computes nothing: every number it returns is the service's. The address is the one
    ///  the Detective uses.
    /// </summary>
    public sealed class ResultsClient
    {
        private static readonly TimeSpan ConnectTime = TimeSpan.FromSeconds(3);

        // Reads the contract's snake_case names, and writes them for a request.
        private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

        private readonly HttpClient http;

        public ResultsClient(string? address = null)
        {
            Address = DetectiveClient.ResolveAddress(address);
            http = new HttpClient { BaseAddress = new Uri(Address + "/"), Timeout = TimeSpan.FromSeconds(60) };
        }

        public string Address { get; }

        /// <summary>
        ///  The whole stored scan. Gives up quickly when nothing is listening, so the app can
        ///  fall back on the snapshot built into it without holding up the start.
        /// </summary>
        public async Task<ResultsDocument> SnapshotAsync(CancellationToken cancel = default)
        {
            try
            {
                using var connecting = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                connecting.CancelAfter(ConnectTime);
                using var reply = await http.GetAsync("snapshot", HttpCompletionOption.ResponseHeadersRead, connecting.Token);
                if (!reply.IsSuccessStatusCode)
                {
                    throw new ServiceUnavailableException($"The service answered with an error ({(int)reply.StatusCode}).");
                }
                // A real scan runs to several megabytes, so the body gets the client's full time, not the connecting time.
                await using var body = await reply.Content.ReadAsStreamAsync(cancel);
                using var buffered = new MemoryStream();
                await body.CopyToAsync(buffered, cancel);
                buffered.Position = 0;
                return ResultsReader.Read(buffered);
            }
            catch (Exception error) when (error is not ServiceUnavailableException)
            {
                throw Unavailable(error, cancel);
            }
        }

        /// <summary>One finding and every series its evidence needs, as a results document holding just those.</summary>
        public Task<ResultsDocument> FindingAsync(string findingId, CancellationToken cancel = default) =>
            DocumentAsync(() => http.GetAsync("findings/" + Uri.EscapeDataString(findingId), cancel), cancel);

        /// <summary>
        ///  A fresh analysis of the cells a box covers, run by the service from the gridded data.
        ///  Its corrections for many tests are made over that region only.
        /// </summary>
        public Task<ResultsDocument> AnalyseAsync(double latMin, double latMax, double lonMin, double lonMax, CancellationToken cancel = default)
        {
            var request = new { lat_min = latMin, lat_max = latMax, lon_min = lonMin, lon_max = lonMax };
            return DocumentAsync(() =>
            {
                var body = new StringContent(JsonSerializer.Serialize(request), System.Text.Encoding.UTF8, "application/json");
                return http.PostAsync("analyse", body, cancel);
            }, cancel);
        }

        /// <summary>Makes a call that answers with a results document, and reads it.</summary>
        private static async Task<ResultsDocument> DocumentAsync(Func<Task<HttpResponseMessage>> call, CancellationToken cancel)
        {
            try
            {
                using var reply = await call();
                if (!reply.IsSuccessStatusCode)
                {
                    throw new ServiceUnavailableException(await DetailOf(reply, cancel));
                }
                await using var body = await reply.Content.ReadAsStreamAsync(cancel);
                using var buffered = new MemoryStream();
                await body.CopyToAsync(buffered, cancel);
                buffered.Position = 0;
                return ResultsReader.Read(buffered);
            }
            catch (Exception error) when (error is not ServiceUnavailableException)
            {
                throw Unavailable(error, cancel);
            }
        }

        /// <summary>The series and trend of each variable over a zone, from the outline the user drew.</summary>
        public async Task<ZoneAnalysis> ZoneAsync(IReadOnlyList<ZonePoint> outline, CancellationToken cancel = default)
        {
            try
            {
                var request = new { polygon = outline.Select(point => new[] { point.Latitude, point.Longitude }).ToArray() };
                // Sent as one block with its length, not in chunks, which some gateways in front of the service refuse.
                using var body = new StringContent(JsonSerializer.Serialize(request, Json), System.Text.Encoding.UTF8, "application/json");
                using var reply = await http.PostAsync("zone", body, cancel);
                if (!reply.IsSuccessStatusCode)
                {
                    throw new ServiceUnavailableException(await DetailOf(reply, cancel));
                }

                var analysis = await reply.Content.ReadFromJsonAsync<ZoneAnalysis>(Json, cancel);
                if (analysis == null || analysis.ContractVersion.Length == 0)
                {
                    throw new ServiceUnavailableException("The service sent an answer the app could not read.");
                }
                return analysis;
            }
            catch (Exception error) when (error is not ServiceUnavailableException)
            {
                throw Unavailable(error, cancel);
            }
        }

        /// <summary>A variable year by year: each scanned cell's mean anomaly in each calendar year. For looking, not a result.</summary>
        public async Task<YearsField> YearsAsync(string variable, CancellationToken cancel = default)
        {
            try
            {
                using var reply = await http.GetAsync("years?variable=" + Uri.EscapeDataString(variable), cancel);
                if (!reply.IsSuccessStatusCode)
                {
                    throw new ServiceUnavailableException((int)reply.StatusCode == 404 && !(await reply.Content.ReadAsStringAsync(cancel)).Contains("variable")
                        ? "This copy of the analysis service is too old to show the years. Restart it from the current code."
                        : await DetailOf(reply, cancel));
                }

                var field = await reply.Content.ReadFromJsonAsync<YearsField>(Json, cancel);
                if (field == null || field.ContractVersion.Length == 0)
                {
                    throw new ServiceUnavailableException("The service sent an answer the app could not read.");
                }
                return field;
            }
            catch (Exception error) when (error is not ServiceUnavailableException)
            {
                throw Unavailable(error, cancel);
            }
        }

        /// <summary>The reason the service gave for refusing, from the contract's <c>{"detail": "..."}</c> body.</summary>
        private static async Task<string> DetailOf(HttpResponseMessage reply, CancellationToken cancel)
        {
            try
            {
                using var document = JsonDocument.Parse(await reply.Content.ReadAsStringAsync(cancel));
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
                {
                    return detail.GetString() ?? "";
                }
            }
            catch (JsonException)
            {
                // Not the contract's error body, such as an HTML page from a gateway.
            }
            return $"The service answered with an error ({(int)reply.StatusCode}).";
        }

        private static Exception Unavailable(Exception error, CancellationToken cancel) => error switch
        {
            OperationCanceledException when cancel.IsCancellationRequested => error,
            HttpRequestException => new ServiceUnavailableException("The service could not be reached.", error),
            OperationCanceledException => new ServiceUnavailableException("The service took too long to answer.", error),
            // Anything else is a reply that is not the contract's JSON.
            _ => new ServiceUnavailableException("The service sent an answer the app could not read.", error)
        };
    }
}
