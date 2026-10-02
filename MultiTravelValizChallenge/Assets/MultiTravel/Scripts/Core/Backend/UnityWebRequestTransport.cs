using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace MultiTravel.Core.Backend
{
    /// <summary>
    /// <see cref="IHttpTransport"/> built on <see cref="UnityWebRequest"/> and Unity 6 awaitables.
    /// Must be called from the main thread; the continuation resumes on the main thread without blocking it.
    /// </summary>
    public sealed class UnityWebRequestTransport : IHttpTransport
    {
        private const string JsonContentType = "application/json";

        public async Task<HttpResponse> PostJsonAsync(
            string url,
            string jsonBody,
            IReadOnlyDictionary<string, string> headers,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("URL is required.", nameof(url));
            }

            var payload = Encoding.UTF8.GetBytes(jsonBody ?? string.Empty);

            using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(payload) { contentType = JsonContentType };
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = Math.Max(1, timeoutSeconds);

                if (headers != null)
                {
                    foreach (var header in headers)
                    {
                        request.SetRequestHeader(header.Key, header.Value);
                    }
                }

                using (cancellationToken.Register(() => AbortSafely(request)))
                {
                    await request.SendWebRequest();
                }

                var response = new HttpResponse
                {
                    StatusCode = (int)request.responseCode,
                    Body = ReadBodySafely(request),
                    ErrorDetail = request.error
                };

                switch (request.result)
                {
                    case UnityWebRequest.Result.Success:
                    case UnityWebRequest.Result.ProtocolError:
                        // HTTP status (and body) carry the outcome.
                        break;
                    default:
                        response.IsNetworkError = true;
                        response.IsCancelled = cancellationToken.IsCancellationRequested;
                        response.IsTimeout = !response.IsCancelled
                                             && request.error != null
                                             && request.error.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0;
                        break;
                }

                return response;
            }
        }

        private static string ReadBodySafely(UnityWebRequest request)
        {
            try
            {
                return request.downloadHandler != null ? request.downloadHandler.text : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void AbortSafely(UnityWebRequest request)
        {
            try
            {
                if (!request.isDone)
                {
                    request.Abort();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[MultiTravel] Could not abort web request: " + ex.Message);
            }
        }
    }
}
