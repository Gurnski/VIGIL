using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using VIGIL.Models;

namespace VIGIL.Data
{
    /// <summary>
    ///  Tells the analysis service what the presenter has on show, so viewers opened from that
    ///  service can follow. See "Presenting" in <c>contract/README.md</c>. The service takes
    ///  this only from the machine it runs on, which is where the app is when presenting.
    /// </summary>
    public sealed class PresenterClient : IDisposable
    {
        private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

        private readonly HttpClient http;

        public PresenterClient(string? address = null)
        {
            Address = DetectiveClient.ResolveAddress(address);
            http = new HttpClient { BaseAddress = new Uri(Address + "/"), Timeout = TimeSpan.FromSeconds(5) };
        }

        public string Address { get; }

        /// <summary>Whether anyone is presenting, as the service has it.</summary>
        public async Task<PresentState> StateAsync(CancellationToken cancel = default)
        {
            try
            {
                using var reply = await http.GetAsync("present", cancel);
                return await Read(reply, cancel);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error) when (error is not ServiceUnavailableException)
            {
                throw new ServiceUnavailableException("The analysis service is not answering at " + Address + ".", error);
            }
        }

        /// <summary>Puts a finding, a layer and a zone on show. Any of them may be left out.</summary>
        public Task<PresentState> ShowAsync(string? findingId, string? variable, Zone? zone, CancellationToken cancel = default)
        {
            object? shownZone = zone == null
                ? null
                : new { name = zone.Name, polygon = zone.Points.Select(point => new[] { point.Latitude, point.Longitude }).ToArray() };
            return Post(new { presenting = true, finding_id = findingId, variable, zone = shownZone }, cancel);
        }

        /// <summary>Stops presenting: viewers stay where they are and stop following.</summary>
        public Task<PresentState> StopAsync(CancellationToken cancel = default) => Post(new { presenting = false }, cancel);

        private async Task<PresentState> Post(object request, CancellationToken cancel)
        {
            try
            {
                using var body = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
                using var reply = await http.PostAsync("present", body, cancel);
                return await Read(reply, cancel);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error) when (error is not ServiceUnavailableException)
            {
                throw new ServiceUnavailableException("The analysis service is not answering at " + Address + ".", error);
            }
        }

        private static async Task<PresentState> Read(HttpResponseMessage reply, CancellationToken cancel)
        {
            if (!reply.IsSuccessStatusCode)
            {
                throw new ServiceUnavailableException((int)reply.StatusCode == 404
                    ? "This copy of the analysis service is too old to present from. Restart it from the current code."
                    : $"The service would not take it ({(int)reply.StatusCode}).");
            }
            await using var stream = await reply.Content.ReadAsStreamAsync(cancel);
            return await JsonSerializer.DeserializeAsync<PresentState>(stream, Json, cancel) ?? new PresentState();
        }

        /// <summary>
        ///  The addresses other devices on the same network would open to reach the service: this
        ///  machine's own network addresses with the service's port. When the service is on
        ///  another machine, its address is the only one. The service must have been started
        ///  to listen on every address (<c>--host 0.0.0.0</c>) for any of them to answer.
        /// </summary>
        public static IReadOnlyList<string> JoinAddresses(string serviceAddress)
        {
            var service = new Uri(serviceAddress);
            if (!service.IsLoopback)
            {
                return new[] { serviceAddress.TrimEnd('/') };
            }

            var found = new List<string>();
            try
            {
                foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up
                        || adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    {
                        continue;
                    }
                    var properties = adapter.GetIPProperties();
                    // An adapter with no gateway is a virtual switch or a cable to nowhere: nobody else is on it.
                    if (properties.GatewayAddresses.Count == 0)
                    {
                        continue;
                    }
                    foreach (var address in properties.UnicastAddresses)
                    {
                        if (address.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            found.Add($"http://{address.Address}:{service.Port}");
                        }
                    }
                }
            }
            catch (NetworkInformationException)
            {
                // No list of adapters: the card says so by having no address to show.
            }
            return found;
        }

        public void Dispose() => http.Dispose();
    }
}
