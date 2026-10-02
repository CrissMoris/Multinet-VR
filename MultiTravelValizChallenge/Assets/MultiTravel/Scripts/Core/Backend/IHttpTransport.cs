using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MultiTravel.Core.Backend
{
    /// <summary>Raw HTTP response as seen by <see cref="SupabaseBackendClient"/>.</summary>
    public sealed class HttpResponse
    {
        /// <summary>HTTP status code; 0 when the request never reached the server.</summary>
        public int StatusCode { get; set; }

        /// <summary>Response body (may be null or empty).</summary>
        public string Body { get; set; }

        /// <summary>True for DNS / connection / data-processing failures (no HTTP status).</summary>
        public bool IsNetworkError { get; set; }

        /// <summary>True when the request hit the configured timeout.</summary>
        public bool IsTimeout { get; set; }

        /// <summary>True when the request was aborted through the cancellation token.</summary>
        public bool IsCancelled { get; set; }

        /// <summary>Transport error text for logs.</summary>
        public string ErrorDetail { get; set; }

        public bool IsSuccessStatus => !IsNetworkError && StatusCode >= 200 && StatusCode < 300;
    }

    /// <summary>
    /// Minimal HTTP abstraction so <see cref="SupabaseBackendClient"/> can be unit-tested with a fake transport.
    /// Production implementation: <see cref="UnityWebRequestTransport"/>. Implementations must not throw for
    /// HTTP error statuses; they return the status and body instead.
    /// </summary>
    public interface IHttpTransport
    {
        Task<HttpResponse> PostJsonAsync(
            string url,
            string jsonBody,
            IReadOnlyDictionary<string, string> headers,
            int timeoutSeconds,
            CancellationToken cancellationToken);
    }
}
