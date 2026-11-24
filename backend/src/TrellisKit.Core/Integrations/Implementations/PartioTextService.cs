namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Partio chunking and summarization over plain HttpClient (resolved decision 18): POST {baseUrl}/v1.0/chunk and
    /// POST {baseUrl}/v1.0/summarize with a bearer token and PascalCase JSON. Settings (documents.partio.*) are read on
    /// every call, so changes apply live.
    /// Thread safety: safe to call concurrently (HttpClient is thread safe).
    /// </summary>
    public class PartioTextService : ITextChunker, ITextSummarizer, IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Chunker name.
        /// </summary>
        public string Name
        {
            get { return "partio"; }
        }

        /// <summary>
        /// Whether Partio is turned on and has a base URL.
        /// </summary>
        public bool Configured
        {
            get
            {
                LibrarySettings s = _Settings();
                return s.PartioEnabled && Uri.TryCreate(s.PartioBaseUrl, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
            }
        }

        /// <summary>
        /// Whether summaries are configured and turned on.
        /// </summary>
        public bool Enabled
        {
            get { return Configured && _Settings().PartioSummarize && !String.IsNullOrWhiteSpace(_Settings().PartioCompletionEndpointId); }
        }

        #endregion

        #region Private-Members

        private readonly Func<LibrarySettings> _Settings;
        private readonly HttpClient _Http;
        private readonly bool _OwnsHttp;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="settings">Reads the current library settings.</param>
        /// <param name="http">HTTP client, or null to create one (disposed with this instance).</param>
        /// <exception cref="ArgumentNullException">The settings accessor is null.</exception>
        public PartioTextService(Func<LibrarySettings> settings, HttpClient? http = null)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _OwnsHttp = http == null;
            _Http = http ?? new HttpClient();
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<TextChunkResult> ChunkAsync(string text, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(text);
            LibrarySettings s = _Settings();
            Dictionary<string, object?> body = new Dictionary<string, object?>
            {
                { "Type", "Text" },
                { "Text", text },
                { "ChunkingConfiguration", new Dictionary<string, object?> { { "Strategy", s.PartioStrategy.ToString() }, { "FixedTokenCount", s.PartioChunkTokens }, { "OverlapCount", s.PartioOverlapTokens } } },
                { "Labels", new List<string>() },
                { "Tags", new Dictionary<string, string>() }
            };
            using (JsonDocument doc = await PostAsync("/v1.0/chunk", body, token).ConfigureAwait(false))
            {
                TextChunkResult result = new TextChunkResult { Chunker = Name };
                if (!doc.RootElement.TryGetProperty("Chunks", out JsonElement chunks) || chunks.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("Partio returned no Chunks.");
                foreach (JsonElement chunk in chunks.EnumerateArray())
                {
                    if (chunk.TryGetProperty("Text", out JsonElement t) && t.ValueKind == JsonValueKind.String && !String.IsNullOrWhiteSpace(t.GetString())) result.Chunks.Add(t.GetString()!.Trim());
                }
                return result;
            }
        }

        /// <inheritdoc />
        public async Task<string?> SummarizeAsync(string text, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(text);
            LibrarySettings s = _Settings();
            if (String.IsNullOrWhiteSpace(s.PartioCompletionEndpointId)) return null;
            Dictionary<string, object?> body = new Dictionary<string, object?>
            {
                { "Text", text },
                { "SummarizationConfiguration", new Dictionary<string, object?> { { "CompletionEndpointId", s.PartioCompletionEndpointId.Trim() }, { "Order", "BottomUp" }, { "MaxSummaryTokens", s.PartioMaxSummaryTokens } } }
            };
            using (JsonDocument doc = await PostAsync("/v1.0/summarize", body, token).ConfigureAwait(false))
            {
                if (doc.RootElement.TryGetProperty("Success", out JsonElement ok) && ok.ValueKind == JsonValueKind.False) throw new InvalidOperationException("Partio summarization did not succeed.");
                if (!doc.RootElement.TryGetProperty("Summary", out JsonElement summary) || summary.ValueKind != JsonValueKind.String) return null;
                string value = summary.GetString() ?? String.Empty;
                return value.Trim().Length == 0 ? null : value.Trim();
            }
        }

        /// <summary>
        /// Dispose the owned HTTP client.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose pattern.
        /// </summary>
        /// <param name="disposing">Whether called from Dispose.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing && _OwnsHttp) _Http.Dispose();
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private async Task<JsonDocument> PostAsync(string path, object body, CancellationToken token)
        {
            LibrarySettings s = _Settings();
            if (!Configured) throw new InvalidOperationException("Partio is not configured (documents.partio.enabled and documents.partio.baseUrl).");
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, s.PartioBaseUrl.TrimEnd('/') + path))
            {
                if (!String.IsNullOrEmpty(s.PartioApiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.PartioApiKey);
                request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(s.PartioTimeoutSeconds));
                    try
                    {
                        using (HttpResponseMessage response = await _Http.SendAsync(request, timeout.Token).ConfigureAwait(false))
                        {
                            string json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
                            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Partio " + path + " returned " + (int)response.StatusCode + ".");
                            return JsonDocument.Parse(json);
                        }
                    }
                    catch (HttpRequestException e)
                    {
                        throw new InvalidOperationException("Partio " + path + " failed: " + e.Message, e);
                    }
                    catch (OperationCanceledException e) when (!token.IsCancellationRequested)
                    {
                        throw new InvalidOperationException("Partio " + path + " timed out.", e);
                    }
                    catch (JsonException e)
                    {
                        throw new InvalidOperationException("Partio " + path + " returned invalid JSON.", e);
                    }
                }
            }
        }

        #endregion
    }
}
