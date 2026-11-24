namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Integrations.Implementations;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Security;

    /// <summary>
    /// Model endpoints (docs/AI_CONTRACT.md sections 1 and 2): validation per apiType and authType, write-only secrets
    /// encrypted with the engine secret key, test and validate calls, provider model listing with a bounded cache,
    /// duplicate, audited reveal, and re-encryption with the current key. Every route is SystemAdmin only (the route
    /// group enforces it).
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class ModelEndpointService
    {
        #region Private-Members

        private static readonly Regex _HeaderToken = new Regex("^[!#$%&'*+.^_`|~0-9A-Za-z-]{1,100}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _QueryName = new Regex("^[A-Za-z0-9_.-]{1,100}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly HashSet<string> _ForbiddenHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Host", "Content-Length", "Content-Type", "Transfer-Encoding" };
        private static readonly HashSet<ModelApiTypeEnum> _HasDefaultUrl = new HashSet<ModelApiTypeEnum>
        {
            ModelApiTypeEnum.OpenAI, ModelApiTypeEnum.Gemini, ModelApiTypeEnum.Anthropic, ModelApiTypeEnum.Cohere, ModelApiTypeEnum.VoyageAI, ModelApiTypeEnum.XAI, ModelApiTypeEnum.OllamaCloud,
            ModelApiTypeEnum.VertexAI, ModelApiTypeEnum.Bedrock
        };

        private static readonly IReadOnlyList<string> _ProviderFields = new[] { "region", "project", "apiVersion", "deployment", "accessKeyId" };
        private static readonly Regex _ProviderValue = new Regex("^[A-Za-z0-9._:-]{1,200}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly AiServices _Ai;
        private readonly ConcurrentDictionary<string, ModelListSnapshot> _ModelLists = new ConcurrentDictionary<string, ModelListSnapshot>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="ai">AI services of the property.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public ModelEndpointService(AiServices ai)
        {
            _Ai = ai ?? throw new ArgumentNullException(nameof(ai));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Enumerate endpoints.
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">404 NotAvailable, 400 ValidationFailed.</exception>
        public async Task<EnumerationResult<ModelEndpointView>> EnumerateAsync(EnumerationQuery? query, CancellationToken token)
        {
            _Ai.EnsureAvailable();
            EnumerationRequest request = _Ai.App.Enumeration.Prepare(query, AiCollections.Endpoints, true);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            AiEnumerationFilter filter = new AiEnumerationFilter
            {
                Kind = request.Query.FilterEnum<ModelEndpointKindEnum>("kind", errors),
                ApiType = request.Query.FilterEnum<ModelApiTypeEnum>("apiType", errors),
                Enabled = request.Query.FilterBool("enabled", errors),
                HealthStatus = request.Query.FilterEnum<ModelHealthStatusEnum>("healthStatus", errors),
                AuthType = request.Query.FilterEnum<ModelAuthTypeEnum>("authType", errors),
                Scope = request.Query.FilterEnum<ModelEndpointScopeEnum>("scope", errors)
            };
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationResult<ModelEndpoint> page = await _Ai.App.Database.Ai.Endpoints.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Ai.App.Metadata.LoadAsync(TargetTypeEnum.ModelEndpoint, page.Objects.Select(o => o.Id), true, token).ConfigureAwait(false);
            List<ModelEndpointView> views = new List<ModelEndpointView>();
            bool uptime = page.Objects.Count <= 50;
            foreach (ModelEndpoint endpoint in page.Objects)
                views.Add(await ToViewAsync(endpoint, metadata.TryGetValue(endpoint.Id, out ObjectMetadata? m) ? m : null, false, uptime, token).ConfigureAwait(false));
            return page.With(views);
        }

        /// <summary>
        /// Read an endpoint with its usedBy preview.
        /// </summary>
        /// <param name="id">Endpoint id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ApiErrorException">404.</exception>
        public async Task<ModelEndpointView> GetAsync(string id, CancellationToken token)
        {
            ModelEndpoint endpoint = await RequireAsync(id, token).ConfigureAwait(false);
            ObjectMetadata metadata = await _Ai.App.Metadata.LoadOneAsync(TargetTypeEnum.ModelEndpoint, endpoint.Id, true, token).ConfigureAwait(false);
            return await ToViewAsync(endpoint, metadata, true, true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Read a live endpoint or throw 404.
        /// </summary>
        /// <param name="id">Endpoint id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Endpoint.</returns>
        /// <exception cref="ApiErrorException">404 NotAvailable or 404 NotFound.</exception>
        public async Task<ModelEndpoint> RequireAsync(string id, CancellationToken token)
        {
            _Ai.EnsureAvailable();
            if (String.IsNullOrWhiteSpace(id) || !id.StartsWith(AiConstants.EndpointIdPrefix, StringComparison.Ordinal)) throw ApiErrorException.NotFound("Endpoint");
            ModelEndpoint? endpoint = await _Ai.App.Database.Ai.Endpoints.ReadAsync(id, token).ConfigureAwait(false);
            return endpoint ?? throw ApiErrorException.NotFound("Endpoint");
        }

        /// <summary>
        /// Create an endpoint. Audited as ai.endpoint.create.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="request">Body.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 400 AuthTypeNotSupported, 409 Conflict.</exception>
        public async Task<ModelEndpointView> CreateAsync(ContentActor actor, ModelEndpointRequest request, RequestInfo info, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            _Ai.EnsureAvailable();
            DateTime now = DateTime.UtcNow;
            ModelEndpoint endpoint = new ModelEndpoint
            {
                PropertyId = _Ai.App.PropertyId,
                Id = IdGenerator.Generate(AiConstants.EndpointIdPrefix),
                CreatedByUserId = actor.UserId,
                CreatedUtc = now,
                UpdatedUtc = now
            };
            List<string> changed = new List<string>();
            Apply(endpoint, request, null, true, changed);
            await _Ai.App.Metadata.ValidateAsync(actor, request.Labels, request.Tags, true, token, TargetTypeEnum.ModelEndpoint).ConfigureAwait(false);
            endpoint.NextCheckUtc = now;
            try
            {
                await _Ai.App.Database.Ai.Endpoints.CreateAsync(endpoint, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw new ApiErrorException(409, "Conflict", "An endpoint with that name already exists.", new Dictionary<string, string> { { "name", "Already in use." } });
            }
            ObjectMetadata metadata = await _Ai.App.Metadata.ApplyAsync(actor, TargetTypeEnum.ModelEndpoint, endpoint.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            await _Ai.App.Audit.WriteAsync(info, "ai.endpoint.create", "ModelEndpoint", endpoint.Id, endpoint.Name + " (" + endpoint.ApiType + ", " + endpoint.Kind + ")", token).ConfigureAwait(false);
            return await ToViewAsync(endpoint, metadata, true, false, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Update an endpoint (partial; secrets per section 2.3). Audited as ai.endpoint.update with the changed field
        /// names only.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Endpoint id.</param>
        /// <param name="request">Body.</param>
        /// <param name="present">Top-level fields present in the body.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ApiErrorException">404, 400 ValidationFailed, 400 AuthTypeNotSupported, 409 Conflict.</exception>
        public async Task<ModelEndpointView> UpdateAsync(ContentActor actor, string id, ModelEndpointRequest request, HashSet<string> present, RequestInfo info, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(present);
            ModelEndpoint endpoint = await RequireAsync(id, token).ConfigureAwait(false);
            ModelEndpointKindEnum kind = endpoint.Kind;
            string oldHost = HostOf(endpoint.EndpointUrl);
            List<string> changed = new List<string>();
            Apply(endpoint, request, present, false, changed);
            if (endpoint.Kind != kind)
            {
                List<string> users = await _Ai.App.Database.Ai.Endpoints.ReferencingAssistantsAsync(endpoint.Id, 1, token).ConfigureAwait(false);
                if (users.Count > 0) throw ApiErrorException.Field("kind", "The kind cannot change while an assistant references the endpoint.");
            }
            await _Ai.App.Metadata.ValidateAsync(actor, request.Labels, request.Tags, true, token, TargetTypeEnum.ModelEndpoint).ConfigureAwait(false);
            endpoint.UpdatedUtc = DateTime.UtcNow;
            if (changed.Contains("enabled") || changed.Contains("healthCheck") || changed.Contains("endpointUrl") || changed.Contains("authValue")) endpoint.NextCheckUtc = endpoint.UpdatedUtc;
            try
            {
                await _Ai.App.Database.Ai.Endpoints.UpdateAsync(endpoint, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw new ApiErrorException(409, "Conflict", "An endpoint with that name already exists.", new Dictionary<string, string> { { "name", "Already in use." } });
            }
            _Ai.Factory.Evict(endpoint.PropertyId, endpoint.Id);
            _ModelLists.TryRemove(endpoint.Id, out ModelListSnapshot? _);
            ObjectMetadata metadata = await _Ai.App.Metadata.ApplyAsync(actor, TargetTypeEnum.ModelEndpoint, endpoint.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            if (request.Labels != null || request.Tags != null) changed.Add("labels/tags");
            string newHost = HostOf(endpoint.EndpointUrl);
            bool hostChanged = !String.Equals(oldHost, newHost, StringComparison.OrdinalIgnoreCase) && (endpoint.AuthValueSecret != null || endpoint.Headers.Any(h => h.ValueSecret != null));
            string summary = (changed.Count == 0 ? "no changes" : String.Join(", ", changed.Distinct())) + (hostChanged ? "; endpoint host changed with stored credentials" : String.Empty);
            await _Ai.App.Audit.WriteAsync(info, "ai.endpoint.update", "ModelEndpoint", endpoint.Id, summary, token).ConfigureAwait(false);
            return await ToViewAsync(endpoint, metadata, true, true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Delete an endpoint (health history and usage rollups are kept). Audited as ai.endpoint.delete.
        /// </summary>
        /// <param name="id">Endpoint id.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">404, 409 EndpointInUse.</exception>
        public async Task DeleteAsync(string id, RequestInfo info, CancellationToken token)
        {
            ModelEndpoint endpoint = await RequireAsync(id, token).ConfigureAwait(false);
            List<string> users = await _Ai.App.Database.Ai.Endpoints.ReferencingAssistantsAsync(endpoint.Id, 10, token).ConfigureAwait(false);
            if (users.Count > 0)
                throw new ApiErrorException(409, "EndpointInUse", "An assistant references this endpoint.", new Dictionary<string, string> { { "assistants", String.Join(",", users) } });
            await _Ai.App.Database.Ai.Endpoints.DeleteAsync(endpoint.Id, DateTime.UtcNow, token).ConfigureAwait(false);
            _Ai.Factory.Evict(endpoint.PropertyId, endpoint.Id);
            await _Ai.App.Metadata.ApplyAsync(new ContentActor { IsSystemAdmin = true, IsStaff = true }, TargetTypeEnum.ModelEndpoint, endpoint.Id, new List<string>(), new Dictionary<string, string>(), token).ConfigureAwait(false);
            await _Ai.App.Audit.WriteAsync(info, "ai.endpoint.delete", "ModelEndpoint", endpoint.Id, endpoint.Name, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Validate an unsaved endpoint body and run a test call against it (nothing is stored).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="request">Create body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Test result.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 400 AuthTypeNotSupported, 429.</exception>
        public async Task<EndpointTestResult> ValidateAsync(ContentActor actor, ModelEndpointRequest request, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            _Ai.EnsureAvailable();
            _Ai.App.Limits.EnforceMember(AdminBucket(actor), "ai-admin-tests", _Ai.App.Settings.Ai.AdminTestsPerHour, RateLimitService.Hour);
            ModelEndpoint endpoint = new ModelEndpoint { PropertyId = _Ai.App.PropertyId, Id = "mep_validate_" + Guid.NewGuid().ToString("N"), CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow };
            EndpointCredentials credentials = ApplyForValidation(endpoint, request);
            return await TestCoreAsync(endpoint, credentials, new EndpointTestRequest(), true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Test a saved endpoint. Audited as ai.endpoint.test.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Endpoint id.</param>
        /// <param name="request">Test inputs.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Test result.</returns>
        /// <exception cref="ApiErrorException">404, 400, 429.</exception>
        public async Task<EndpointTestResult> TestAsync(ContentActor actor, string id, EndpointTestRequest request, RequestInfo info, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ModelEndpoint endpoint = await RequireAsync(id, token).ConfigureAwait(false);
            _Ai.App.Limits.EnforceMember(AdminBucket(actor), "ai-admin-tests", _Ai.App.Settings.Ai.AdminTestsPerHour, RateLimitService.Hour);
            EndpointTestResult result = await TestCoreAsync(endpoint, null, request ?? new EndpointTestRequest(), false, token).ConfigureAwait(false);
            await _Ai.App.Audit.WriteAsync(info, "ai.endpoint.test", "ModelEndpoint", endpoint.Id, (result.Success ? "succeeded" : "failed") + " in " + result.LatencyMs + " ms", token).ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// Enumerate the provider's models (cached for ai.modelListCacheMinutes, at most ai.maxListedModels items).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Endpoint id.</param>
        /// <param name="query">Query (search over name; ordering NameAscending).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">404, 502 ModelError, 503 ModelUnavailable.</exception>
        public async Task<EnumerationResult<ProviderModelView>> EnumerateModelsAsync(ContentActor actor, string id, EnumerationQuery? query, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ModelEndpoint endpoint = await RequireAsync(id, token).ConfigureAwait(false);
            if (!_Ai.Factory.SupportsModelListing(endpoint.ApiType)) return PageOf(new List<ProviderModelView>(), query, id);
            if (!_ModelLists.TryGetValue(endpoint.Id, out ModelListSnapshot? snapshot) || snapshot.Version != endpoint.UpdatedUtc || snapshot.ExpiresUtc < DateTime.UtcNow)
            {
                _Ai.App.Limits.EnforceMember(AdminBucket(actor), "ai-admin-tests", _Ai.App.Settings.Ai.AdminTestsPerHour, RateLimitService.Hour);
                if (!endpoint.Enabled) throw AiErrors.ModelUnavailable();
                List<ProviderModelView> models = new List<ProviderModelView>();
                using (ModelClientLease<ProviderClientBase> lease = _Ai.Factory.CreateModelLister(endpoint))
                {
                    try
                    {
                        await foreach (ModelInformation info in lease.Client.ListModelsAsync(token).ConfigureAwait(false))
                        {
                            models.Add(new ProviderModelView
                            {
                                Name = info.Name,
                                DisplayName = info.DisplayName,
                                Description = info.Description,
                                OwnedBy = info.OwnedBy,
                                CreatedUtc = info.CreatedUtc,
                                SizeBytes = info.SizeBytes,
                                InputTokenLimit = info.InputTokenLimit,
                                OutputTokenLimit = info.OutputTokenLimit,
                                SupportsTools = info.SupportsTools
                            });
                            if (models.Count >= _Ai.App.Settings.Ai.MaxListedModels) break;
                        }
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception e) when (e is not OutOfMemoryException)
                    {
                        _Ai.App.Logger.Warn("[ModelEndpointService] model listing failed on " + endpoint.Id + ": " + lease.Redactor.Redact(e.Message));
                        throw AiErrors.ModelError();
                    }
                }
                snapshot = new ModelListSnapshot(endpoint.UpdatedUtc, DateTime.UtcNow.AddMinutes(_Ai.App.Settings.Ai.ModelListCacheMinutes), models.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList());
                _ModelLists[endpoint.Id] = snapshot;
            }
            return PageOf(snapshot.Models, query, id);
        }

        /// <summary>
        /// Duplicate an endpoint (disabled, health Unknown). Audited as ai.endpoint.duplicate.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Endpoint id.</param>
        /// <param name="request">Name and includeSecrets.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View of the copy.</returns>
        /// <exception cref="ApiErrorException">404, 400, 409.</exception>
        public async Task<ModelEndpointView> DuplicateAsync(ContentActor actor, string id, EndpointDuplicateRequest request, RequestInfo info, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ModelEndpoint source = await RequireAsync(id, token).ConfigureAwait(false);
            string name = String.IsNullOrWhiteSpace(request?.Name) ? "Copy of " + source.Name : request!.Name!.Trim();
            if (name.Length > 100) name = name.Substring(0, 100);
            EndpointCredentials? credentials = request != null && request.IncludeSecrets ? _Ai.Factory.Credentials(source) : null;
            DateTime now = DateTime.UtcNow;
            string json = JsonSerializer.Serialize(source);
            ModelEndpoint copy = JsonSerializer.Deserialize<ModelEndpoint>(json)!;
            copy.Id = IdGenerator.Generate(AiConstants.EndpointIdPrefix);
            copy.Name = name;
            copy.Enabled = false;
            copy.HealthStatus = ModelHealthStatusEnum.Unknown;
            copy.ConsecutiveFailures = 0;
            copy.ConsecutiveSuccesses = 0;
            copy.LastCheckedUtc = null;
            copy.LastError = null;
            copy.LastLatencyMs = null;
            copy.LastHealthyUtc = null;
            copy.LastUnhealthyUtc = null;
            copy.NextCheckUtc = now;
            copy.CreatedByUserId = actor.UserId;
            copy.CreatedUtc = now;
            copy.UpdatedUtc = now;
            copy.AuthValueSecret = credentials?.AuthValue == null ? null : Protect(copy, "authValue", credentials.AuthValue);
            foreach (ModelEndpointHeader header in copy.Headers)
            {
                string? value = credentials?.ExtraHeaders.FirstOrDefault(h => String.Equals(h.Key, header.Name, StringComparison.OrdinalIgnoreCase)).Value;
                header.ValueSecret = String.IsNullOrEmpty(value) ? null : Protect(copy, HeaderField(header.Name), value);
            }
            try
            {
                await _Ai.App.Database.Ai.Endpoints.CreateAsync(copy, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw new ApiErrorException(409, "Conflict", "An endpoint with that name already exists.", new Dictionary<string, string> { { "name", "Already in use." } });
            }
            await _Ai.App.Metadata.SnapshotAsync(TargetTypeEnum.ModelEndpoint, source.Id, TargetTypeEnum.ModelEndpoint, copy.Id, token).ConfigureAwait(false);
            await _Ai.App.Audit.WriteAsync(info, "ai.endpoint.duplicate", "ModelEndpoint", copy.Id, "from " + source.Id + (credentials != null ? " with secrets" : " without secrets"), token).ConfigureAwait(false);
            ObjectMetadata metadata = await _Ai.App.Metadata.LoadOneAsync(TargetTypeEnum.ModelEndpoint, copy.Id, true, token).ConfigureAwait(false);
            return await ToViewAsync(copy, metadata, true, false, token).ConfigureAwait(false);
        }

        /// <summary>
        /// The conditions every secret reveal shares (section 2.5), for endpoint secrets and harness credentials: AI is
        /// available, ai.allowSecretReveal is on, and the caller is an administrator signed in with their own account
        /// (never the admin token) within ai.revealRecentSignInMinutes.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">403 RevealNotAllowed, 404 NotAvailable.</exception>
        public async Task EnsureRevealAllowedAsync(ContentActor actor, RequestInfo info, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            _Ai.EnsureAvailable();
            if (!_Ai.App.Settings.Ai.AllowSecretReveal) throw new ApiErrorException(403, "RevealNotAllowed", "Revealing secrets is turned off (ai.allowSecretReveal).");
            if (actor.IsBreakGlass || String.IsNullOrEmpty(actor.UserId) || info.Context == null || info.Context.PrincipalKind != PrincipalKindEnum.Member || String.IsNullOrEmpty(info.Context.SessionId))
                throw new ApiErrorException(403, "RevealNotAllowed", "Revealing a secret needs an administrator signed in with their own account.");
            AuthSession? session = await _Ai.App.Database.Sessions.ReadAsync(info.Context.SessionId, token).ConfigureAwait(false);
            if (session == null || session.CreatedUtc < DateTime.UtcNow.AddMinutes(-_Ai.App.Settings.Ai.RevealRecentSignInMinutes))
                throw new ApiErrorException(403, "RevealNotAllowed", "Sign in again to reveal this secret (a sign-in within the last " + _Ai.App.Settings.Ai.RevealRecentSignInMinutes + " minutes is required).");
        }

        /// <summary>
        /// Reveal a stored secret (section 2.5): a SystemAdmin member session signed in within
        /// ai.revealRecentSignInMinutes, never the break-glass token or a delegated session; reason required; rate
        /// limited; audited as ai.endpoint.reveal without the value.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Endpoint id.</param>
        /// <param name="request">Field, header name, and reason.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The value.</returns>
        /// <exception cref="ApiErrorException">403 RevealNotAllowed, 404, 400, 429.</exception>
        public async Task<SecretRevealResponse> RevealAsync(ContentActor actor, string id, SecretRevealRequest request, RequestInfo info, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(request);
            await EnsureRevealAllowedAsync(actor, info, token).ConfigureAwait(false);
            ModelEndpoint endpoint = await RequireAsync(id, token).ConfigureAwait(false);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string reason = (request.Reason ?? String.Empty).Trim();
            if (reason.Length < 10 || reason.Length > 500) errors["reason"] = "Must be 10 to 500 characters.";
            if (request.Field != "authValue" && request.Field != "extraHeader") errors["field"] = "Must be authValue or extraHeader.";
            if (request.Field == "extraHeader" && String.IsNullOrWhiteSpace(request.HeaderName)) errors["headerName"] = "Required for extraHeader.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            _Ai.App.Limits.EnforceMember(actor, "ai-reveals", _Ai.App.Settings.Ai.RevealsPerHour, RateLimitService.Hour);
            EndpointCredentials credentials = _Ai.Factory.Credentials(endpoint);
            string? value;
            string? headerName = null;
            if (request.Field == "authValue")
            {
                value = credentials.AuthValue;
            }
            else
            {
                KeyValuePair<string, string> header = credentials.ExtraHeaders.FirstOrDefault(h => String.Equals(h.Key, request.HeaderName!.Trim(), StringComparison.OrdinalIgnoreCase));
                headerName = header.Key;
                value = String.IsNullOrEmpty(header.Value) ? null : header.Value;
            }
            if (value == null) throw ApiErrorException.NotFound("Secret");
            TrellisKitTelemetry.Add(AiTelemetry.SecretReveals, 1);
            await _Ai.App.Audit.WriteAsync(info, "ai.endpoint.reveal", "ModelEndpoint", endpoint.Id, request.Field + (headerName != null ? " " + headerName : String.Empty) + "; reason: " + reason, token).ConfigureAwait(false);
            _Ai.App.Logger.Info("[ModelEndpointService] " + actor.UserId + " revealed " + request.Field + " of endpoint " + endpoint.Id);
            return new SecretRevealResponse { Field = request.Field!, HeaderName = headerName, Value = value };
        }

        /// <summary>
        /// Enumerate the assistants that reference an endpoint.
        /// </summary>
        /// <param name="id">Endpoint id.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        public async Task<EnumerationResult<EndpointUsageItem>> EnumerateUsageAsync(string id, EnumerationQuery? query, CancellationToken token)
        {
            ModelEndpoint endpoint = await RequireAsync(id, token).ConfigureAwait(false);
            EnumerationRequest request = _Ai.App.Enumeration.Prepare(query, AiCollections.EndpointUsage, true, endpoint.Id);
            return await _Ai.App.Database.Ai.Endpoints.EnumerateUsageAsync(request, endpoint.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Rewrite every stored secret with the current key. Audited as ai.endpoint.reencrypt.
        /// </summary>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Counts.</returns>
        /// <exception cref="ApiErrorException">400 when no key is configured.</exception>
        public async Task<ReencryptResponse> ReencryptAsync(RequestInfo info, CancellationToken token)
        {
            _Ai.EnsureAvailable();
            if (!_Ai.Protector.IsConfigured) throw ApiErrorException.Field("secrets.encryptionKey", "Set secrets.encryptionKey (TK_SECRETS_ENCRYPTION_KEY) first.");
            int endpoints = 0;
            int secrets = 0;
            string? after = null;
            while (true)
            {
                List<ModelEndpoint> page = await _Ai.App.Database.Ai.Endpoints.ListPageAsync(200, after, token).ConfigureAwait(false);
                if (page.Count == 0) break;
                foreach (ModelEndpoint endpoint in page)
                {
                    bool rotate = _Ai.Protector.NeedsRotation(endpoint.AuthValueSecret) || endpoint.Headers.Any(h => _Ai.Protector.NeedsRotation(h.ValueSecret));
                    if (!rotate) continue;
                    EndpointCredentials credentials = _Ai.Factory.Credentials(endpoint);
                    if (credentials.AuthValue != null)
                    {
                        endpoint.AuthValueSecret = Protect(endpoint, "authValue", credentials.AuthValue);
                        secrets++;
                    }
                    foreach (ModelEndpointHeader header in endpoint.Headers)
                    {
                        string value = credentials.ExtraHeaders.FirstOrDefault(h => String.Equals(h.Key, header.Name, StringComparison.OrdinalIgnoreCase)).Value ?? String.Empty;
                        if (value.Length == 0) continue;
                        header.ValueSecret = Protect(endpoint, HeaderField(header.Name), value);
                        secrets++;
                    }
                    await _Ai.App.Database.Ai.Endpoints.UpdateAsync(endpoint, token).ConfigureAwait(false);
                    _Ai.Factory.Evict(endpoint.PropertyId, endpoint.Id);
                    endpoints++;
                }
                after = page[page.Count - 1].Id;
            }
            secrets += await _Ai.HarnessProfiles.ReencryptAsync(token).ConfigureAwait(false);
            await _Ai.App.Audit.WriteAsync(info, "ai.endpoint.reencrypt", "ModelEndpoint", null, endpoints + " endpoints, " + secrets + " secrets, key " + _Ai.Protector.CurrentKeyId, token).ConfigureAwait(false);
            return new ReencryptResponse { Endpoints = endpoints, Secrets = secrets, KeyId = _Ai.Protector.CurrentKeyId };
        }

        /// <summary>
        /// Build the admin view.
        /// </summary>
        /// <param name="endpoint">Endpoint.</param>
        /// <param name="metadata">Labels and tags, or null.</param>
        /// <param name="usedBy">Include the usedBy preview.</param>
        /// <param name="uptime">Compute the uptime percentage.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        public async Task<ModelEndpointView> ToViewAsync(ModelEndpoint endpoint, ObjectMetadata? metadata, bool usedBy, bool uptime, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            ModelEndpointView view = new ModelEndpointView
            {
                Id = endpoint.Id,
                Name = endpoint.Name,
                Description = endpoint.Description,
                Enabled = endpoint.Enabled,
                Kind = endpoint.Kind,
                ApiType = endpoint.ApiType,
                Model = endpoint.Model,
                EndpointUrl = endpoint.EndpointUrl,
                AuthType = endpoint.AuthType,
                AuthHeaderName = endpoint.AuthHeaderName,
                AuthQueryParameter = endpoint.AuthQueryParameter,
                AuthUsername = endpoint.AuthUsername,
                HasAuthValue = !String.IsNullOrEmpty(endpoint.AuthValueSecret),
                ExtraHeaders = endpoint.Headers.Select(h => new EndpointHeaderView { Name = h.Name, HasValue = !String.IsNullOrEmpty(h.ValueSecret) }).ToList(),
                ProviderSettings = new EndpointProviderSettingsView { Region = endpoint.Region, Project = endpoint.Project, ApiVersion = endpoint.ApiVersion, Deployment = endpoint.Deployment, AccessKeyId = endpoint.AccessKeyId },
                Parameters = new EndpointParametersView
                {
                    Temperature = endpoint.Temperature,
                    TopP = endpoint.TopP,
                    MaxTokens = endpoint.MaxTokens,
                    ContextWindow = endpoint.ContextWindow,
                    ReasoningEffort = endpoint.ReasoningEffort,
                    Streaming = endpoint.Streaming,
                    ToolCalling = endpoint.ToolCalling,
                    TimeoutMs = endpoint.TimeoutMs,
                    FirstResponseTimeoutMs = endpoint.FirstResponseTimeoutMs,
                    Dimensions = endpoint.Dimensions
                },
                Limits = new EndpointLimitsView { MaxConcurrentRequests = endpoint.MaxConcurrentRequests, MaxQueueDepth = endpoint.MaxQueueDepth, MaxRetries = endpoint.MaxRetries },
                HealthCheck = new EndpointHealthCheckView
                {
                    Enabled = endpoint.HealthCheckEnabled,
                    Mode = endpoint.HealthCheckMode,
                    IntervalMs = endpoint.HealthCheckIntervalMs,
                    TimeoutMs = endpoint.HealthCheckTimeoutMs,
                    HealthyThreshold = endpoint.HealthyThreshold,
                    UnhealthyThreshold = endpoint.UnhealthyThreshold
                },
                Health = await _Ai.Health.HealthViewAsync(endpoint, uptime, token).ConfigureAwait(false),
                Pricing = new EndpointPricingView { InputPerMillionTokens = endpoint.InputPricePerMillion, OutputPerMillionTokens = endpoint.OutputPricePerMillion, CacheReadPerMillionTokens = endpoint.CacheReadPricePerMillion, CacheWritePerMillionTokens = endpoint.CacheWritePricePerMillion },
                Scope = endpoint.Scope,
                Labels = metadata?.Labels ?? new List<string>(),
                Tags = metadata?.Tags ?? new Dictionary<string, string>(),
                CreatedUtc = endpoint.CreatedUtc,
                UpdatedUtc = endpoint.UpdatedUtc
            };
            if (usedBy)
            {
                EnumerationRequest request = _Ai.App.Enumeration.Prepare(new EnumerationQuery { MaxResults = AiConstants.UsedByPreview }, AiCollections.EndpointUsage, true, endpoint.Id);
                EnumerationResult<EndpointUsageItem> page = await _Ai.App.Database.Ai.Endpoints.EnumerateUsageAsync(request, endpoint.Id, token).ConfigureAwait(false);
                view.UsedBy = new EndpointUsedByView
                {
                    Objects = page.Objects,
                    TotalRecords = page.TotalRecords ?? page.Objects.Count,
                    Enumerate = Constants.ApiPrefix + "/admin/ai/endpoints/" + endpoint.Id + "/usage/enumerate"
                };
            }
            return view;
        }

        #endregion

        #region Private-Methods

        private static ContentActor AdminBucket(ContentActor actor)
        {
            // Admin test limits apply to SystemAdmin too (the member limiter exempts them), keyed by the admin.
            return new ContentActor { UserId = actor.UserId ?? "admin-token", IsSystemAdmin = false };
        }

        private async Task<EndpointTestResult> TestCoreAsync(ModelEndpoint endpoint, EndpointCredentials? credentials, EndpointTestRequest request, bool validation, CancellationToken token)
        {
            EndpointTestResult result = new EndpointTestResult { Kind = endpoint.Kind, Model = endpoint.Model };
            Stopwatch watch = Stopwatch.StartNew();
            AiSecretRedactor redactor = AiSecretRedactor.None;
            AiCallDetail detail = new AiCallDetail { Retries = 0, Streamed = false };
            TokenUsage? reported = null;
            // The probe follows this asynchronous flow only, so it needs no restore when the method ends.
            ModelCallProbe probe = ModelCallProbe.Begin();
            try
            {
                switch (endpoint.Kind)
                {
                    case ModelEndpointKindEnum.Embedding:
                        using (ModelClientLease<ProviderClientBase> lease = _Ai.Factory.CreateEmbedding(endpoint, credentials))
                        {
                            redactor = lease.Redactor;
                            List<string> input = (request.Input ?? new List<string>()).Where(i => !String.IsNullOrWhiteSpace(i)).Take(10).ToList();
                            if (input.Count == 0) input.Add("ping");
                            EmbeddingResponse response = await lease.Client.EmbedAsync(input, new EmbeddingOptions { Model = endpoint.CallModel }, token).ConfigureAwait(false);
                            result.Success = response.Success && response.Embeddings.Count > 0;
                            result.StatusCode = response.StatusCode;
                            result.Error = response.Success ? null : redactor.Redact(response.Error);
                            if (response.Embeddings.Count > 0)
                                result.Embedding = new EmbeddingTestView { Dimensions = response.Embeddings[0].Embedding.Length, Preview = response.Embeddings[0].Embedding.Take(8).ToList() };
                            int tokens = AiCallDetails.EmbeddingTokens(response) ?? 0;
                            result.Usage = new AiTokenUsageView { PromptTokens = tokens, TotalTokens = tokens };
                            reported = response.Usage ?? new TokenUsage { PromptTokens = tokens, TotalTokens = tokens };
                            detail.ProviderModel = response.Model;
                        }
                        break;
                    case ModelEndpointKindEnum.Rerank:
                        using (ModelClientLease<ProviderClientBase> lease = _Ai.Factory.CreateRerank(endpoint, credentials))
                        {
                            redactor = lease.Redactor;
                            List<string> documents = (request.Documents ?? new List<string>()).Where(d => !String.IsNullOrWhiteSpace(d)).Take(10).ToList();
                            if (documents.Count == 0) documents = new List<string> { "A community for members.", "The weather is sunny." };
                            string query = String.IsNullOrWhiteSpace(request.Query) ? "community" : request.Query.Trim();
                            RerankResponse response = await lease.Client.RerankAsync(query, documents, null, token).ConfigureAwait(false);
                            result.Success = response.Success;
                            result.StatusCode = response.StatusCode;
                            result.Error = response.Success ? null : redactor.Redact(response.Error);
                            result.Rerank = new RerankTestView { Scores = response.Results.OrderByDescending(r => r.Score).Take(10).Select(r => new RerankScoreView { Index = r.Index, Score = r.Score }).ToList() };
                            reported = new TokenUsage { PromptTokens = response.TotalTokens, TotalTokens = response.TotalTokens };
                            detail.ResponseId = response.ResponseId;
                            detail.ProviderModel = response.Model;
                        }
                        break;
                    default:
                        using (ModelClientLease<ProviderClientBase> lease = _Ai.Factory.CreateCompletion(endpoint, credentials))
                        {
                            redactor = lease.Redactor;
                            string prompt = String.IsNullOrWhiteSpace(request.Prompt) ? "Say hello in five words." : request.Prompt.Trim();
                            if (prompt.Length > 2000) prompt = prompt.Substring(0, 2000);
                            int maxTokens = Math.Clamp(request.MaxTokens ?? AiConstants.DefaultTestMaxTokens, 1, AiConstants.MaxTestMaxTokens);
                            result.MaxTokens = maxTokens;
                            PolyPrompt.Models.CompletionOptions options = ModelInvoker.OptionsFor(endpoint, maxTokens, null, null, _Ai.App.Settings.Ai.DefaultContextWindow);
                            if (endpoint.Streaming)
                            {
                                ChatStreamingResponse response = await lease.Client.ChatStreamingAsync(prompt, options, token).ConfigureAwait(false);
                                result.StatusCode = response.StatusCode;
                                detail.Streamed = true;
                                detail.ReasoningEffort = response.AppliedReasoningEffort;
                                if (!response.Success)
                                {
                                    result.Error = redactor.Redact(response.Error);
                                }
                                else
                                {
                                    System.Text.StringBuilder text = new System.Text.StringBuilder();
                                    System.Text.StringBuilder reasoning = new System.Text.StringBuilder();
                                    string? finish = null;
                                    await foreach (ChatStreamingChunk chunk in response.Chunks.WithCancellation(token).ConfigureAwait(false))
                                    {
                                        if (result.TimeToFirstTokenMs == null && (!String.IsNullOrEmpty(chunk.Text) || !String.IsNullOrEmpty(chunk.ReasoningText))) result.TimeToFirstTokenMs = watch.ElapsedMilliseconds;
                                        if (!String.IsNullOrEmpty(chunk.Text)) text.Append(chunk.Text);
                                        if (!String.IsNullOrEmpty(chunk.ReasoningText) && reasoning.Length <= AiConstants.MaxTestOutputCharacters) reasoning.Append(chunk.ReasoningText);
                                        if (!String.IsNullOrEmpty(chunk.FinishReason)) finish = chunk.FinishReason;
                                        if (text.Length > AiConstants.MaxTestOutputCharacters) break;
                                    }
                                    result.Success = response.State != StreamingState.Failed;
                                    if (!result.Success) result.Error = redactor.Redact(response.StreamError);
                                    result.Output = Cut(text.ToString(), AiConstants.MaxTestOutputCharacters);
                                    result.Reasoning = reasoning.Length == 0 ? Cut(response.Reasoning, AiConstants.MaxTestOutputCharacters) : Cut(reasoning.ToString(), AiConstants.MaxTestOutputCharacters);
                                    result.FinishReason = response.FinishReason ?? finish;
                                    result.Usage = Usage(response.Usage);
                                    reported = response.Usage;
                                    detail.Chunks = response.ChunkCount;
                                    detail.ResponseId = response.ResponseId;
                                    detail.ProviderModel = response.Model;
                                    detail.TimeToLastTokenMs = response.TimeToLastTokenMs >= 0 ? response.TimeToLastTokenMs : watch.ElapsedMilliseconds;
                                    detail.TokensPerSecond = response.OverallTokensPerSecond > 0 ? response.OverallTokensPerSecond : null;
                                }
                            }
                            else
                            {
                                ChatResponse response = await lease.Client.ChatAsync(prompt, options, token).ConfigureAwait(false);
                                result.Success = response.Success;
                                result.StatusCode = response.StatusCode;
                                result.Error = response.Success ? null : redactor.Redact(response.Error);
                                result.Output = Cut(response.Text, AiConstants.MaxTestOutputCharacters);
                                result.Reasoning = Cut(response.Reasoning, AiConstants.MaxTestOutputCharacters);
                                result.FinishReason = response.FinishReason;
                                result.Usage = Usage(response.Usage);
                                reported = response.Usage;
                                detail.ResponseId = response.ResponseId;
                                detail.ProviderModel = response.Model;
                                detail.ReasoningEffort = response.AppliedReasoningEffort;
                                if (response.Success) detail.TimeToLastTokenMs = watch.ElapsedMilliseconds;
                            }
                            ExplainCompletion(result, maxTokens);
                        }
                        break;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (CryptographicException)
            {
                result.Success = false;
                result.Error = "A stored secret cannot be decrypted with the configured keys.";
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                result.Success = false;
                result.Error = redactor.Redact(e.Message);
            }
            result.LatencyMs = watch.ElapsedMilliseconds;
            AiCallDetails.ApplyProbe(detail, probe);
            probe.Dispose();
            await LedgerTestAsync(endpoint, result, detail, reported, !validation).ConfigureAwait(false);
            if (!validation) TrellisKitTelemetry.Add(AiTelemetry.Requests, 1, "purpose", "test", "api_type", AiTelemetry.Label(endpoint.ApiType.ToString()), "outcome", result.Success ? "ok" : "error", "fallback", "false", "test", "true");
            return result;
        }

        private async Task LedgerTestAsync(ModelEndpoint endpoint, EndpointTestResult result, AiCallDetail detail, TokenUsage? usage, bool saved)
        {
            // docs/AI_TELEMETRY.md: an administrator's test or validation is a model interaction too. It gets a ledger row
            // (purpose EndpointTest, test traffic) with the cost the endpoint's prices give, and no usage rollup.
            int prompt = Math.Max(0, usage?.PromptTokens ?? 0);
            int completion = Math.Max(0, usage?.CompletionTokens ?? 0);
            int reasoning = Math.Max(0, usage?.ReasoningTokens ?? 0);
            AiCallDetails.ApplyUsage(detail, usage);
            detail.StatusCode = result.StatusCode;
            detail.FinishReason = result.FinishReason;
            detail.TimeToFirstTokenMs = result.TimeToFirstTokenMs;
            if (detail.TimeToFirstTokenMs != null && detail.TimeToLastTokenMs != null) detail.StreamingMs = Math.Max(0, detail.TimeToLastTokenMs.Value - detail.TimeToFirstTokenMs.Value);
            detail.TokensPerSecond ??= AiCallDetails.TokensPerSecond(completion + reasoning, detail.EvalMs, result.Success ? result.LatencyMs : null);
            await _Ai.Usage.RecordDirectAsync(new AiUsageRecord
            {
                Purpose = AssistantPurposeEnum.EndpointTest,
                Outcome = result.Success ? AiUsageOutcomeEnum.Ok : AiUsageOutcomeEnum.Error,
                PromptTokens = prompt,
                CompletionTokens = completion,
                ReasoningTokens = reasoning,
                LatencyMs = result.LatencyMs,
                TimeToFirstTokenMs = result.TimeToFirstTokenMs,
                CostMicros = ModelInvoker.Cost(endpoint, prompt, completion + reasoning, detail.CachedReadTokens, detail.CacheCreationTokens),
                OccurredUtc = DateTime.UtcNow,
                Detail = detail
            }, endpoint, result.Success ? null : "ModelError", saved).ConfigureAwait(false);
        }

        private static void ExplainCompletion(EndpointTestResult result, int maxTokens)
        {
            if (String.IsNullOrWhiteSpace(result.Reasoning)) result.Reasoning = null;
            string finish = (result.FinishReason ?? String.Empty).Trim().ToLowerInvariant();
            int completion = result.Usage == null ? 0 : result.Usage.CompletionTokens + result.Usage.ReasoningTokens;
            result.Truncated = finish == "length" || finish.Contains("max_tokens", StringComparison.Ordinal) || finish.Contains("max_output_tokens", StringComparison.Ordinal) || finish == "max"
                || (String.IsNullOrEmpty(finish) && completion >= maxTokens);
            if (!result.Success) return;
            bool empty = String.IsNullOrWhiteSpace(result.Output);
            bool reasoned = (result.Usage != null && result.Usage.ReasoningTokens > 0) || result.Reasoning != null;
            if (empty && (reasoned || result.Truncated)) result.Hint = "ReasoningConsumedBudget";
            else if (empty) result.Hint = "EmptyOutput";
            else if (result.Truncated) result.Hint = "Truncated";
        }

        private static AiTokenUsageView? Usage(TokenUsage? usage)
        {
            if (usage == null) return null;
            int prompt = usage.PromptTokens ?? 0;
            int completion = usage.CompletionTokens ?? 0;
            int reasoning = usage.ReasoningTokens ?? 0;
            return new AiTokenUsageView { PromptTokens = prompt, CompletionTokens = completion, ReasoningTokens = reasoning, TotalTokens = usage.TotalTokens ?? prompt + completion + reasoning };
        }

        private static string? Cut(string? text, int max)
        {
            if (text == null) return null;
            return text.Length <= max ? text : text.Substring(0, max);
        }

        private EndpointCredentials ApplyForValidation(ModelEndpoint endpoint, ModelEndpointRequest request)
        {
            List<string> changed = new List<string>();
            string? authValue = request.AuthValue;
            List<EndpointHeaderRequest>? headers = request.ExtraHeaders;
            ModelEndpointRequest copy = new ModelEndpointRequest
            {
                Name = String.IsNullOrWhiteSpace(request.Name) ? "validation" : request.Name,
                Kind = request.Kind,
                ApiType = request.ApiType,
                Model = request.Model,
                EndpointUrl = request.EndpointUrl,
                AuthType = request.AuthType,
                AuthHeaderName = request.AuthHeaderName,
                AuthQueryParameter = request.AuthQueryParameter,
                AuthUsername = request.AuthUsername,
                ProviderSettings = request.ProviderSettings,
                Parameters = request.Parameters,
                Limits = request.Limits,
                HealthCheck = request.HealthCheck,
                Pricing = request.Pricing
            };
            ApplyCore(endpoint, copy, null, true, changed, false);
            EndpointCredentials credentials = new EndpointCredentials
            {
                AuthType = endpoint.AuthType,
                HeaderName = endpoint.AuthHeaderName,
                QueryParameter = endpoint.AuthQueryParameter,
                Username = endpoint.AuthUsername,
                AuthValue = endpoint.AuthType == ModelAuthTypeEnum.None || String.IsNullOrEmpty(authValue) ? null : authValue
            };
            Dictionary<string, string> errors = new Dictionary<string, string>();
            foreach (EndpointHeaderRequest header in ValidateHeaders(endpoint, headers, errors))
                credentials.ExtraHeaders.Add(new KeyValuePair<string, string>(header.Name!.Trim(), header.Value ?? String.Empty));
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            return credentials;
        }

        private void Apply(ModelEndpoint endpoint, ModelEndpointRequest request, HashSet<string>? present, bool isCreate, List<string> changed)
        {
            ApplyCore(endpoint, request, present, isCreate, changed, true);
        }

        private void ApplyCore(ModelEndpoint e, ModelEndpointRequest r, HashSet<string>? present, bool isCreate, List<string> changed, bool storeSecrets)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            bool Has(string name)
            {
                return present == null ? true : present.Contains(name);
            }

            if (isCreate || (Has("name") && r.Name != null))
            {
                string name = (r.Name ?? String.Empty).Trim();
                if (name.Length < 1 || name.Length > 100) errors["name"] = "Must be 1 to 100 characters.";
                else if (name != e.Name) { e.Name = name; changed.Add("name"); }
            }
            if (isCreate || Has("description"))
            {
                string? description = String.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim();
                if (description != null && description.Length > 1000) errors["description"] = "Must be at most 1000 characters.";
                else if (description != e.Description) { e.Description = description; changed.Add("description"); }
            }
            if (r.Enabled != null && r.Enabled.Value != e.Enabled) { e.Enabled = r.Enabled.Value; changed.Add("enabled"); }
            if (isCreate && r.Kind == null) errors["kind"] = "Required.";
            if (r.Kind != null && r.Kind.Value != e.Kind) { e.Kind = r.Kind.Value; changed.Add("kind"); }
            if (isCreate && r.ApiType == null) errors["apiType"] = "Required.";
            if (r.ApiType != null && r.ApiType.Value != e.ApiType) { e.ApiType = r.ApiType.Value; changed.Add("apiType"); }
            if (!ApiTypeEnabled(e.ApiType)) errors["apiType"] = "Not enabled in this deployment (ai.enabledApiTypes).";
            else if (!_Ai.Factory.Supports(e.ApiType, e.Kind)) errors["kind"] = e.ApiType + " does not support " + e.Kind + " in this release.";
            if (isCreate || (Has("model") && r.Model != null))
            {
                string model = (r.Model ?? String.Empty).Trim();
                if (model.Length < 1 || model.Length > 200) errors["model"] = "Must be 1 to 200 characters.";
                else if (model != e.Model) { e.Model = model; changed.Add("model"); }
            }
            if (isCreate || Has("endpointUrl"))
            {
                string url = (r.EndpointUrl ?? String.Empty).Trim();
                string? problem = UrlProblem(url, e.ApiType);
                if (problem != null) errors["endpointUrl"] = problem;
                else if (url != e.EndpointUrl) { e.EndpointUrl = url; changed.Add("endpointUrl"); }
            }
            else if (UrlProblem(e.EndpointUrl, e.ApiType) is string urlProblem)
            {
                errors["endpointUrl"] = urlProblem;
            }
            ModelAuthTypeEnum previousAuth = e.AuthType;
            if (r.AuthType != null && r.AuthType.Value != e.AuthType) { e.AuthType = r.AuthType.Value; changed.Add("authType"); }
            if (isCreate || Has("authHeaderName")) e.AuthHeaderName = String.IsNullOrWhiteSpace(r.AuthHeaderName) ? null : r.AuthHeaderName.Trim();
            if (isCreate || Has("authQueryParameter")) e.AuthQueryParameter = String.IsNullOrWhiteSpace(r.AuthQueryParameter) ? null : r.AuthQueryParameter.Trim();
            if (isCreate || Has("authUsername")) e.AuthUsername = String.IsNullOrWhiteSpace(r.AuthUsername) ? null : r.AuthUsername.Trim();
            if (e.AuthType != ModelAuthTypeEnum.Header) e.AuthHeaderName = null;
            if (e.AuthType != ModelAuthTypeEnum.QueryParameter) e.AuthQueryParameter = null;
            if (e.AuthType != ModelAuthTypeEnum.Basic) e.AuthUsername = null;
            if (e.AuthType == ModelAuthTypeEnum.Header)
            {
                if (e.AuthHeaderName == null || !_HeaderToken.IsMatch(e.AuthHeaderName) || _ForbiddenHeaders.Contains(e.AuthHeaderName)) errors["authHeaderName"] = "Required: 1 to 100 header token characters, not Host, Content-Length, Content-Type, or Transfer-Encoding.";
            }
            if (e.AuthType == ModelAuthTypeEnum.QueryParameter && (e.AuthQueryParameter == null || !_QueryName.IsMatch(e.AuthQueryParameter))) errors["authQueryParameter"] = "Required: 1 to 100 of A-Z a-z 0-9 _ - .";
            if (e.AuthType == ModelAuthTypeEnum.Basic && (e.AuthUsername == null || e.AuthUsername.Length > 200)) errors["authUsername"] = "Required: 1 to 200 characters.";
            ApplyNested(e, r, errors, changed);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            if (!_Ai.Factory.IsAuthTypeAllowed(e.ApiType, e.AuthType)) throw AiErrors.AuthTypeNotSupported(e.ApiType.ToString());
            if (!storeSecrets) return;

            if (e.AuthType == ModelAuthTypeEnum.None)
            {
                if (e.AuthValueSecret != null) { e.AuthValueSecret = null; changed.Add("authValue changed"); }
            }
            else if (isCreate ? r.AuthValue != null : Has("authValue"))
            {
                if (String.IsNullOrEmpty(r.AuthValue))
                {
                    if (e.AuthValueSecret != null) changed.Add("authValue changed");
                    e.AuthValueSecret = null;
                }
                else
                {
                    if (r.AuthValue.Length > 16384) throw ApiErrorException.Field("authValue", "Must be at most 16384 characters.");
                    e.AuthValueSecret = Protect(e, "authValue", r.AuthValue);
                    changed.Add("authValue changed");
                }
            }
            if (isCreate ? r.ExtraHeaders != null : Has("extraHeaders"))
            {
                Dictionary<string, string> headerErrors = new Dictionary<string, string>();
                List<EndpointHeaderRequest> headers = ValidateHeaders(e, r.ExtraHeaders, headerErrors);
                if (headerErrors.Count > 0) throw ApiErrorException.Validation(headerErrors);
                List<ModelEndpointHeader> next = new List<ModelEndpointHeader>();
                foreach (EndpointHeaderRequest header in headers)
                {
                    string name = header.Name!.Trim();
                    ModelEndpointHeader? existing = e.Headers.FirstOrDefault(h => String.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase));
                    string? secret = existing?.ValueSecret;
                    if (header.Value != null)
                    {
                        secret = header.Value.Length == 0 ? null : Protect(e, HeaderField(name), header.Value);
                        changed.Add("extraHeaders[" + name + "] changed");
                    }
                    else if (existing == null)
                    {
                        changed.Add("extraHeaders[" + name + "] added");
                    }
                    next.Add(new ModelEndpointHeader { Name = name, ValueSecret = secret, Position = next.Count });
                }
                foreach (ModelEndpointHeader removed in e.Headers.Where(h => !next.Any(n => String.Equals(n.Name, h.Name, StringComparison.OrdinalIgnoreCase)))) changed.Add("extraHeaders[" + removed.Name + "] removed");
                e.Headers = next;
            }
            if (previousAuth != e.AuthType && e.AuthType == ModelAuthTypeEnum.None) e.AuthValueSecret = null;
        }

        private List<EndpointHeaderRequest> ValidateHeaders(ModelEndpoint e, List<EndpointHeaderRequest>? headers, Dictionary<string, string> errors)
        {
            List<EndpointHeaderRequest> list = headers ?? new List<EndpointHeaderRequest>();
            if (list.Count > _Ai.App.Settings.Ai.MaxExtraHeaders) errors["extraHeaders"] = "At most " + _Ai.App.Settings.Ai.MaxExtraHeaders + " headers.";
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? authHeader = e.AuthType == ModelAuthTypeEnum.Header ? e.AuthHeaderName : e.AuthType == ModelAuthTypeEnum.Bearer || e.AuthType == ModelAuthTypeEnum.Basic ? "Authorization" : null;
            for (int i = 0; i < list.Count; i++)
            {
                string name = (list[i].Name ?? String.Empty).Trim();
                if (!_HeaderToken.IsMatch(name) || _ForbiddenHeaders.Contains(name)) errors["extraHeaders[" + i + "].name"] = "1 to 100 header token characters; not Host, Content-Length, Content-Type, or Transfer-Encoding.";
                else if (!seen.Add(name)) errors["extraHeaders[" + i + "].name"] = "Duplicate header.";
                else if (authHeader != null && String.Equals(authHeader, name, StringComparison.OrdinalIgnoreCase)) errors["extraHeaders[" + i + "].name"] = "Collides with the header the auth type sets.";
                if (list[i].Value != null && list[i].Value!.Length > 16384) errors["extraHeaders[" + i + "].value"] = "Must be at most 16384 characters.";
            }
            return list;
        }

        private void ApplyNested(ModelEndpoint e, ModelEndpointRequest r, Dictionary<string, string> errors, List<string> changed)
        {
            ApplyProviderSettings(e, r, errors, changed);
            if (r.Parameters != null)
            {
                Dictionary<string, JsonElement> p = r.Parameters;
                if (AiFields.Has(p, "temperature", out JsonElement v)) e.Temperature = AiFields.Double(v, "parameters.temperature", 0, 2, errors);
                if (AiFields.Has(p, "topP", out v)) e.TopP = AiFields.Double(v, "parameters.topP", 0, 1, errors);
                if (AiFields.Has(p, "maxTokens", out v)) e.MaxTokens = AiFields.Int(v, "parameters.maxTokens", 1, 200000, errors);
                if (AiFields.Has(p, "contextWindow", out v)) e.ContextWindow = AiFields.Int(v, "parameters.contextWindow", 512, 2000000, errors);
                if (AiFields.Has(p, "reasoningEffort", out v)) e.ReasoningEffort = AiFields.Enum<ModelReasoningEffortEnum>(v, "parameters.reasoningEffort", errors);
                if (AiFields.Has(p, "streaming", out v)) e.Streaming = AiFields.Bool(v, "parameters.streaming", errors) ?? true;
                if (AiFields.Has(p, "toolCalling", out v)) e.ToolCalling = AiFields.IsNull(v) ? ModelToolCallingEnum.Native : AiFields.Enum<ModelToolCallingEnum>(v, "parameters.toolCalling", errors) ?? ModelToolCallingEnum.Native;
                if (AiFields.Has(p, "timeoutMs", out v)) e.TimeoutMs = AiFields.Int(v, "parameters.timeoutMs", 1000, 600000, errors) ?? 120000;
                if (AiFields.Has(p, "firstResponseTimeoutMs", out v)) e.FirstResponseTimeoutMs = AiFields.Int(v, "parameters.firstResponseTimeoutMs", 500, 300000, errors);
                if (AiFields.Has(p, "dimensions", out v))
                {
                    e.Dimensions = AiFields.Int(v, "parameters.dimensions", 1, 8192, errors);
                    if (e.Dimensions != null && e.Kind != ModelEndpointKindEnum.Embedding) errors["parameters.dimensions"] = "Embedding endpoints only.";
                }
                changed.Add("parameters");
            }
            if (r.Limits != null)
            {
                Dictionary<string, JsonElement> l = r.Limits;
                if (AiFields.Has(l, "maxConcurrentRequests", out JsonElement v)) e.MaxConcurrentRequests = AiFields.Int(v, "limits.maxConcurrentRequests", 1, 256, errors) ?? 2;
                if (AiFields.Has(l, "maxQueueDepth", out v)) e.MaxQueueDepth = AiFields.Int(v, "limits.maxQueueDepth", 0, 10000, errors) ?? 20;
                if (AiFields.Has(l, "maxRetries", out v)) e.MaxRetries = AiFields.Int(v, "limits.maxRetries", 0, 5, errors) ?? 2;
                changed.Add("limits");
            }
            if (r.HealthCheck != null)
            {
                Dictionary<string, JsonElement> h = r.HealthCheck;
                if (AiFields.Has(h, "enabled", out JsonElement v)) e.HealthCheckEnabled = AiFields.Bool(v, "healthCheck.enabled", errors) ?? true;
                if (AiFields.Has(h, "mode", out v)) e.HealthCheckMode = AiFields.Enum<ModelHealthCheckModeEnum>(v, "healthCheck.mode", errors) ?? ModelHealthCheckModeEnum.Connectivity;
                if (AiFields.Has(h, "intervalMs", out v)) e.HealthCheckIntervalMs = AiFields.Int(v, "healthCheck.intervalMs", 10000, 3600000, errors) ?? 60000;
                if (AiFields.Has(h, "timeoutMs", out v)) e.HealthCheckTimeoutMs = AiFields.Int(v, "healthCheck.timeoutMs", 1000, 120000, errors) ?? 10000;
                if (AiFields.Has(h, "healthyThreshold", out v)) e.HealthyThreshold = AiFields.Int(v, "healthCheck.healthyThreshold", 1, 20, errors) ?? 2;
                if (AiFields.Has(h, "unhealthyThreshold", out v)) e.UnhealthyThreshold = AiFields.Int(v, "healthCheck.unhealthyThreshold", 1, 20, errors) ?? 2;
                changed.Add("healthCheck");
            }
            if (!e.HealthCheckEnabled) e.HealthStatus = ModelHealthStatusEnum.Unknown;
            bool modeFits = e.HealthCheckMode == ModelHealthCheckModeEnum.Connectivity ||
                (e.HealthCheckMode == ModelHealthCheckModeEnum.Completion && e.Kind == ModelEndpointKindEnum.Completion) ||
                (e.HealthCheckMode == ModelHealthCheckModeEnum.Embedding && e.Kind == ModelEndpointKindEnum.Embedding) ||
                (e.HealthCheckMode == ModelHealthCheckModeEnum.Rerank && e.Kind == ModelEndpointKindEnum.Rerank);
            if (!modeFits) errors["healthCheck.mode"] = "Must be Connectivity or match the endpoint kind.";
            if (r.Pricing != null)
            {
                Dictionary<string, JsonElement> p = r.Pricing;
                if (AiFields.Has(p, "inputPerMillionTokens", out JsonElement v)) e.InputPricePerMillion = AiFields.Double(v, "pricing.inputPerMillionTokens", 0, 1000000, errors);
                if (AiFields.Has(p, "outputPerMillionTokens", out v)) e.OutputPricePerMillion = AiFields.Double(v, "pricing.outputPerMillionTokens", 0, 1000000, errors);
                if (AiFields.Has(p, "cacheReadPerMillionTokens", out v)) e.CacheReadPricePerMillion = AiFields.Double(v, "pricing.cacheReadPerMillionTokens", 0, 1000000, errors);
                if (AiFields.Has(p, "cacheWritePerMillionTokens", out v)) e.CacheWritePricePerMillion = AiFields.Double(v, "pricing.cacheWritePerMillionTokens", 0, 1000000, errors);
                changed.Add("pricing");
            }
            if (r.Scope != null && r.Scope.Value != ModelEndpointScopeEnum.Property) errors["scope"] = "Deployment-scope endpoints are created by platform administrators (not available in this release).";
        }

        private static void ApplyProviderSettings(ModelEndpoint e, ModelEndpointRequest r, Dictionary<string, string> errors, List<string> changed)
        {
            // docs/AI_CONTRACT.md sections 1.2 and 3.3: region (Bedrock, VertexAI), project (VertexAI), apiVersion and
            // deployment (AzureOpenAI), accessKeyId (Bedrock with ApiKey). Present fields replace the stored value; fields
            // the provider does not use must be null and are cleared.
            HashSet<string> used = new HashSet<string>(ProviderFields(e.ApiType, e.AuthType), StringComparer.Ordinal);
            if (r.ProviderSettings != null)
            {
                foreach (string field in r.ProviderSettings.Keys)
                {
                    if (!_ProviderFields.Contains(field)) errors["providerSettings." + field] = "Unknown provider setting (use " + String.Join(", ", _ProviderFields) + ").";
                }
                foreach (string field in _ProviderFields)
                {
                    if (!AiFields.Has(r.ProviderSettings, field, out JsonElement value)) continue;
                    if (!used.Contains(field))
                    {
                        if (!AiFields.IsNull(value)) errors["providerSettings." + field] = "Not used by " + e.ApiType + (field == "accessKeyId" ? " with " + e.AuthType : String.Empty) + "; must be null.";
                        continue;
                    }
                    string? text = AiFields.Str(value, "providerSettings." + field, 200, errors);
                    if (text != null && text.Length == 0) text = null;
                    if (text != null && !_ProviderValue.IsMatch(text)) errors["providerSettings." + field] = "Use 1 to 200 of A-Z a-z 0-9 . _ : - characters.";
                    SetProviderField(e, field, text);
                }
                changed.Add("providerSettings");
            }
            foreach (string field in _ProviderFields)
            {
                if (!used.Contains(field)) SetProviderField(e, field, null);
            }
            foreach (string field in RequiredProviderFields(e.ApiType, e.AuthType))
            {
                if (String.IsNullOrWhiteSpace(ProviderField(e, field)) && !errors.ContainsKey("providerSettings." + field)) errors["providerSettings." + field] = "Required for " + e.ApiType + (field == "accessKeyId" ? " with ApiKey auth" : String.Empty) + ".";
            }
            if (e.ApiType == ModelApiTypeEnum.VertexAI && e.AuthType == ModelAuthTypeEnum.ApiKey && !String.IsNullOrEmpty(r.AuthValue) && !IsServiceAccountJson(r.AuthValue))
                errors["authValue"] = "VertexAI ApiKey auth takes the service account key JSON (with client_email and private_key).";
        }

        private static IReadOnlyList<string> ProviderFields(ModelApiTypeEnum apiType, ModelAuthTypeEnum authType)
        {
            switch (apiType)
            {
                case ModelApiTypeEnum.AzureOpenAI:
                    return new[] { "apiVersion", "deployment" };
                case ModelApiTypeEnum.VertexAI:
                    return new[] { "project", "region" };
                case ModelApiTypeEnum.Bedrock:
                    return authType == ModelAuthTypeEnum.ApiKey ? new[] { "region", "accessKeyId" } : new[] { "region" };
                default:
                    return Array.Empty<string>();
            }
        }

        private static IReadOnlyList<string> RequiredProviderFields(ModelApiTypeEnum apiType, ModelAuthTypeEnum authType)
        {
            switch (apiType)
            {
                case ModelApiTypeEnum.AzureOpenAI:
                    return new[] { "deployment" };
                case ModelApiTypeEnum.VertexAI:
                    return new[] { "project", "region" };
                case ModelApiTypeEnum.Bedrock:
                    return authType == ModelAuthTypeEnum.ApiKey ? new[] { "region", "accessKeyId" } : new[] { "region" };
                default:
                    return Array.Empty<string>();
            }
        }

        private static string? ProviderField(ModelEndpoint e, string field)
        {
            switch (field)
            {
                case "region": return e.Region;
                case "project": return e.Project;
                case "apiVersion": return e.ApiVersion;
                case "deployment": return e.Deployment;
                default: return e.AccessKeyId;
            }
        }

        private static void SetProviderField(ModelEndpoint e, string field, string? value)
        {
            switch (field)
            {
                case "region": e.Region = value; break;
                case "project": e.Project = value; break;
                case "apiVersion": e.ApiVersion = value; break;
                case "deployment": e.Deployment = value; break;
                default: e.AccessKeyId = value; break;
            }
        }

        private static bool IsServiceAccountJson(string value)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(value))
                {
                    JsonElement root = doc.RootElement;
                    return root.ValueKind == JsonValueKind.Object &&
                        root.TryGetProperty("client_email", out JsonElement email) && email.ValueKind == JsonValueKind.String && !String.IsNullOrWhiteSpace(email.GetString()) &&
                        root.TryGetProperty("private_key", out JsonElement key) && key.ValueKind == JsonValueKind.String && !String.IsNullOrWhiteSpace(key.GetString());
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private bool ApiTypeEnabled(ModelApiTypeEnum apiType)
        {
            string raw = _Ai.App.Settings.Ai.EnabledApiTypes ?? String.Empty;
            if (String.IsNullOrWhiteSpace(raw) || raw.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)) return true;
            return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(t => String.Equals(t, apiType.ToString(), StringComparison.OrdinalIgnoreCase));
        }

        private static string? UrlProblem(string url, ModelApiTypeEnum apiType)
        {
            if (String.IsNullOrEmpty(url)) return _HasDefaultUrl.Contains(apiType) ? null : "Required for " + apiType + ".";
            if (url.Length > 2000) return "Must be at most 2000 characters.";
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return "Must be an absolute http or https URL.";
            if (!String.IsNullOrEmpty(uri.UserInfo)) return "Must not contain credentials.";
            if (!String.IsNullOrEmpty(uri.Query) || !String.IsNullOrEmpty(uri.Fragment)) return "Must not contain a query string or fragment (use QueryParameter auth).";
            return null;
        }

        private static string HostOf(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ? uri.Host : String.Empty;
        }

        private static string HeaderField(string name)
        {
            return "extraHeader:" + name.Trim().ToLowerInvariant();
        }

        private string Protect(ModelEndpoint endpoint, string field, string value)
        {
            if (!_Ai.Protector.IsConfigured) throw ApiErrorException.Field(field == "authValue" ? "authValue" : "extraHeaders", "Secrets cannot be stored until secrets.encryptionKey (TK_SECRETS_ENCRYPTION_KEY) is set.");
            return _Ai.Protector.Protect(value, SecretBoxProtector.EndpointAssociatedData(endpoint.PropertyId, endpoint.Id, field));
        }

        private EnumerationResult<ProviderModelView> PageOf(List<ProviderModelView> models, EnumerationQuery? query, string endpointId)
        {
            EnumerationRequest request = _Ai.App.Enumeration.Prepare(query, AiCollections.ProviderModels, true, "provider-models|" + endpointId);
            return CatalogEnumeration.Page(request, models.DistinctBy(item => item.Name).ToList(), item => item.Name,
                item => item.CreatedUtc ?? DateTime.UnixEpoch,
                item => item.Name + " " + item.DisplayName + " " + item.Description,
                item => item.Labels, item => item.Tags, item => item.Name);
        }

        #endregion
    }
}
