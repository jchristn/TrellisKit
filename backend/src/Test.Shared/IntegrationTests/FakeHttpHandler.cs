namespace Test.Shared.IntegrationTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// In-memory HTTP handler for offline integration tests. Routes match by method and URL prefix (the most recently
    /// registered match wins); unmatched requests get a 404 with body {"error":"no fake route"}. Every request is recorded.
    /// Thread safe.
    /// </summary>
    public sealed class FakeHttpHandler : HttpMessageHandler
    {
        #region Private-Members

        private readonly object _Lock = new object();
        private readonly List<FakeHttpRoute> _Routes = new List<FakeHttpRoute>();
        private readonly List<RecordedRequest> _Requests = new List<RecordedRequest>();

        #endregion

        #region Public-Members

        /// <summary>
        /// Snapshot of recorded requests, oldest first.
        /// </summary>
        public IReadOnlyList<RecordedRequest> Requests
        {
            get
            {
                lock (_Lock) return _Requests.ToList();
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register a route.
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="urlPrefix">URL prefix.</param>
        /// <param name="responder">Response factory.</param>
        public void Map(string method, string urlPrefix, Func<RecordedRequest, FakeHttpResponse> responder)
        {
            lock (_Lock) _Routes.Add(new FakeHttpRoute(method, urlPrefix, responder));
        }

        /// <summary>
        /// Register a route that always returns the same JSON.
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="urlPrefix">URL prefix.</param>
        /// <param name="status">Status code.</param>
        /// <param name="body">JSON body.</param>
        public void MapJson(string method, string urlPrefix, int status, string body)
        {
            Map(method, urlPrefix, r => FakeHttpResponse.Json(status, body));
        }

        /// <summary>
        /// Count recorded requests whose URL starts with a prefix.
        /// </summary>
        /// <param name="urlPrefix">URL prefix.</param>
        /// <returns>Count.</returns>
        public int Count(string urlPrefix)
        {
            return Requests.Count(r => r.Url.AbsoluteUri.StartsWith(urlPrefix, StringComparison.Ordinal));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RecordedRequest recorded = new RecordedRequest
            {
                Method = request.Method.Method,
                Url = request.RequestUri ?? new Uri("http://127.0.0.1/")
            };
            foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
            {
                recorded.Headers[header.Key] = String.Join(",", header.Value);
            }
            if (request.Content != null)
            {
                foreach (KeyValuePair<string, IEnumerable<string>> header in request.Content.Headers)
                {
                    recorded.Headers[header.Key] = String.Join(",", header.Value);
                }
                recorded.Body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }

            FakeHttpRoute? route;
            lock (_Lock)
            {
                _Requests.Add(recorded);
                route = _Routes.LastOrDefault(r => String.Equals(r.Method, recorded.Method, StringComparison.OrdinalIgnoreCase)
                    && recorded.Url.AbsoluteUri.StartsWith(r.UrlPrefix, StringComparison.Ordinal));
            }

            FakeHttpResponse response = route != null ? route.Responder(recorded) : FakeHttpResponse.Json(404, "{\"error\":\"no fake route\"}");
            HttpResponseMessage message = new HttpResponseMessage((HttpStatusCode)response.Status)
            {
                Content = new StringContent(response.Body, Encoding.UTF8)
            };
            message.Content.Headers.ContentType = new MediaTypeHeaderValue(response.ContentType);
            return message;
        }

        #endregion
    }
}
