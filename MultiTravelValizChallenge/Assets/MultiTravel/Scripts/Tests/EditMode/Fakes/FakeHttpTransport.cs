using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MultiTravel.Core.Backend;

namespace MultiTravel.Tests.EditMode.Fakes
{
    /// <summary>Scripted <see cref="IHttpTransport"/>: records requests and replays queued responses synchronously.</summary>
    public sealed class FakeHttpTransport : IHttpTransport
    {
        public sealed class RecordedRequest
        {
            public string Url;
            public string Body;
            public IReadOnlyDictionary<string, string> Headers;
            public int TimeoutSeconds;
        }

        private readonly Queue<HttpResponse> responses = new Queue<HttpResponse>();

        public List<RecordedRequest> Requests { get; } = new List<RecordedRequest>();

        /// <summary>When set, the next call throws this exception instead of returning a response.</summary>
        public Exception ExceptionToThrow { get; set; }

        public RecordedRequest LastRequest => Requests.Count > 0 ? Requests[Requests.Count - 1] : null;

        public void Enqueue(HttpResponse response)
        {
            responses.Enqueue(response);
        }

        public void EnqueueJson(int statusCode, string body)
        {
            responses.Enqueue(new HttpResponse { StatusCode = statusCode, Body = body });
        }

        public void EnqueueNetworkError(string detail = "Cannot connect to destination host")
        {
            responses.Enqueue(new HttpResponse { IsNetworkError = true, ErrorDetail = detail });
        }

        public void EnqueueTimeout()
        {
            responses.Enqueue(new HttpResponse { IsNetworkError = true, IsTimeout = true, ErrorDetail = "Request timeout" });
        }

        public Task<HttpResponse> PostJsonAsync(
            string url,
            string jsonBody,
            IReadOnlyDictionary<string, string> headers,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest { Url = url, Body = jsonBody, Headers = headers, TimeoutSeconds = timeoutSeconds });

            if (ExceptionToThrow != null)
            {
                var ex = ExceptionToThrow;
                ExceptionToThrow = null;
                throw ex;
            }

            if (responses.Count == 0)
            {
                throw new InvalidOperationException("FakeHttpTransport: no response queued for " + url);
            }

            return Task.FromResult(responses.Dequeue());
        }
    }
}
