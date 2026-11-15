using System.Text.Json;
using VIGIL.Models;

namespace VIGIL.Data
{
    /// <summary>
    ///  Asks the analysis service whether it is there (<c>GET /health</c>). It gives up after a
    ///  few seconds, so a service that has stopped is noticed quickly, and it never throws for a
    ///  service that is not answering: that is one of the answers.
    /// </summary>
    public sealed class ServiceHealthClient : IDisposable
    {
        private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

        private readonly HttpClient http;

        public ServiceHealthClient(string? address = null)
        {
            Address = DetectiveClient.ResolveAddress(address);
            http = new HttpClient { BaseAddress = new Uri(Address + "/"), Timeout = TimeSpan.FromSeconds(3) };
        }

        public string Address { get; }

        /// <summary>What the service says about itself, or null when nothing answered as the service.</summary>
        public async Task<ServiceHealth?> HealthAsync(CancellationToken cancel = default)
        {
            try
            {
                using var reply = await http.GetAsync("health", cancel);
                if (!reply.IsSuccessStatusCode)
                {
                    return null;
                }
                var health = JsonSerializer.Deserialize<ServiceHealth>(await reply.Content.ReadAsStringAsync(cancel), Json);
                return health?.Status == "ok" ? health : null;
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or IOException)
            {
                return null;
            }
        }

        public void Dispose() => http.Dispose();
    }
}
