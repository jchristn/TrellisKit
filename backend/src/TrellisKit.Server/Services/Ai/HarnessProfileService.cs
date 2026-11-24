namespace TrellisKit.Server.Services.Ai
{
    using PolyPrompt.Models;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Security;

    /// <summary>
    /// Harness profiles (docs/HARNESSES.md, "Harness profiles"): the administrator's settings of each CLI agent harness
    /// runtime. A profile holds the default model, the permission mode (Restricted or ReadOnly only), the timeout, the
    /// job tool call limit, whether web research is allowed, a base URL, and the credential (an API key, an auth token,
    /// an OAuth token, or the worker's own environment). The credential is encrypted like an endpoint secret, never
    /// returned by a read, and revealed only under the endpoint reveal rules. Profiles change what a harness call
    /// receives; they never change where harnesses run (jobs.runHarnesses instances, inside a job item).
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class HarnessProfileService
    {
        #region Public-Members

        /// <summary>
        /// The harness runtimes, in catalog order.
        /// </summary>
        public static readonly IReadOnlyList<AiExecutorRuntimeEnum> Runtimes = new List<AiExecutorRuntimeEnum>
        {
            AiExecutorRuntimeEnum.ClaudeCode, AiExecutorRuntimeEnum.Codex, AiExecutorRuntimeEnum.GeminiCli, AiExecutorRuntimeEnum.CursorCli,
            AiExecutorRuntimeEnum.Mux, AiExecutorRuntimeEnum.OpenCode, AiExecutorRuntimeEnum.Pi
        };

        /// <summary>
        /// Longest stored credential, in characters.
        /// </summary>
        public const int MaxCredentialCharacters = 16384;

        #endregion

        #region Private-Members

        private readonly AiServices _Ai;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="ai">AI services of the property.</param>
        /// <exception cref="ArgumentNullException">ai is null.</exception>
        public HarnessProfileService(AiServices ai)
        {
            _Ai = ai ?? throw new ArgumentNullException(nameof(ai));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The permission mode a profile forces, or null when it leaves the mode to each job.
        /// </summary>
        /// <param name="profile">Profile, or null.</param>
        /// <returns>Restricted, ReadOnly, or null.</returns>
        public static CliPermissionMode? PermissionOf(AiHarnessProfile? profile)
        {
            if (profile == null || String.IsNullOrWhiteSpace(profile.PermissionMode)) return null;
            if (String.Equals(profile.PermissionMode, "ReadOnly", StringComparison.OrdinalIgnoreCase)) return CliPermissionMode.ReadOnly;
            return CliPermissionMode.Restricted;
        }

        /// <summary>
        /// Read the saved profile of a runtime (the harness service calls this for every harness call).
        /// </summary>
        /// <param name="runtime">Runtime.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Profile, or null when none was saved.</returns>
        public Task<AiHarnessProfile?> ReadAsync(AiExecutorRuntimeEnum runtime, CancellationToken token = default)
        {
            return _Ai.App.Database.Ai.Harnesses.ReadAsync(runtime, token);
        }

        /// <summary>
        /// Decrypt the stored credential of a profile.
        /// </summary>
        /// <param name="profile">Profile.</param>
        /// <returns>Credential, or null when none is stored or the type is Environment.</returns>
        /// <exception cref="ArgumentNullException">profile is null.</exception>
        /// <exception cref="CryptographicException">The stored value cannot be decrypted with the configured keys.</exception>
        public string? Credential(AiHarnessProfile profile)
        {
            ArgumentNullException.ThrowIfNull(profile);
            if (profile.CredentialType == HarnessCredentialTypeEnum.Environment || String.IsNullOrEmpty(profile.CredentialSecret)) return null;
            return _Ai.Protector.Unprotect(profile.CredentialSecret, AssociatedData(profile.Runtime));
        }

        /// <summary>
        /// GET /api/v1/admin/ai/harnesses (SystemAdmin): every harness runtime with its profile.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One view per runtime (a bounded catalog).</returns>
        /// <exception cref="ApiErrorException">403 Forbidden.</exception>
        public async Task<EnumerationResult<AiHarnessProfileView>> EnumerateAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            RequireAdmin(actor);
            EnumerationRequest request = _Ai.App.Enumeration.Prepare(query, AiCollections.AdminHarnesses, true);
            List<AiHarnessProfileView> source = await ListCatalogAsync(actor, token).ConfigureAwait(false);
            return CatalogEnumeration.Page(request, source, item => item.Runtime, search: item => item.Runtime, labels: item => item.Labels, tags: item => item.Tags);
        }

        private async Task<List<AiHarnessProfileView>> ListCatalogAsync(ContentActor actor, CancellationToken token = default)
        {
            RequireAdmin(actor);
            List<AiHarnessProfile> saved = await _Ai.App.Database.Ai.Harnesses.ReadAllAsync(token).ConfigureAwait(false);
            return Runtimes.Select(r => View(r, saved.FirstOrDefault(p => p.Runtime == r))).ToList();
        }

        /// <summary>
        /// GET /api/v1/admin/ai/harnesses/{runtime} (SystemAdmin).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="runtime">Runtime name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task<AiHarnessProfileView> GetAsync(ContentActor actor, string runtime, CancellationToken token = default)
        {
            RequireAdmin(actor);
            AiExecutorRuntimeEnum r = Require(runtime);
            return View(r, await ReadAsync(r, token).ConfigureAwait(false));
        }

        /// <summary>
        /// PUT /api/v1/admin/ai/harnesses/{runtime} (SystemAdmin): replace a runtime's profile. Audited as
        /// ai.harness.update (the credential value is never written to the audit trail).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="runtime">Runtime name.</param>
        /// <param name="request">Body.</param>
        /// <param name="present">Top-level properties present in the body (the credential is kept when absent).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound.</exception>
        public async Task<AiHarnessProfileView> SaveAsync(ContentActor actor, RequestInfo info, string runtime, AiHarnessProfileRequest request, HashSet<string> present, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(present);
            RequireAdmin(actor);
            AiExecutorRuntimeEnum r = Require(runtime);
            AiHarnessProfile? existing = await ReadAsync(r, token).ConfigureAwait(false);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            AiHarnessProfile profile = new AiHarnessProfile
            {
                Runtime = r,
                CreatedUtc = existing?.CreatedUtc ?? DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow,
                UpdatedByUserId = actor.UserId,
                AllowWebResearch = request.AllowWebResearch ?? true
            };

            string? model = String.IsNullOrWhiteSpace(request.Model) ? null : request.Model.Trim();
            if (model != null && model.Length > 200) errors["model"] = "At most 200 characters.";
            profile.Model = model;

            if (!String.IsNullOrWhiteSpace(request.PermissionMode))
            {
                // docs/HARNESSES.md: harnesses never run in WorkspaceWrite or Unrestricted mode, whoever asks.
                if (String.Equals(request.PermissionMode.Trim(), "Restricted", StringComparison.OrdinalIgnoreCase)) profile.PermissionMode = "Restricted";
                else if (String.Equals(request.PermissionMode.Trim(), "ReadOnly", StringComparison.OrdinalIgnoreCase)) profile.PermissionMode = "ReadOnly";
                else errors["permissionMode"] = "Restricted or ReadOnly.";
            }

            if (request.TimeoutSeconds != null)
            {
                if (request.TimeoutSeconds.Value < 30 || request.TimeoutSeconds.Value > 3600) errors["timeoutSeconds"] = "30 to 3600.";
                else profile.TimeoutSeconds = request.TimeoutSeconds;
            }
            if (request.MaxToolCalls != null)
            {
                if (request.MaxToolCalls.Value < 1 || request.MaxToolCalls.Value > 500) errors["maxToolCalls"] = "1 to 500.";
                else profile.MaxToolCalls = request.MaxToolCalls;
            }

            string? baseUrl = String.IsNullOrWhiteSpace(request.BaseUrl) ? null : request.BaseUrl.Trim();
            if (baseUrl != null)
            {
                string? problem = HarnessLaunchPlanner.BaseUrlVariable(r) == null ? r + " takes no base URL." : UrlProblem(baseUrl);
                if (problem != null) errors["baseUrl"] = problem;
                else profile.BaseUrl = baseUrl;
            }

            HarnessCredentialTypeEnum type = HarnessCredentialTypeEnum.Environment;
            if (!String.IsNullOrWhiteSpace(request.CredentialType) && (!Enum.TryParse(request.CredentialType.Trim(), true, out type) || !Enum.IsDefined(type)))
            {
                errors["credentialType"] = "Environment, ApiKey, AuthToken, or OAuthToken.";
                type = HarnessCredentialTypeEnum.Environment;
            }
            else if (!HarnessLaunchPlanner.CredentialTypes(r).Contains(type))
            {
                errors["credentialType"] = r + " accepts " + String.Join(", ", HarnessLaunchPlanner.CredentialTypes(r)) + ".";
            }
            profile.CredentialType = type;
            if (type == HarnessCredentialTypeEnum.AuthToken && baseUrl == null && !errors.ContainsKey("baseUrl")) errors["baseUrl"] = "Required with AuthToken (the gateway the token is for).";

            bool sent = present.Contains("credential") && request.Credential != null;
            string? secret = null;
            if (type != HarnessCredentialTypeEnum.Environment && !errors.ContainsKey("credentialType"))
            {
                if (sent && request.Credential!.Length > 0)
                {
                    if (request.Credential.Length > MaxCredentialCharacters) errors["credential"] = "Must be at most " + MaxCredentialCharacters + " characters.";
                    else if (!_Ai.Protector.IsConfigured) errors["credential"] = "Secrets cannot be stored until secrets.encryptionKey (TK_SECRETS_ENCRYPTION_KEY) is set.";
                    else secret = _Ai.Protector.Protect(request.Credential, AssociatedData(r));
                }
                else if (!sent && existing != null && existing.CredentialType == type && !String.IsNullOrEmpty(existing.CredentialSecret))
                {
                    secret = existing.CredentialSecret;
                }
                else
                {
                    errors["credential"] = "Required for " + type + " (or choose Environment to use the worker's own credentials).";
                }
            }
            profile.CredentialSecret = secret;

            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            await _Ai.App.Database.Ai.Harnesses.SaveAsync(profile, token).ConfigureAwait(false);
            bool changed = (existing?.CredentialSecret ?? String.Empty) != (profile.CredentialSecret ?? String.Empty);
            await _Ai.App.Audit.WriteAsync(info, "ai.harness.update", "AiHarnessProfile", r.ToString(),
                "credential " + type + (changed ? " changed" : " kept") + (profile.BaseUrl == null ? String.Empty : "; base URL set") + (profile.PermissionMode == null ? String.Empty : "; mode " + profile.PermissionMode), token).ConfigureAwait(false);
            return View(r, profile);
        }

        /// <summary>
        /// POST /api/v1/admin/ai/harnesses/{runtime}/reveal: return the stored credential, under the endpoint reveal
        /// rules (docs/AI_CONTRACT.md section 2.5). Audited as ai.harness.reveal without the value.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information.</param>
        /// <param name="runtime">Runtime name.</param>
        /// <param name="request">Reason.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The value.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 RevealNotAllowed, 404 NotFound, 429 RateLimited.</exception>
        public async Task<SecretRevealResponse> RevealAsync(ContentActor actor, RequestInfo info, string runtime, HarnessRevealRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            await _Ai.Endpoints.EnsureRevealAllowedAsync(actor, info, token).ConfigureAwait(false);
            AiExecutorRuntimeEnum r = Require(runtime);
            string reason = (request.Reason ?? String.Empty).Trim();
            if (reason.Length < 10 || reason.Length > 500) throw ApiErrorException.Field("reason", "Must be 10 to 500 characters.");
            _Ai.App.Limits.EnforceMember(actor, "ai-reveals", _Ai.App.Settings.Ai.RevealsPerHour, RateLimitService.Hour);
            AiHarnessProfile? profile = await ReadAsync(r, token).ConfigureAwait(false);
            string? value = profile == null ? null : Credential(profile);
            if (String.IsNullOrEmpty(value)) throw ApiErrorException.NotFound("Secret");
            TrellisKitTelemetry.Add(AiTelemetry.SecretReveals, 1);
            await _Ai.App.Audit.WriteAsync(info, "ai.harness.reveal", "AiHarnessProfile", r.ToString(), "credential; reason: " + reason, token).ConfigureAwait(false);
            _Ai.App.Logger.Info("[HarnessProfileService] " + actor.UserId + " revealed the credential of harness " + r);
            return new SecretRevealResponse { Field = "credential", Value = value };
        }

        /// <summary>
        /// Rewrite every stored harness credential with the current key (part of the endpoint re-encryption route).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Credentials rewritten.</returns>
        public async Task<int> ReencryptAsync(CancellationToken token = default)
        {
            int rewritten = 0;
            foreach (AiHarnessProfile profile in await _Ai.App.Database.Ai.Harnesses.ReadAllAsync(token).ConfigureAwait(false))
            {
                if (!_Ai.Protector.NeedsRotation(profile.CredentialSecret)) continue;
                string? value = Credential(profile);
                if (String.IsNullOrEmpty(value)) continue;
                profile.CredentialSecret = _Ai.Protector.Protect(value, AssociatedData(profile.Runtime));
                await _Ai.App.Database.Ai.Harnesses.SaveAsync(profile, token).ConfigureAwait(false);
                rewritten++;
            }
            return rewritten;
        }

        #endregion

        #region Private-Methods

        private static void RequireAdmin(ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (!actor.IsSystemAdmin) throw new ApiErrorException(403, "Forbidden", "Only administrators can read or change harness profiles.");
        }

        private static AiExecutorRuntimeEnum Require(string runtime)
        {
            if (!String.IsNullOrWhiteSpace(runtime) && Enum.TryParse(runtime.Trim(), true, out AiExecutorRuntimeEnum parsed) && Runtimes.Contains(parsed)) return parsed;
            throw ApiErrorException.NotFound("Harness runtime");
        }

        private string AssociatedData(AiExecutorRuntimeEnum runtime)
        {
            return SecretBoxProtector.EndpointAssociatedData(_Ai.App.PropertyId, "harness:" + runtime, "credential");
        }

        private static string? UrlProblem(string url)
        {
            if (url.Length > 2000) return "Must be at most 2000 characters.";
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return "Must be an absolute http or https URL.";
            if (!String.IsNullOrEmpty(uri.UserInfo)) return "Must not contain credentials.";
            if (!String.IsNullOrEmpty(uri.Query) || !String.IsNullOrEmpty(uri.Fragment)) return "Must not contain a query string or fragment.";
            return null;
        }

        private AiHarnessProfileView View(AiExecutorRuntimeEnum runtime, AiHarnessProfile? profile)
        {
            HarnessCredentialTypeEnum type = profile?.CredentialType ?? HarnessCredentialTypeEnum.Environment;
            return new AiHarnessProfileView
            {
                Runtime = runtime.ToString(),
                SupportsJobTools = HarnessLaunchPlanner.ToolsUnsupportedReason(runtime) == null,
                CredentialTypes = HarnessLaunchPlanner.CredentialTypes(runtime).Select(t => t.ToString()).ToList(),
                BaseUrlVariable = HarnessLaunchPlanner.BaseUrlVariable(runtime),
                CredentialVariables = HarnessLaunchPlanner.StoredCredentialVariables(runtime, type),
                CredentialVariablesByType = HarnessLaunchPlanner.CredentialTypes(runtime).Where(t => t != HarnessCredentialTypeEnum.Environment)
                    .ToDictionary(t => t.ToString(), t => HarnessLaunchPlanner.StoredCredentialVariables(runtime, t)),
                EnvironmentVariables = HarnessLaunchPlanner.CredentialVariables(runtime),
                Model = profile?.Model,
                PermissionMode = profile?.PermissionMode,
                TimeoutSeconds = profile?.TimeoutSeconds,
                MaxToolCalls = profile?.MaxToolCalls,
                AllowWebResearch = profile?.AllowWebResearch ?? true,
                BaseUrl = profile?.BaseUrl,
                CredentialType = type.ToString(),
                HasCredential = type != HarnessCredentialTypeEnum.Environment && !String.IsNullOrEmpty(profile?.CredentialSecret),
                DefaultTimeoutSeconds = _Ai.App.Settings.Jobs.HarnessTimeoutSeconds,
                DefaultMaxToolCalls = _Ai.App.Settings.Jobs.HarnessMaxToolCalls,
                RunsOnThisInstance = _Ai.App.Settings.Jobs.RunHarnesses,
                Saved = profile != null,
                UpdatedByUserId = profile?.UpdatedByUserId,
                UpdatedUtc = profile?.UpdatedUtc
            };
        }

        #endregion
    }
}
