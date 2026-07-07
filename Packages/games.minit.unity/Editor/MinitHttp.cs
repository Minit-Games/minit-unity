using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MinitGames.Editor
{
    /// <summary>Result of a <see cref="MinitHttp"/> request.</summary>
    public struct MinitHttpResponse
    {
        public int StatusCode;
        public bool IsSuccess;
        public string Body;
    }

    /// <summary>
    /// Small, dependency-free HTTP helper shared by the Login &amp; Upload editor tools
    /// (auth exchange, identity lookup, and — separately — the drops/upload REST client).
    /// Wraps a single shared <see cref="HttpClient"/>. Never bakes in an api key — callers
    /// pass a bearer token explicitly for authenticated requests.
    /// </summary>
    public static class MinitHttp
    {
        // A single shared HttpClient is the recommended usage pattern (avoids socket
        // exhaustion from creating/disposing one per request).
        private static readonly HttpClient Client = new HttpClient();

        /// <summary>Sends a GET request, optionally with a bearer token.</summary>
        public static async Task<MinitHttpResponse> GetAsync(string url, string bearerToken = null, CancellationToken ct = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyBearerToken(request, bearerToken);
            return await SendAsync(request, ct);
        }

        /// <summary>Sends a POST request with a JSON body, optionally with a bearer token.</summary>
        public static async Task<MinitHttpResponse> PostJsonAsync(string url, string jsonBody, string bearerToken = null, CancellationToken ct = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonBody ?? string.Empty, Encoding.UTF8, "application/json")
            };
            ApplyBearerToken(request, bearerToken);
            return await SendAsync(request, ct);
        }

        private static void ApplyBearerToken(HttpRequestMessage request, string bearerToken)
        {
            if (!string.IsNullOrEmpty(bearerToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        private static async Task<MinitHttpResponse> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using HttpResponseMessage response = await Client.SendAsync(request, ct);
            string body = await response.Content.ReadAsStringAsync();

            return new MinitHttpResponse
            {
                StatusCode = (int)response.StatusCode,
                IsSuccess = response.IsSuccessStatusCode,
                Body = body
            };
        }
    }
}
