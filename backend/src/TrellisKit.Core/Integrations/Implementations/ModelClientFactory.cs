namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Security.Cryptography;
    using System.Threading;
    using System.Threading.Tasks;
    using PolyPrompt.Auth;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using SyslogLogging;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Security;

    /// <summary>
    /// PolyPrompt client factory (docs/AI_CONTRACT.md section 3). Each endpoint gets one cached HttpClient over a
    /// SocketsHttpHandler (pooled connection lifetime 5 minutes, infinite transport timeout since PolyPrompt enforces
    /// TimeoutMs) wrapped by <see cref="EndpointAuthHandler"/>; the cache key is (property, endpoint, updated time), so an
    /// edit replaces the handler. Clients are created per call with RedactCredentials on and call detail capture set
    /// by ai.captureCallDetails (Metadata when on, None otherwise). Decrypted secrets live only in the cached handler
    /// and redactor. Azure OpenAI, Vertex AI, and Bedrock use the provider credential overloads of section 3.3: an Azure
    /// api-key or Entra bearer token, a Vertex AI service account (or application default credentials), and Bedrock
    /// SigV4 credentials (static keys or the environment); their credential providers are cached with the endpoint so
    /// OAuth tokens are reused across calls.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public sealed class ModelClientFactory : IModelClientFactory, IDisposable
    {
        #region Private-Members

        private static readonly LoggingModule _Silent = CreateSilent();
        private readonly ISecretProtector _Protector;
        private readonly Func<bool> _CaptureCallDetails;
        private readonly ConcurrentDictionary<string, CachedModelEndpoint> _Cache = new ConcurrentDictionary<string, CachedModelEndpoint>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, object> _Providers = new ConcurrentDictionary<string, object>(StringComparer.Ordinal);
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the factory.
        /// </summary>
        /// <param name="protector">Secret protector that decrypts stored credentials.</param>
        /// <param name="captureCallDetails">Returns ai.captureCallDetails (read live).</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public ModelClientFactory(ISecretProtector protector, Func<bool> captureCallDetails)
        {
            _Protector = protector ?? throw new ArgumentNullException(nameof(protector));
            _CaptureCallDetails = captureCallDetails ?? throw new ArgumentNullException(nameof(captureCallDetails));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public bool Supports(ModelApiTypeEnum apiType, ModelEndpointKindEnum kind)
        {
            switch (kind)
            {
                case ModelEndpointKindEnum.Completion:
                    return apiType == ModelApiTypeEnum.Ollama || apiType == ModelApiTypeEnum.OpenAI || apiType == ModelApiTypeEnum.OpenAICompatible || apiType == ModelApiTypeEnum.Gemini ||
                        apiType == ModelApiTypeEnum.Anthropic || apiType == ModelApiTypeEnum.OllamaCloud || apiType == ModelApiTypeEnum.XAI || apiType == ModelApiTypeEnum.Cohere ||
                        apiType == ModelApiTypeEnum.AzureOpenAI || apiType == ModelApiTypeEnum.VertexAI || apiType == ModelApiTypeEnum.Bedrock;
                case ModelEndpointKindEnum.Embedding:
                    return apiType == ModelApiTypeEnum.Ollama || apiType == ModelApiTypeEnum.OpenAI || apiType == ModelApiTypeEnum.OpenAICompatible || apiType == ModelApiTypeEnum.Gemini ||
                        apiType == ModelApiTypeEnum.OllamaCloud || apiType == ModelApiTypeEnum.Cohere || apiType == ModelApiTypeEnum.VoyageAI ||
                        apiType == ModelApiTypeEnum.Tei || apiType == ModelApiTypeEnum.AzureOpenAI || apiType == ModelApiTypeEnum.VertexAI || apiType == ModelApiTypeEnum.Bedrock;
                case ModelEndpointKindEnum.Rerank:
                    return apiType == ModelApiTypeEnum.Cohere || apiType == ModelApiTypeEnum.VoyageAI || apiType == ModelApiTypeEnum.Tei || apiType == ModelApiTypeEnum.Bedrock;
                default:
                    return false;
            }
        }

        /// <inheritdoc />
        public bool SupportsModelListing(ModelApiTypeEnum apiType)
        {
            return apiType == ModelApiTypeEnum.Ollama || apiType == ModelApiTypeEnum.OpenAI || apiType == ModelApiTypeEnum.OpenAICompatible || apiType == ModelApiTypeEnum.Gemini ||
                apiType == ModelApiTypeEnum.Anthropic || apiType == ModelApiTypeEnum.OllamaCloud || apiType == ModelApiTypeEnum.XAI || apiType == ModelApiTypeEnum.Cohere ||
                apiType == ModelApiTypeEnum.Tei || apiType == ModelApiTypeEnum.AzureOpenAI || apiType == ModelApiTypeEnum.Bedrock;
        }

        /// <inheritdoc />
        public bool IsAuthTypeAllowed(ModelApiTypeEnum apiType, ModelAuthTypeEnum authType)
        {
            switch (apiType)
            {
                case ModelApiTypeEnum.AzureOpenAI:
                    return authType == ModelAuthTypeEnum.ApiKey || authType == ModelAuthTypeEnum.Bearer;
                case ModelApiTypeEnum.VertexAI:
                case ModelApiTypeEnum.Bedrock:
                    return authType == ModelAuthTypeEnum.ApiKey || authType == ModelAuthTypeEnum.None;
                case ModelApiTypeEnum.XAI:
                case ModelApiTypeEnum.OllamaCloud:
                    return authType == ModelAuthTypeEnum.ApiKey;
                default:
                    return true;
            }
        }

        /// <inheritdoc />
        public EndpointCredentials Credentials(ModelEndpoint endpoint)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            EndpointCredentials credentials = new EndpointCredentials
            {
                AuthType = endpoint.AuthType,
                HeaderName = endpoint.AuthHeaderName,
                QueryParameter = endpoint.AuthQueryParameter,
                Username = endpoint.AuthUsername
            };
            if (!String.IsNullOrEmpty(endpoint.AuthValueSecret))
                credentials.AuthValue = _Protector.Unprotect(endpoint.AuthValueSecret, SecretBoxProtector.EndpointAssociatedData(endpoint.PropertyId, endpoint.Id, "authValue"));
            foreach (ModelEndpointHeader header in endpoint.Headers)
            {
                string value = String.IsNullOrEmpty(header.ValueSecret)
                    ? String.Empty
                    : _Protector.Unprotect(header.ValueSecret, SecretBoxProtector.EndpointAssociatedData(endpoint.PropertyId, endpoint.Id, "extraHeader:" + header.Name.Trim().ToLowerInvariant()));
                credentials.ExtraHeaders.Add(new KeyValuePair<string, string>(header.Name, value));
            }
            return credentials;
        }

        /// <inheritdoc />
        public ModelClientLease<ProviderClientBase> CreateCompletion(ModelEndpoint endpoint, EndpointCredentials? credentials = null)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            if (!Supports(endpoint.ApiType, ModelEndpointKindEnum.Completion)) throw new NotSupportedException(endpoint.ApiType + " has no completion client in this build.");
            ModelClientLease<ProviderClientBase> lease = Create(endpoint, credentials, false);
            CompletionOptions? section = lease.Client.Defaults.Completion;
            string? model = endpoint.ApiType == ModelApiTypeEnum.AzureOpenAI ? Deployment(endpoint) : endpoint.Model;
            if (section != null && !String.IsNullOrWhiteSpace(model)) section.Model = model.Trim();
            return lease;
        }

        /// <inheritdoc />
        public ModelClientLease<ProviderClientBase> CreateEmbedding(ModelEndpoint endpoint, EndpointCredentials? credentials = null)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            if (!Supports(endpoint.ApiType, ModelEndpointKindEnum.Embedding)) throw new NotSupportedException(endpoint.ApiType + " has no embedding client in this build.");
            ModelClientLease<ProviderClientBase> lease = Create(endpoint, credentials, false);
            EmbeddingOptions? section = lease.Client.Defaults.Embedding;
            string? model = endpoint.ApiType == ModelApiTypeEnum.AzureOpenAI ? Deployment(endpoint) : endpoint.Model;
            if (section != null && !String.IsNullOrWhiteSpace(model)) section.Model = model.Trim();
            return lease;
        }

        /// <inheritdoc />
        public ModelClientLease<ProviderClientBase> CreateRerank(ModelEndpoint endpoint, EndpointCredentials? credentials = null)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            if (!Supports(endpoint.ApiType, ModelEndpointKindEnum.Rerank)) throw new NotSupportedException(endpoint.ApiType + " has no rerank client in this build.");
            ModelClientLease<ProviderClientBase> lease = Create(endpoint, credentials, false);
            RerankOptions? section = lease.Client.Defaults.Rerank;
            if (section != null && !String.IsNullOrWhiteSpace(endpoint.Model)) section.Model = endpoint.Model.Trim();
            return lease;
        }

        /// <inheritdoc />
        public ModelClientLease<ProviderClientBase> CreateModelLister(ModelEndpoint endpoint, EndpointCredentials? credentials = null)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            if (!SupportsModelListing(endpoint.ApiType)) throw new NotSupportedException(endpoint.ApiType + " cannot list models.");
            // Bedrock: endpointUrl overrides the runtime host; model listing always uses the regional control plane.
            return Create(endpoint, credentials, endpoint.ApiType == ModelApiTypeEnum.Bedrock);
        }

        /// <inheritdoc />
        public AiSecretRedactor RedactorFor(ModelEndpoint endpoint)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            try
            {
                return Cached(endpoint).Redactor;
            }
            catch (CryptographicException)
            {
                return AiSecretRedactor.None;
            }
        }

        /// <inheritdoc />
        public void Evict(string propertyId, string endpointId)
        {
            if (_Cache.TryRemove(propertyId + "|" + endpointId, out CachedModelEndpoint? old)) Retire(old);
            foreach (string key in _Providers.Keys)
            {
                if (key.StartsWith(propertyId + "|" + endpointId + "|", StringComparison.Ordinal) && _Providers.TryRemove(key, out object? provider)) RetireProvider(provider);
            }
        }

        /// <summary>
        /// Dispose every cached HTTP client.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            foreach (CachedModelEndpoint cached in _Cache.Values) cached.Http.Dispose();
            _Cache.Clear();
            foreach (object provider in _Providers.Values) (provider as IDisposable)?.Dispose();
            _Providers.Clear();
        }

        #endregion

        #region Private-Methods

        private static LoggingModule CreateSilent()
        {
            LoggingModule module = new LoggingModule();
            module.Settings.EnableConsole = false;
            return module;
        }

        private void Resolve(ModelEndpoint endpoint, EndpointCredentials? explicitCredentials, out HttpClient http, out AiSecretRedactor redactor, out EndpointCredentials credentials, out HttpClient? owned)
        {
            if (explicitCredentials != null)
            {
                credentials = explicitCredentials;
                http = BuildHttp(credentials);
                redactor = new AiSecretRedactor(credentials);
                owned = http;
                return;
            }
            CachedModelEndpoint cached = Cached(endpoint);
            credentials = cached.Credentials;
            http = cached.Http;
            redactor = cached.Redactor;
            owned = null;
        }

        private CachedModelEndpoint Cached(ModelEndpoint endpoint)
        {
            ObjectDisposedException.ThrowIf(_Disposed, this);
            string key = endpoint.PropertyId + "|" + endpoint.Id;
            if (_Cache.TryGetValue(key, out CachedModelEndpoint? existing) && existing.Version == endpoint.UpdatedUtc) return existing;
            EndpointCredentials credentials = Credentials(endpoint);
            CachedModelEndpoint fresh = new CachedModelEndpoint(endpoint.UpdatedUtc, BuildHttp(credentials), credentials, new AiSecretRedactor(credentials));
            CachedModelEndpoint stored = _Cache.AddOrUpdate(key, fresh, (k, old) =>
            {
                if (old.Version == endpoint.UpdatedUtc) return old;
                Retire(old);
                return fresh;
            });
            if (!ReferenceEquals(stored, fresh)) fresh.Http.Dispose();
            return stored;
        }

        private static HttpClient BuildHttp(EndpointCredentials credentials)
        {
            SocketsHttpHandler sockets = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                AllowAutoRedirect = false
            };
            EndpointAuthHandler auth = new EndpointAuthHandler(credentials, sockets);
            return new HttpClient(auth, true) { Timeout = Timeout.InfiniteTimeSpan };
        }

        private static void Retire(CachedModelEndpoint old)
        {
            // In-flight calls may still use the old client; dispose it once they have had time to finish.
            _ = Task.Delay(TimeSpan.FromMinutes(11)).ContinueWith(t => old.Http.Dispose(), TaskScheduler.Default);
        }

        private ModelClientLease<ProviderClientBase> Create(ModelEndpoint endpoint, EndpointCredentials? credentials, bool ignoreUrl)
        {
            HttpClient? owned;
            Resolve(endpoint, credentials, out HttpClient http, out AiSecretRedactor redactor, out EndpointCredentials creds, out owned);
            try
            {
                string? key = ApiKeyFor(creds);
                string? url = ignoreUrl ? null : UrlOf(endpoint);
                bool explicitCredentials = credentials != null;
                ClientOptions options = new ClientOptions
                {
                    Logging = _Silent,
                    HttpClient = http,
                    TimeoutMs = Math.Max(1000, endpoint.TimeoutMs),
                    RedactCredentials = true,
                    CallDetailCapture = _CaptureCallDetails() ? CallDetailCapture.Metadata : CallDetailCapture.None
                };
                // Providers that require a key at construction (PolyPrompt 4.0) get the stored key, the bearer value for
                // OpenAI-style bearer auth, or a placeholder that EndpointAuthHandler strips before applying the
                // endpoint's own authentication (header, query, Basic, or none).
                string required = key ?? (creds.AuthType == ModelAuthTypeEnum.Bearer && !String.IsNullOrEmpty(creds.AuthValue) && (endpoint.ApiType == ModelApiTypeEnum.OpenAI || endpoint.ApiType == ModelApiTypeEnum.XAI) ? creds.AuthValue : EndpointAuthHandler.PlaceholderKey);
                ProviderClientBase client;
                switch (endpoint.ApiType)
                {
                    case ModelApiTypeEnum.Ollama:
                        client = new OllamaClient(url ?? "http://localhost:11434", key, options);
                        break;
                    case ModelApiTypeEnum.OpenAI:
                        // PolyPrompt 4.0: OpenAI is the Responses API (/v1/responses).
                        client = new OpenAiClient(required, url, options);
                        break;
                    case ModelApiTypeEnum.OpenAICompatible:
                        // Chat Completions servers (vLLM, LM Studio, gateways); the 3.x OpenAiCompletionClient wire.
                        client = new OpenAiCompatibleClient(url ?? "https://api.openai.com/v1", key, options);
                        break;
                    case ModelApiTypeEnum.Gemini:
                        client = new GeminiClient(required, url, options);
                        break;
                    case ModelApiTypeEnum.Anthropic:
                        client = new AnthropicClient(required, url, options);
                        break;
                    case ModelApiTypeEnum.Cohere:
                        client = new CohereClient(required, url, options);
                        break;
                    case ModelApiTypeEnum.VoyageAI:
                        client = new VoyageAiClient(required, url, options);
                        break;
                    case ModelApiTypeEnum.Tei:
                        client = new TeiClient(RequireUrl(endpoint, url), key, options);
                        break;
                    case ModelApiTypeEnum.OllamaCloud:
                        client = new OllamaCloudClient(required, url, options);
                        break;
                    case ModelApiTypeEnum.XAI:
                        client = new XAiClient(required, url, options);
                        break;
                    case ModelApiTypeEnum.AzureOpenAI:
                        // PolyPrompt 4.0: the Azure v1 API (/openai/v1/responses) with the deployment as the model; no api-version.
                        client = creds.AuthType == ModelAuthTypeEnum.Bearer
                            ? new AzureOpenAiClient(RequireUrl(endpoint, url), TokenCredential(endpoint, creds, explicitCredentials), options)
                            : new AzureOpenAiClient(RequireUrl(endpoint, url), key ?? throw new NotSupportedException(endpoint.ApiType + " ApiKey auth needs authValue."), options);
                        break;
                    case ModelApiTypeEnum.VertexAI:
                        client = new VertexAiClient(Project(endpoint), Region(endpoint), GoogleCredential(endpoint, creds, explicitCredentials), url, options);
                        break;
                    case ModelApiTypeEnum.Bedrock:
                        client = new BedrockClient(Region(endpoint), AwsCredential(endpoint, creds, explicitCredentials), url, options);
                        break;
                    default:
                        throw new NotSupportedException(endpoint.ApiType + " has no client in this build.");
                }
                client.MaxCallDetails = _CaptureCallDetails() ? 20 : 0;
                return new ModelClientLease<ProviderClientBase>(client, redactor, owned);
            }
            catch
            {
                owned?.Dispose();
                throw;
            }
        }

        private static string? ApiKeyFor(EndpointCredentials credentials)
        {
            return credentials.AuthType == ModelAuthTypeEnum.ApiKey && !String.IsNullOrEmpty(credentials.AuthValue) ? credentials.AuthValue : null;
        }

        private static string? UrlOf(ModelEndpoint endpoint)
        {
            return String.IsNullOrWhiteSpace(endpoint.EndpointUrl) ? null : endpoint.EndpointUrl.Trim().TrimEnd('/');
        }

        private static string RequireUrl(ModelEndpoint endpoint, string? url)
        {
            if (url != null) return url;
            throw new NotSupportedException(endpoint.ApiType + " needs endpointUrl (the resource endpoint).");
        }

        private static string Deployment(ModelEndpoint endpoint)
        {
            return String.IsNullOrWhiteSpace(endpoint.Deployment) ? endpoint.Model : endpoint.Deployment.Trim();
        }

        private static string Project(ModelEndpoint endpoint)
        {
            return String.IsNullOrWhiteSpace(endpoint.Project) ? throw new NotSupportedException(endpoint.ApiType + " needs providerSettings.project.") : endpoint.Project.Trim();
        }

        private static string Region(ModelEndpoint endpoint)
        {
            return String.IsNullOrWhiteSpace(endpoint.Region) ? throw new NotSupportedException(endpoint.ApiType + " needs providerSettings.region.") : endpoint.Region.Trim();
        }

        private ICredentialProvider TokenCredential(ModelEndpoint endpoint, EndpointCredentials credentials, bool explicitCredentials)
        {
            if (String.IsNullOrEmpty(credentials.AuthValue)) throw new NotSupportedException(endpoint.ApiType + " Bearer auth needs authValue.");
            return Provider(endpoint, "token", explicitCredentials, () => new StaticTokenCredential(credentials.AuthValue));
        }

        private ICredentialProvider GoogleCredential(ModelEndpoint endpoint, EndpointCredentials credentials, bool explicitCredentials)
        {
            if (credentials.AuthType == ModelAuthTypeEnum.ApiKey)
            {
                if (String.IsNullOrEmpty(credentials.AuthValue)) throw new NotSupportedException(endpoint.ApiType + " ApiKey auth needs the service account JSON in authValue.");
                return Provider(endpoint, "google", explicitCredentials, () => ServiceAccountCredential.FromJson(credentials.AuthValue, null, null));
            }
            return Provider(endpoint, "google", explicitCredentials, () => new AdcCredential(null));
        }

        private IAwsCredentialProvider AwsCredential(ModelEndpoint endpoint, EndpointCredentials credentials, bool explicitCredentials)
        {
            string region = Region(endpoint);
            if (credentials.AuthType == ModelAuthTypeEnum.ApiKey)
            {
                if (String.IsNullOrEmpty(credentials.AuthValue) || String.IsNullOrWhiteSpace(endpoint.AccessKeyId)) throw new NotSupportedException(endpoint.ApiType + " ApiKey auth needs providerSettings.accessKeyId and the secret access key in authValue.");
                return Provider(endpoint, "aws", explicitCredentials, () => new StaticAwsCredential(endpoint.AccessKeyId.Trim(), credentials.AuthValue, region, null));
            }
            return Provider(endpoint, "aws", explicitCredentials, () => new EnvironmentAwsCredential(region));
        }

        private T Provider<T>(ModelEndpoint endpoint, string kind, bool explicitCredentials, Func<T> create) where T : class
        {
            // Explicit credentials (an unsaved test) get a fresh provider; saved endpoints reuse one per version so
            // OAuth tokens and resolved AWS credentials are cached across calls.
            if (explicitCredentials) return create();
            string key = endpoint.PropertyId + "|" + endpoint.Id + "|" + kind + "|" + endpoint.UpdatedUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
            object provider = _Providers.GetOrAdd(key, k =>
            {
                foreach (string stale in _Providers.Keys)
                {
                    if (stale.StartsWith(endpoint.PropertyId + "|" + endpoint.Id + "|" + kind + "|", StringComparison.Ordinal) && stale != k && _Providers.TryRemove(stale, out object? old)) RetireProvider(old);
                }
                return create();
            });
            return (T)provider;
        }

        private static void RetireProvider(object provider)
        {
            if (provider is not IDisposable disposable) return;
            _ = Task.Delay(TimeSpan.FromMinutes(11)).ContinueWith(t => disposable.Dispose(), TaskScheduler.Default);
        }

        #endregion
    }
}
