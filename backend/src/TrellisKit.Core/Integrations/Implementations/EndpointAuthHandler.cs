namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// Applies a model endpoint's authentication and extra headers to every outgoing request (docs/AI_CONTRACT.md
    /// section 3.2), including streaming and model listing calls. ApiKey auth is left to PolyPrompt (it receives the key
    /// in its constructor). The handler never overwrites a header PolyPrompt already set.
    /// Thread safety: immutable after construction; safe for concurrent requests.
    /// </summary>
    public sealed class EndpointAuthHandler : DelegatingHandler
    {
        #region Public-Members

        /// <summary>
        /// Key passed to PolyPrompt 4.0 clients that require one at construction when the endpoint authenticates in
        /// another way (custom header, query parameter, Basic, or none). Every header or query value carrying it is
        /// removed before the endpoint's own authentication is applied, so it never reaches the provider.
        /// </summary>
        public const string PlaceholderKey = "trelliskit-endpoint-auth";

        #endregion

        #region Private-Members

        private readonly EndpointCredentials _Credentials;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the handler.
        /// </summary>
        /// <param name="credentials">Decrypted credentials.</param>
        /// <param name="inner">Inner handler.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public EndpointAuthHandler(EndpointCredentials credentials, HttpMessageHandler inner) : base(inner ?? throw new ArgumentNullException(nameof(inner)))
        {
            _Credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Apply the endpoint's credentials and extra headers to a request (exposed for tests).
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="credentials">Credentials.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static void Apply(HttpRequestMessage request, EndpointCredentials credentials)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(credentials);
            StripPlaceholder(request);
            string? value = credentials.AuthValue;
            switch (credentials.AuthType)
            {
                case ModelAuthTypeEnum.Bearer:
                    if (!String.IsNullOrEmpty(value) && request.Headers.Authorization == null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", value);
                    break;
                case ModelAuthTypeEnum.Basic:
                    if (request.Headers.Authorization == null)
                    {
                        string pair = (credentials.Username ?? String.Empty) + ":" + (value ?? String.Empty);
                        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(pair)));
                    }
                    break;
                case ModelAuthTypeEnum.Header:
                    if (!String.IsNullOrEmpty(credentials.HeaderName) && !String.IsNullOrEmpty(value) && !request.Headers.Contains(credentials.HeaderName))
                        request.Headers.TryAddWithoutValidation(credentials.HeaderName, value);
                    break;
                case ModelAuthTypeEnum.QueryParameter:
                    if (!String.IsNullOrEmpty(credentials.QueryParameter) && !String.IsNullOrEmpty(value) && request.RequestUri != null)
                        request.RequestUri = AppendQuery(request.RequestUri, credentials.QueryParameter, value);
                    break;
                default:
                    break;
            }
            foreach (KeyValuePair<string, string> header in credentials.ExtraHeaders)
            {
                if (String.IsNullOrEmpty(header.Key) || request.Headers.Contains(header.Key)) continue;
                request.Headers.TryAddWithoutValidation(header.Key, header.Value ?? String.Empty);
            }
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Apply(request, _Credentials);
            ModelCallProbe? probe = ModelCallProbe.Current;
            if (probe == null) return base.SendAsync(request, cancellationToken);
            return MeasuredAsync(request, probe, cancellationToken);
        }

        #endregion

        #region Private-Methods

        private async Task<HttpResponseMessage> MeasuredAsync(HttpRequestMessage request, ModelCallProbe probe, CancellationToken token)
        {
            // docs/AI_TELEMETRY.md: request and response sizes come from the declared body lengths, which costs nothing.
            probe.AddRequest(request.Content?.Headers.ContentLength);
            HttpResponseMessage response = await base.SendAsync(request, token).ConfigureAwait(false);
            probe.AddResponse(response.Content?.Headers.ContentLength);
            return response;
        }

        private static void StripPlaceholder(HttpRequestMessage request)
        {
            List<string> names = new List<string>();
            foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
                foreach (string v in header.Value)
                    if (v.Contains(PlaceholderKey, StringComparison.Ordinal)) names.Add(header.Key);
            foreach (string name in names) request.Headers.Remove(name);
            Uri? uri = request.RequestUri;
            if (uri == null || !uri.IsAbsoluteUri || uri.Query.Length < 2 || !uri.Query.Contains(PlaceholderKey, StringComparison.Ordinal)) return;
            List<string> kept = new List<string>();
            foreach (string pair in uri.Query.Substring(1).Split('&', StringSplitOptions.RemoveEmptyEntries))
                if (!pair.Contains(PlaceholderKey, StringComparison.Ordinal)) kept.Add(pair);
            UriBuilder builder = new UriBuilder(uri) { Query = String.Join("&", kept) };
            request.RequestUri = builder.Uri;
        }

        private static Uri AppendQuery(Uri uri, string name, string value)
        {
            UriBuilder builder = new UriBuilder(uri);
            string pair = Uri.EscapeDataString(name) + "=" + Uri.EscapeDataString(value);
            string existing = builder.Query.TrimStart('?');
            builder.Query = existing.Length == 0 ? pair : existing + "&" + pair;
            return builder.Uri;
        }

        #endregion
    }
}
