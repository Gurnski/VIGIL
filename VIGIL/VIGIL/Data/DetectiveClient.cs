using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using VIGIL.Models;

namespace VIGIL.Data
{
    /// <summary>Thrown when the Detective service cannot be reached or does not answer properly.</summary>
    public sealed class DetectiveUnavailableException : Exception
    {
        public DetectiveUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
    }

    /// <summary>
    ///  Calls <c>POST /ask</c> on the Python service. The app never talks to a language model
    ///  itself and holds no API key: the service does that.
    ///
    ///  The address comes from the environment variable <c>VIGIL_API_URL</c>, and is
    ///  <c>http://127.0.0.1:8000</c> when that is not set.
    /// </summary>
    public sealed class DetectiveClient
    {
        public const string DefaultAddress = "http://127.0.0.1:8000";

        /// <summary>The JSON settings for everything the app exchanges with the service.</summary>
        public static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly HttpClient http;

        public DetectiveClient(string? address = null)
        {
            Address = ResolveAddress(address);
            http = new HttpClient { BaseAddress = new Uri(Address + "/"), Timeout = TimeSpan.FromSeconds(90) };
        }

        /// <summary>
        ///  The service's address: the one given, or the environment variable <c>VIGIL_API_URL</c>,
        ///  or the default when neither is a usable web address. No trailing slash.
        /// </summary>
        public static string ResolveAddress(string? address = null)
        {
            address ??= Environment.GetEnvironmentVariable("VIGIL_API_URL");
            address = (address ?? "").Trim().TrimEnd('/');
            // A missing or mistyped address must not stop the app from opening, so it falls back to the default.
            bool usable = Uri.TryCreate(address + "/", UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
            return usable ? address : DefaultAddress;
        }

        public string Address { get; }

        public async Task<AskResponse> AskAsync(AskRequest request, CancellationToken cancel = default)
        {
            try
            {
                // Sent as one block with its length, not in chunks, which some gateways in front of the service refuse.
                using var body = new StringContent(JsonSerializer.Serialize(request, Json), System.Text.Encoding.UTF8, "application/json");
                using var reply = await http.PostAsync("ask", body, cancel);
                if (!reply.IsSuccessStatusCode)
                {
                    throw new DetectiveUnavailableException($"The service answered with an error ({(int)reply.StatusCode}).");
                }

                var response = await reply.Content.ReadFromJsonAsync<AskResponse>(Json, cancel);
                if (response == null || response.Answer.Length == 0 || response.Status.Length == 0)
                {
                    throw new DetectiveUnavailableException("The service sent an answer the app could not read.");
                }
                return response;
            }
            catch (DetectiveUnavailableException)
            {
                throw;
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException error)
            {
                throw new DetectiveUnavailableException("The service could not be reached.", error);
            }
            catch (OperationCanceledException error)
            {
                throw new DetectiveUnavailableException("The service took too long to answer.", error);
            }
            catch (Exception error)
            {
                // Anything else is a reply that isn't the contract's JSON, such as an HTML error page from a gateway.
                throw new DetectiveUnavailableException("The service sent an answer the app could not read.", error);
            }
        }
    }
}
