namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Serialization;

    /// <summary>
    /// Assistants (docs/AI_CONTRACT.md sections 5 and 6.1): admin configuration with per-purpose endpoint validation,
    /// system prompt token checks, knowledge sources, limits, embed settings, and budgets; the member summaries and the
    /// audience rules of section 13.1.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class AssistantService
    {
        #region Public-Members

        /// <summary>
        /// Tokens allowed in system prompts.
        /// </summary>
        public static readonly IReadOnlyList<string> PromptTokens = new[] { "name", "shortName", "memberDisplayName", "date", "assistantTitle" };

        #endregion

        #region Private-Members

        private static readonly Regex _Token = new Regex("\\{([A-Za-z][A-Za-z0-9]*)\\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private readonly AiServices _Ai;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="ai">AI services of the property.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public AssistantService(AiServices ai)
        {
            _Ai = ai ?? throw new ArgumentNullException(nameof(ai));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Admin enumeration (filters enabled, audience, endpointId, embedEnabled).
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        public async Task<EnumerationResult<AssistantView>> EnumerateAdminAsync(EnumerationQuery? query, CancellationToken token)
        {
            _Ai.EnsureAvailable();
            EnumerationRequest request = _Ai.App.Enumeration.Prepare(query, AiCollections.AdminAssistants, true);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            AiEnumerationFilter filter = new AiEnumerationFilter
            {
                Enabled = request.Query.FilterBool("enabled", errors),
                Audience = request.Query.FilterEnum<ContentAudienceEnum>("audience", errors),
                EndpointId = request.Query.FilterString("endpointId"),
                EmbedEnabled = request.Query.FilterBool("embedEnabled", errors)
            };
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationResult<Assistant> page = await _Ai.App.Database.Ai.Assistants.EnumerateAsync(request, filter, null, token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Ai.App.Metadata.LoadAsync(TargetTypeEnum.Assistant, page.Objects.Select(o => o.Id), true, token).ConfigureAwait(false);
            Dictionary<string, AiActivityCounts> stats = await _Ai.App.Database.Ai.Usage.ActivityByAssistantAsync(page.Objects.Select(o => o.Id), DateTime.UtcNow.AddDays(-30), token).ConfigureAwait(false);
            List<AssistantView> views = new List<AssistantView>();
            foreach (Assistant a in page.Objects) views.Add(await ToViewAsync(a, metadata.TryGetValue(a.Id, out ObjectMetadata? m) ? m : null, stats.TryGetValue(a.Id, out AiActivityCounts? s) ? s : null, token).ConfigureAwait(false));
            return page.With(views);
        }

        /// <summary>
        /// Admin read.
        /// </summary>
        /// <param name="id">Assistant id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        public async Task<AssistantView> GetAdminAsync(string id, CancellationToken token)
        {
            Assistant assistant = await RequireAsync(id, token).ConfigureAwait(false);
            return await ViewAsync(assistant, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Read a live assistant or throw 404.
        /// </summary>
        /// <param name="id">Assistant id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Assistant.</returns>
        /// <exception cref="ApiErrorException">404 NotAvailable or NotFound.</exception>
        public async Task<Assistant> RequireAsync(string id, CancellationToken token)
        {
            _Ai.EnsureAvailable();
            if (String.IsNullOrWhiteSpace(id) || !id.StartsWith(AiConstants.AssistantIdPrefix, StringComparison.Ordinal)) throw ApiErrorException.NotFound("Assistant");
            Assistant? assistant = await _Ai.App.Database.Ai.Assistants.ReadAsync(id, token).ConfigureAwait(false);
            return assistant ?? throw ApiErrorException.NotFound("Assistant");
        }

        /// <summary>
        /// Create an assistant. Audited as ai.assistant.create.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="request">Body.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 400 EndpointKindMismatch, 409 Conflict.</exception>
        public async Task<AssistantView> CreateAsync(ContentActor actor, AssistantRequest request, RequestInfo info, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            _Ai.EnsureAvailable();
            DateTime now = DateTime.UtcNow;
            Assistant assistant = new Assistant
            {
                PropertyId = _Ai.App.PropertyId,
                Id = IdGenerator.Generate(AiConstants.AssistantIdPrefix),
                Audience = _Ai.App.Access.Current.DefaultAudienceFor(FeatureKeyEnum.Assistants),
                CreatedByUserId = actor.UserId,
                CreatedUtc = now,
                UpdatedUtc = now
            };
            await ApplyAsync(assistant, request, null, true, token).ConfigureAwait(false);
            await _Ai.App.Metadata.ValidateAsync(actor, request.Labels, request.Tags, true, token, TargetTypeEnum.Assistant).ConfigureAwait(false);
            await SaveAsync(assistant, true, token).ConfigureAwait(false);
            await _Ai.App.Metadata.ApplyAsync(actor, TargetTypeEnum.Assistant, assistant.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            await _Ai.App.Audit.WriteAsync(info, "ai.assistant.create", "Assistant", assistant.Id, assistant.Name, token).ConfigureAwait(false);
            return await ViewAsync(assistant, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Update an assistant (partial; nested objects merge; lists replace when present). Audited as ai.assistant.update.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Assistant id.</param>
        /// <param name="request">Body.</param>
        /// <param name="present">Top-level fields present.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        public async Task<AssistantView> UpdateAsync(ContentActor actor, string id, AssistantRequest request, HashSet<string> present, RequestInfo info, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            Assistant assistant = await RequireAsync(id, token).ConfigureAwait(false);
            await ApplyAsync(assistant, request, present, false, token).ConfigureAwait(false);
            await _Ai.App.Metadata.ValidateAsync(actor, request.Labels, request.Tags, true, token, TargetTypeEnum.Assistant).ConfigureAwait(false);
            assistant.UpdatedUtc = DateTime.UtcNow;
            await SaveAsync(assistant, false, token).ConfigureAwait(false);
            await _Ai.App.Metadata.ApplyAsync(actor, TargetTypeEnum.Assistant, assistant.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            await _Ai.App.Audit.WriteAsync(info, "ai.assistant.update", "Assistant", assistant.Id, String.Join(", ", present.OrderBy(p => p, StringComparer.Ordinal)), token).ConfigureAwait(false);
            return await ViewAsync(assistant, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Delete an assistant (its conversations are deleted after ai.deletedAssistantRetentionDays). Audited as
        /// ai.assistant.delete.
        /// </summary>
        /// <param name="id">Assistant id.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task DeleteAsync(string id, RequestInfo info, CancellationToken token)
        {
            Assistant assistant = await RequireAsync(id, token).ConfigureAwait(false);
            await _Ai.Indexer.DeleteAsync(assistant.Id, token).ConfigureAwait(false);
            await _Ai.App.Database.Ai.Assistants.DeleteAsync(assistant.Id, DateTime.UtcNow, token).ConfigureAwait(false);
            await _Ai.App.Audit.WriteAsync(info, "ai.assistant.delete", "Assistant", assistant.Id, assistant.Name, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Duplicate an assistant (the copy is disabled). Audited as ai.assistant.duplicate.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Assistant id.</param>
        /// <param name="request">Name of the copy.</param>
        /// <param name="info">Request information.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View of the copy.</returns>
        public async Task<AssistantView> DuplicateAsync(ContentActor actor, string id, AssistantDuplicateRequest request, RequestInfo info, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(actor);
            Assistant source = await RequireAsync(id, token).ConfigureAwait(false);
            Assistant copy = JsonSerializer.Deserialize<Assistant>(JsonSerializer.Serialize(source))!;
            DateTime now = DateTime.UtcNow;
            copy.Id = IdGenerator.Generate(AiConstants.AssistantIdPrefix);
            string name = String.IsNullOrWhiteSpace(request?.Name) ? "Copy of " + source.Name : request!.Name!.Trim();
            copy.Name = name.Length > 100 ? name.Substring(0, 100) : name;
            copy.Enabled = false;
            copy.CreatedByUserId = actor.UserId;
            copy.CreatedUtc = now;
            copy.UpdatedUtc = now;
            foreach (AssistantKnowledgeSource s in copy.Sources) s.Id = IdGenerator.Generate(AiConstants.KnowledgeSourceIdPrefix);
            await SaveAsync(copy, true, token).ConfigureAwait(false);
            await _Ai.App.Metadata.SnapshotAsync(TargetTypeEnum.Assistant, source.Id, TargetTypeEnum.Assistant, copy.Id, token).ConfigureAwait(false);
            await _Ai.App.Audit.WriteAsync(info, "ai.assistant.duplicate", "Assistant", copy.Id, "from " + source.Id, token).ConfigureAwait(false);
            return await ViewAsync(copy, token).ConfigureAwait(false);
        }

        /// <summary>
        /// An unsaved copy of an assistant with draft fields applied and validated (test console).
        /// </summary>
        /// <param name="assistant">Saved assistant.</param>
        /// <param name="draft">Draft fields, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Copy.</returns>
        public async Task<Assistant> DraftAsync(Assistant assistant, AssistantRequest? draft, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(assistant);
            Assistant copy = JsonSerializer.Deserialize<Assistant>(JsonSerializer.Serialize(assistant))!;
            copy.PropertyId = assistant.PropertyId;
            if (draft == null) return copy;
            JsonElement root = JsonSerializer.SerializeToElement(draft, ApiJson.Api);
            HashSet<string> present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in root.EnumerateObject())
                if (property.Value.ValueKind != JsonValueKind.Null) present.Add(property.Name);
            await ApplyAsync(copy, draft, present, false, token).ConfigureAwait(false);
            return copy;
        }

        /// <summary>
        /// Load the endpoints an assistant references (live ones only), by id.
        /// </summary>
        /// <param name="assistant">Assistant.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Endpoints.</returns>
        public async Task<Dictionary<string, ModelEndpoint>> EndpointsAsync(Assistant assistant, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(assistant);
            List<ModelEndpoint> list = await _Ai.App.Database.Ai.Endpoints.ReadManyAsync(assistant.Models.Values.Distinct(), token).ConfigureAwait(false);
            return list.ToDictionary(e => e.Id, StringComparer.Ordinal);
        }

        /// <summary>
        /// The system prompt with its tokens substituted, plus the engine's fixed guidance.
        /// </summary>
        /// <param name="assistant">Assistant.</param>
        /// <param name="caller">Caller.</param>
        /// <returns>System message text.</returns>
        public string RenderSystemPrompt(Assistant assistant, AssistantCaller caller)
        {
            ArgumentNullException.ThrowIfNull(assistant);
            ArgumentNullException.ThrowIfNull(caller);
            string prompt = (assistant.SystemPrompt ?? String.Empty)
                .Replace("{name}", _Ai.App.Property.Name, StringComparison.Ordinal)
                .Replace("{shortName}", String.IsNullOrWhiteSpace(_Ai.App.Property.Property.ShortName) ? _Ai.App.Property.Name : _Ai.App.Property.Property.ShortName, StringComparison.Ordinal)
                .Replace("{memberDisplayName}", caller.DisplayName ?? String.Empty, StringComparison.Ordinal)
                .Replace("{date}", DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{assistantTitle}", assistant.Title, StringComparison.Ordinal);
            string guard = _Ai.Prompts.Cached(PromptCatalog.AssistantGuard);
            return prompt.Length == 0 ? guard : prompt + "\n\n" + guard;
        }

        /// <summary>
        /// Assistants the caller may use (POST /api/v1/assistants/enumerate).
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of summaries.</returns>
        public async Task<EnumerationResult<AssistantSummaryView>> EnumerateForCallerAsync(AssistantCaller caller, EnumerationQuery? query, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(caller);
            _Ai.EnsureAvailable();
            EnumerationRequest request = _Ai.App.Enumeration.Prepare(query, AiCollections.Assistants, caller.IsSystemAdmin, AudienceScope(caller));
            List<string> audiences = AudiencesFor(_Ai.App.Access.Current, caller).Select(a => a.ToString()).ToList();
            EnumerationResult<Assistant> page = await _Ai.App.Database.Ai.Assistants.EnumerateAsync(request, new AiEnumerationFilter(), audiences, token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Ai.App.Metadata.LoadAsync(TargetTypeEnum.Assistant, page.Objects.Select(o => o.Id), caller.IsSystemAdmin, token).ConfigureAwait(false);
            List<AssistantSummaryView> views = new List<AssistantSummaryView>();
            foreach (Assistant a in page.Objects) views.Add(await SummaryAsync(a, caller, metadata.TryGetValue(a.Id, out ObjectMetadata? m) ? m : null, token).ConfigureAwait(false));
            return page.With(views);
        }

        /// <summary>
        /// GET /api/v1/assistants/{id} for the caller.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="id">Assistant id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Summary.</returns>
        public async Task<AssistantSummaryView> GetForCallerAsync(AssistantCaller caller, string id, CancellationToken token)
        {
            Assistant assistant = await RequireUsableAsync(caller, id, token).ConfigureAwait(false);
            ObjectMetadata metadata = await _Ai.App.Metadata.LoadOneAsync(TargetTypeEnum.Assistant, assistant.Id, caller.IsSystemAdmin, token).ConfigureAwait(false);
            return await SummaryAsync(assistant, caller, metadata, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Read an enabled assistant the caller may chat with (section 13.1): 404 when unknown or disabled, 401 for
        /// SignedIn and Members assistants signed out, 402 MembershipRequired for Members assistants without a membership.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="id">Assistant id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Assistant.</returns>
        public async Task<Assistant> RequireUsableAsync(AssistantCaller caller, string id, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(caller);
            Assistant assistant = await RequireAsync(id, token).ConfigureAwait(false);
            if (!assistant.Enabled && !caller.IsSystemAdmin) throw ApiErrorException.NotFound("Assistant");
            EnsureAudience(_Ai.App.Access.Current, assistant, caller);
            return assistant;
        }

        /// <summary>
        /// Throw when the caller may not chat with the assistant under the property's access model
        /// (docs/AI_CONTRACT.md sections 0.2 and 13.1, docs/ACCESS_MODELS_CONTRACT.md section 3). Signed-in callers need
        /// the assistants feature's participation tier for the assistant's audience: a membership in FullyGated, in
        /// PartiallyGated when assistants is gated or the audience is Members, and never in Open (Members is SignedIn
        /// there). Visitors (signed out) reach only Public assistants, and only when anonymous browsing is on (outside
        /// FullyGated, and assistants not gated) or the assistant is embeddable (the widget, a marketing sample in every
        /// model).
        /// </summary>
        /// <param name="policy">Access policy.</param>
        /// <param name="assistant">Assistant.</param>
        /// <param name="caller">Caller.</param>
        /// <exception cref="ApiErrorException">401 Unauthorized or 402 MembershipRequired, with required, feature, targetType, and targetId.</exception>
        public static void EnsureAudience(AccessPolicy policy, Assistant assistant, AssistantCaller caller)
        {
            ArgumentNullException.ThrowIfNull(policy);
            ArgumentNullException.ThrowIfNull(assistant);
            ArgumentNullException.ThrowIfNull(caller);
            if (caller.IsSystemAdmin) return;
            if (caller.UserId == null)
            {
                if (VisitorMayUse(policy, assistant)) return;
                throw Denied(401, "Unauthorized", "Sign in to chat with this assistant.", "SignIn", assistant.Id);
            }
            AccessTierEnum tier = AccessDecider.TierOf(false, true, true, caller.HasMembership);
            if (!AccessDecider.Meets(tier, AccessDecider.ParticipateTier(policy, FeatureKeyEnum.Assistants, assistant.Audience)))
                throw Denied(402, "MembershipRequired", "An active membership is required.", "Membership", assistant.Id);
        }

        /// <summary>
        /// Whether a signed-out visitor may use an assistant: Public, and either anonymous browsing applies (outside
        /// FullyGated, assistants not gated) or the assistant is embeddable.
        /// </summary>
        /// <param name="policy">Access policy.</param>
        /// <param name="assistant">Assistant.</param>
        /// <returns>True when allowed.</returns>
        public static bool VisitorMayUse(AccessPolicy policy, Assistant assistant)
        {
            ArgumentNullException.ThrowIfNull(policy);
            ArgumentNullException.ThrowIfNull(assistant);
            if (assistant.Audience != ContentAudienceEnum.Public) return false;
            return VisitorsMayBrowse(policy) || assistant.EmbedEnabled;
        }

        /// <summary>
        /// Whether signed-out visitors may list assistants: anonymous browsing applies and assistants is not gated.
        /// </summary>
        /// <param name="policy">Access policy.</param>
        /// <returns>True when allowed.</returns>
        public static bool VisitorsMayBrowse(AccessPolicy policy)
        {
            ArgumentNullException.ThrowIfNull(policy);
            return policy.AllowsAnonymous && !policy.IsGated(FeatureKeyEnum.Assistants);
        }

        /// <summary>
        /// Messages left today for the caller, or null when unlimited (or for administrators).
        /// </summary>
        /// <param name="assistant">Assistant.</param>
        /// <param name="caller">Caller.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Remaining messages.</returns>
        public async Task<int?> RemainingTodayAsync(Assistant assistant, AssistantCaller caller, CancellationToken token)
        {
            int? limit = DailyLimit(assistant, caller);
            if (limit == null) return null;
            string subjectType = caller.UserId != null ? "User" : "Visitor";
            string? subject = caller.UserId ?? caller.VisitorId;
            if (subject == null) return limit;
            AiDailyUsage? usage = await _Ai.App.Database.Ai.Conversations.ReadDailyUsageAsync(assistant.Id, subjectType, subject, Today(), token).ConfigureAwait(false);
            return Math.Max(0, limit.Value - (usage?.Messages ?? 0));
        }

        /// <summary>
        /// The caller's daily allowance, or null when unlimited or exempt.
        /// </summary>
        /// <param name="assistant">Assistant.</param>
        /// <param name="caller">Caller.</param>
        /// <returns>Limit.</returns>
        public static int? DailyLimit(Assistant assistant, AssistantCaller caller)
        {
            ArgumentNullException.ThrowIfNull(assistant);
            ArgumentNullException.ThrowIfNull(caller);
            if (caller.IsSystemAdmin) return null;
            int limit = caller.UserId != null ? assistant.MessagesPerMemberPerDay : assistant.MessagesPerVisitorPerDay;
            return limit <= 0 ? null : limit;
        }

        /// <summary>
        /// Today's UTC date, yyyy-MM-dd.
        /// </summary>
        /// <returns>Date.</returns>
        public static string Today()
        {
            return DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Member summary of an assistant.
        /// </summary>
        /// <param name="assistant">Assistant.</param>
        /// <param name="caller">Caller.</param>
        /// <param name="metadata">Labels and tags, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Summary.</returns>
        public async Task<AssistantSummaryView> SummaryAsync(Assistant assistant, AssistantCaller caller, ObjectMetadata? metadata, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(assistant);
            ArgumentNullException.ThrowIfNull(caller);
            int? limit = DailyLimit(assistant, caller);
            return new AssistantSummaryView
            {
                Id = assistant.Id,
                Name = assistant.Name,
                Title = assistant.Title,
                Description = assistant.Description,
                Avatar = Avatar(assistant),
                StarterPrompts = assistant.StarterPrompts,
                Disclaimer = assistant.Disclaimer,
                Streaming = assistant.Streaming,
                ShowReasoning = assistant.ShowReasoning,
                Audience = assistant.Audience,
                ToolsEnabled = assistant.ToolsEnabled && _Ai.Tools.ForCaller(caller, assistant.AllowedTools).Count > 0,
                KnowledgeEnabled = assistant.KnowledgeEnabled,
                Limits = new AssistantMemberLimitsView
                {
                    MaxMessageCharacters = assistant.MaxMessageCharacters,
                    MaxMessagesPerConversation = assistant.MaxMessagesPerConversation,
                    MessagesPerDay = limit,
                    RemainingToday = await RemainingTodayAsync(assistant, caller, token).ConfigureAwait(false)
                },
                Privacy = new AssistantPrivacyView(),
                Embed = assistant.EmbedEnabled && assistant.Audience == ContentAudienceEnum.Public
                    ? new AssistantEmbedSummaryView { LauncherLabel = assistant.EmbedLauncherLabel, Position = assistant.EmbedPosition, AllowedOrigins = EmbedOrigins(assistant) }
                    : null,
                Labels = metadata?.Labels ?? new List<string>(),
                Tags = metadata?.Tags ?? new Dictionary<string, string>()
            };
        }

        /// <summary>
        /// The exact origins an embeddable assistant may be framed by and exchange postMessage events with: its
        /// embed.allowedOrigins plus the property's own web origins (docs/AI_CONTRACT.md section 13.3), lowercase,
        /// without duplicates (at most 20 configured plus 2 property origins).
        /// </summary>
        /// <param name="assistant">Assistant.</param>
        /// <returns>Origins (scheme://host[:port]).</returns>
        /// <exception cref="ArgumentNullException">The assistant is null.</exception>
        public List<string> EmbedOrigins(Assistant assistant)
        {
            ArgumentNullException.ThrowIfNull(assistant);
            List<string> origins = new List<string>();
            void Add(string? value)
            {
                if (String.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri)) return;
                if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return;
                string origin = uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
                if (!origins.Contains(origin, StringComparer.Ordinal)) origins.Add(origin);
            }
            foreach (string origin in assistant.EmbedOrigins) Add(origin);
            Add(_Ai.App.Property.PublicBaseUrl);
            string web = (_Ai.App.Property.Property.Domains.Web ?? String.Empty).Trim();
            if (web.Length > 0) Add("https://" + web);
            return origins;
        }

        /// <summary>
        /// Avatar view.
        /// </summary>
        /// <param name="assistant">Assistant.</param>
        /// <returns>Avatar.</returns>
        public static AssistantAvatarView Avatar(Assistant assistant)
        {
            ArgumentNullException.ThrowIfNull(assistant);
            return new AssistantAvatarView { MediaId = assistant.AvatarMediaId, Emoji = assistant.AvatarEmoji };
        }

        /// <summary>
        /// Admin view with labels, tags, and 30-day stats.
        /// </summary>
        /// <param name="assistant">Assistant.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        public async Task<AssistantView> ViewAsync(Assistant assistant, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(assistant);
            ObjectMetadata metadata = await _Ai.App.Metadata.LoadOneAsync(TargetTypeEnum.Assistant, assistant.Id, true, token).ConfigureAwait(false);
            Dictionary<string, AiActivityCounts> stats = await _Ai.App.Database.Ai.Usage.ActivityByAssistantAsync(new[] { assistant.Id }, DateTime.UtcNow.AddDays(-30), token).ConfigureAwait(false);
            return await ToViewAsync(assistant, metadata, stats.TryGetValue(assistant.Id, out AiActivityCounts? s) ? s : null, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task<AssistantView> ToViewAsync(Assistant a, ObjectMetadata? metadata, AiActivityCounts? stats, CancellationToken token)
        {
            string? Model(AssistantPurposeEnum purpose)
            {
                return a.Models.TryGetValue(purpose, out string? id) ? id : null;
            }

            long rated = (stats?.ThumbsUp ?? 0) + (stats?.ThumbsDown ?? 0);
            return new AssistantView
            {
                Id = a.Id,
                Name = a.Name,
                Description = a.Description,
                Enabled = a.Enabled,
                Title = a.Title,
                Avatar = Avatar(a),
                SystemPrompt = a.SystemPrompt,
                Models = new AssistantModelsView
                {
                    Chat = Model(AssistantPurposeEnum.Chat),
                    ChatFallback = Model(AssistantPurposeEnum.ChatFallback),
                    TitleGeneration = Model(AssistantPurposeEnum.TitleGeneration),
                    QueryRewrite = Model(AssistantPurposeEnum.QueryRewrite),
                    Embedding = Model(AssistantPurposeEnum.Embedding),
                    Rerank = Model(AssistantPurposeEnum.Rerank),
                    Moderation = Model(AssistantPurposeEnum.Moderation),
                    ToolRouting = Model(AssistantPurposeEnum.ToolRouting)
                },
                Generation = new AssistantGenerationView { Temperature = a.Temperature, TopP = a.TopP },
                Audience = a.Audience,
                Knowledge = new AssistantKnowledgeView
                {
                    Enabled = a.KnowledgeEnabled,
                    Mode = a.KnowledgeMode,
                    MaxContextItems = a.MaxContextItems,
                    MaxContextCharacters = a.MaxContextCharacters,
                    Citations = a.Citations,
                    Sources = a.Sources.Select(s => new KnowledgeSourceView { Id = s.Id, TargetType = s.TargetType, Labels = s.Labels, LabelMatch = s.LabelMatch, Tags = s.Tags, MaxItems = s.MaxItems }).ToList()
                },
                Tools = new AssistantToolsView
                {
                    Enabled = a.ToolsEnabled,
                    Allowed = new List<string>(a.AllowedTools),
                    MaxToolIterations = a.MaxToolIterations,
                    MaxToolCallsPerTurn = a.MaxToolCallsPerTurn,
                    ToolCallTimeoutMs = a.ToolCallTimeoutMs,
                    MaxToolOutputCharacters = a.MaxToolOutputCharacters,
                    MaxTokensPerTurn = a.MaxTokensPerTurn,
                    ShowToolActivity = a.ShowToolActivity
                },
                Limits = new AssistantLimitsView
                {
                    MessagesPerMemberPerDay = a.MessagesPerMemberPerDay,
                    MessagesPerVisitorPerDay = a.MessagesPerVisitorPerDay,
                    MaxMessagesPerConversation = a.MaxMessagesPerConversation,
                    MaxTokensPerReply = a.MaxTokensPerReply,
                    MaxMessageCharacters = a.MaxMessageCharacters,
                    ConversationRetentionDays = a.ConversationRetentionDays
                },
                Streaming = a.Streaming,
                ShowReasoning = a.ShowReasoning,
                StarterPrompts = a.StarterPrompts,
                Disclaimer = a.Disclaimer,
                Embed = new AssistantEmbedView { Enabled = a.EmbedEnabled, AllowedOrigins = a.EmbedOrigins, LauncherLabel = a.EmbedLauncherLabel, Position = a.EmbedPosition },
                ModerationMode = a.ModerationMode,
                Budget = new AssistantBudgetView { MonthlyUsd = a.MonthlyBudgetUsd, HardStop = a.BudgetHardStop, MonthToDateUsd = await _Ai.Usage.MonthToDateUsdAsync(a.Id, token).ConfigureAwait(false) },
                Stats = new AssistantStatsView { Conversations30d = stats?.Conversations ?? 0, Messages30d = stats?.Messages ?? 0, ThumbsUpRate30d = rated == 0 ? null : Math.Round((double)stats!.ThumbsUp / rated, 4) },
                Labels = metadata?.Labels ?? new List<string>(),
                Tags = metadata?.Tags ?? new Dictionary<string, string>(),
                CreatedUtc = a.CreatedUtc,
                UpdatedUtc = a.UpdatedUtc
            };
        }

        private async Task SaveAsync(Assistant assistant, bool create, CancellationToken token)
        {
            try
            {
                if (create) await _Ai.App.Database.Ai.Assistants.CreateAsync(assistant, token).ConfigureAwait(false);
                else await _Ai.App.Database.Ai.Assistants.UpdateAsync(assistant, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw new ApiErrorException(409, "Conflict", "An assistant with that name already exists.", new Dictionary<string, string> { { "name", "Already in use." } });
            }
        }

        private static string AudienceScope(AssistantCaller caller)
        {
            return caller.IsSystemAdmin ? "admin" : caller.UserId == null ? "visitor" : caller.HasMembership ? "member" : "signedin";
        }

        private static List<ContentAudienceEnum> AudiencesFor(AccessPolicy policy, AssistantCaller caller)
        {
            List<ContentAudienceEnum> all = new List<ContentAudienceEnum> { ContentAudienceEnum.Public, ContentAudienceEnum.SignedIn, ContentAudienceEnum.Members };
            if (caller.IsSystemAdmin) return all;
            if (caller.UserId == null)
            {
                if (!VisitorsMayBrowse(policy)) throw Denied(401, "Unauthorized", "Sign in to see the assistants.", "SignIn", null);
                return new List<ContentAudienceEnum> { ContentAudienceEnum.Public };
            }
            AccessTierEnum tier = AccessDecider.TierOf(false, true, true, caller.HasMembership);
            List<ContentAudienceEnum> allowed = all.Where(a => AccessDecider.Meets(tier, AccessDecider.ParticipateTier(policy, FeatureKeyEnum.Assistants, a))).ToList();
            if (allowed.Count == 0) throw Denied(402, "MembershipRequired", "An active membership is required.", "Membership", null);
            return allowed;
        }

        private static ApiErrorException Denied(int status, string error, string message, string required, string? assistantId)
        {
            AccessDeniedResponse body = new AccessDeniedResponse
            {
                Error = error,
                Message = message,
                Required = required,
                Feature = AccessPolicy.FeatureKey(FeatureKeyEnum.Assistants),
                TargetType = assistantId == null ? null : TargetTypeEnum.Assistant.ToString(),
                TargetId = assistantId
            };
            return new ApiErrorException(status, error, message, null, body);
        }

        private async Task ApplyAsync(Assistant a, AssistantRequest r, HashSet<string>? present, bool isCreate, CancellationToken token)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            bool Has(string name)
            {
                return isCreate || (present != null && present.Contains(name));
            }

            if (isCreate || (Has("name") && r.Name != null))
            {
                string name = (r.Name ?? String.Empty).Trim();
                if (name.Length < 1 || name.Length > 100) errors["name"] = "Must be 1 to 100 characters.";
                else a.Name = name;
            }
            if (Has("description"))
            {
                string? description = String.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim();
                if (description != null && description.Length > 1000) errors["description"] = "Must be at most 1000 characters.";
                else a.Description = description;
            }
            if (r.Enabled != null) a.Enabled = r.Enabled.Value;
            if (Has("title"))
            {
                string title = String.IsNullOrWhiteSpace(r.Title) ? a.Name : r.Title.Trim();
                if (title.Length > 80) errors["title"] = "Must be 1 to 80 characters.";
                else a.Title = title;
            }
            if (String.IsNullOrWhiteSpace(a.Title)) a.Title = a.Name.Length > 80 ? a.Name.Substring(0, 80) : a.Name;
            if (r.Avatar != null)
            {
                if (AiFields.Has(r.Avatar, "emoji", out JsonElement emoji)) a.AvatarEmoji = AiFields.Str(emoji, "avatar.emoji", 16, errors);
                if (AiFields.Has(r.Avatar, "mediaId", out JsonElement media))
                {
                    a.AvatarMediaId = AiFields.Str(media, "avatar.mediaId", 64, errors);
                    if (a.AvatarMediaId != null && !a.AvatarMediaId.StartsWith(Constants.MediaIdPrefix, StringComparison.Ordinal)) errors["avatar.mediaId"] = "Must be a media id.";
                }
            }
            if (Has("systemPrompt") && (isCreate || r.SystemPrompt != null))
            {
                string prompt = r.SystemPrompt ?? String.Empty;
                if (prompt.Length > _Ai.App.Settings.Ai.MaxSystemPromptCharacters) errors["systemPrompt"] = "Must be at most " + _Ai.App.Settings.Ai.MaxSystemPromptCharacters + " characters.";
                List<string> unknown = _Token.Matches(prompt).Select(m => m.Groups[1].Value).Where(t => !PromptTokens.Contains(t)).Distinct().ToList();
                if (unknown.Count > 0) errors["systemPrompt"] = "Unknown tokens: " + String.Join(", ", unknown.Select(t => "{" + t + "}")) + ". Allowed: " + String.Join(", ", PromptTokens.Select(t => "{" + t + "}")) + ".";
                a.SystemPrompt = prompt;
            }
            if (r.Generation != null)
            {
                if (AiFields.Has(r.Generation, "temperature", out JsonElement t)) a.Temperature = AiFields.Double(t, "generation.temperature", 0, 2, errors);
                if (AiFields.Has(r.Generation, "topP", out JsonElement p)) a.TopP = AiFields.Double(p, "generation.topP", 0, 1, errors);
            }
            if (r.Audience != null) a.Audience = r.Audience.Value;
            if (r.Knowledge != null) ApplyKnowledge(a, r.Knowledge, errors);
            if (r.Tools != null) ApplyTools(a, r.Tools, errors);
            if (r.Limits != null)
            {
                Dictionary<string, JsonElement> l = r.Limits;
                if (AiFields.Has(l, "messagesPerMemberPerDay", out JsonElement v)) a.MessagesPerMemberPerDay = AiFields.Int(v, "limits.messagesPerMemberPerDay", 0, 10000, errors) ?? 50;
                if (AiFields.Has(l, "messagesPerVisitorPerDay", out v)) a.MessagesPerVisitorPerDay = AiFields.Int(v, "limits.messagesPerVisitorPerDay", 0, 1000, errors) ?? 10;
                if (AiFields.Has(l, "maxMessagesPerConversation", out v)) a.MaxMessagesPerConversation = AiFields.Int(v, "limits.maxMessagesPerConversation", 2, 1000, errors) ?? 100;
                if (AiFields.Has(l, "maxTokensPerReply", out v)) a.MaxTokensPerReply = AiFields.Int(v, "limits.maxTokensPerReply", 16, 32000, errors) ?? 800;
                if (AiFields.Has(l, "maxMessageCharacters", out v)) a.MaxMessageCharacters = AiFields.Int(v, "limits.maxMessageCharacters", 1, 32000, errors) ?? 4000;
                if (AiFields.Has(l, "conversationRetentionDays", out v)) a.ConversationRetentionDays = AiFields.Int(v, "limits.conversationRetentionDays", 1, 3650, errors) ?? 180;
            }
            if (r.Streaming != null) a.Streaming = r.Streaming.Value;
            if (r.ShowReasoning != null) a.ShowReasoning = r.ShowReasoning.Value;
            if (r.StarterPrompts != null)
            {
                List<string> prompts = r.StarterPrompts.Select(s => (s ?? String.Empty).Trim()).Where(s => s.Length > 0).ToList();
                if (prompts.Count > AiConstants.MaxStarterPrompts) errors["starterPrompts"] = "At most " + AiConstants.MaxStarterPrompts + " prompts.";
                else if (prompts.Any(s => s.Length > 200)) errors["starterPrompts"] = "Each prompt must be 1 to 200 characters.";
                else a.StarterPrompts = prompts;
            }
            if (Has("disclaimer"))
            {
                string? disclaimer = String.IsNullOrWhiteSpace(r.Disclaimer) ? null : r.Disclaimer.Trim();
                if (disclaimer != null && disclaimer.Length > 500) errors["disclaimer"] = "Must be at most 500 characters.";
                else a.Disclaimer = disclaimer;
            }
            if (r.Embed != null)
            {
                Dictionary<string, JsonElement> e = r.Embed;
                if (AiFields.Has(e, "enabled", out JsonElement v)) a.EmbedEnabled = AiFields.Bool(v, "embed.enabled", errors) ?? false;
                if (AiFields.Has(e, "allowedOrigins", out v))
                {
                    // https://host[:port], or plain http only for localhost and 127.0.0.1 (local development, any port).
                    List<string> entered = AiFields.Strings(v, "embed.allowedOrigins", AiConstants.MaxEmbedOrigins, errors);
                    List<string> origins = new List<string>();
                    for (int i = 0; i < entered.Count; i++)
                    {
                        string? normalized = TrellisKit.Core.Helpers.EmbedOrigins.Normalize(entered[i]);
                        if (normalized == null) errors["embed.allowedOrigins[" + i + "]"] = TrellisKit.Core.Helpers.EmbedOrigins.Problem(entered[i]);
                        else if (!origins.Contains(normalized, StringComparer.Ordinal)) origins.Add(normalized);
                    }
                    if (errors.Keys.Any(k => k.StartsWith("embed.allowedOrigins[", StringComparison.Ordinal))) errors["embed.allowedOrigins"] = "Each origin must be https://host[:port]; plain http is allowed only for localhost and 127.0.0.1.";
                    else a.EmbedOrigins = origins;
                }
                if (AiFields.Has(e, "launcherLabel", out v)) a.EmbedLauncherLabel = AiFields.Str(v, "embed.launcherLabel", 40, errors);
                if (AiFields.Has(e, "position", out v))
                {
                    string? position = AiFields.Str(v, "embed.position", 20, errors) ?? "BottomRight";
                    if (position != "BottomRight" && position != "BottomLeft") errors["embed.position"] = "Must be BottomRight or BottomLeft.";
                    else a.EmbedPosition = position;
                }
            }
            if (a.EmbedEnabled && a.Audience != ContentAudienceEnum.Public) errors["embed.enabled"] = "Only Public assistants can be embedded.";
            if (r.ModerationMode != null) a.ModerationMode = r.ModerationMode.Value;
            if (r.Budget != null)
            {
                if (AiFields.Has(r.Budget, "monthlyUsd", out JsonElement v)) a.MonthlyBudgetUsd = AiFields.Double(v, "budget.monthlyUsd", 0, 100000000, errors);
                if (AiFields.Has(r.Budget, "hardStop", out v)) a.BudgetHardStop = AiFields.Bool(v, "budget.hardStop", errors) ?? false;
            }
            if (r.Models != null) await ApplyModelsAsync(a, r.Models, errors, token).ConfigureAwait(false);
            if (!a.Models.ContainsKey(AssistantPurposeEnum.Chat)) errors["models.chat"] = "Required: a Completion endpoint.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
        }

        private async Task ApplyModelsAsync(Assistant a, Dictionary<string, JsonElement> models, Dictionary<string, string> errors, CancellationToken token)
        {
            Dictionary<AssistantPurposeEnum, string> next = new Dictionary<AssistantPurposeEnum, string>(a.Models);
            foreach (KeyValuePair<string, JsonElement> pair in models)
            {
                if (!Enum.TryParse(pair.Key, true, out AssistantPurposeEnum purpose) || !Enum.IsDefined(purpose) || purpose == AssistantPurposeEnum.EndpointTest || purpose == AssistantPurposeEnum.HealthCheck)
                {
                    errors["models." + pair.Key] = "Unknown purpose.";
                    continue;
                }
                string field = "models." + Char.ToLowerInvariant(purpose.ToString()[0]) + purpose.ToString().Substring(1);
                string? id = AiFields.Str(pair.Value, field, 64, errors);
                if (id == null || id.Length == 0) next.Remove(purpose);
                else next[purpose] = id;
            }
            List<ModelEndpoint> endpoints = await _Ai.App.Database.Ai.Endpoints.ReadManyAsync(next.Values.Distinct(), token).ConfigureAwait(false);
            foreach (KeyValuePair<AssistantPurposeEnum, string> pair in next)
            {
                string field = "models." + Char.ToLowerInvariant(pair.Key.ToString()[0]) + pair.Key.ToString().Substring(1);
                ModelEndpoint? endpoint = endpoints.FirstOrDefault(e => e.Id == pair.Value);
                if (endpoint == null)
                {
                    errors[field] = "Unknown endpoint.";
                    continue;
                }
                ModelEndpointKindEnum expected = pair.Key == AssistantPurposeEnum.Embedding ? ModelEndpointKindEnum.Embedding : pair.Key == AssistantPurposeEnum.Rerank ? ModelEndpointKindEnum.Rerank : ModelEndpointKindEnum.Completion;
                if (endpoint.Kind != expected) throw AiErrors.KindMismatch(field, expected.ToString());
            }
            a.Models = next;
        }

        private void ApplyTools(Assistant a, Dictionary<string, JsonElement> t, Dictionary<string, string> errors)
        {
            if (AiFields.Has(t, "enabled", out JsonElement v)) a.ToolsEnabled = AiFields.Bool(v, "tools.enabled", errors) ?? false;
            if (AiFields.Has(t, "allowed", out v))
            {
                List<string> entered = AiFields.IsNull(v) ? new List<string>() : AiFields.Strings(v, "tools.allowed", Math.Max(1, _Ai.Tools.Tools.Count + 10), errors);
                List<string> allowed = _Ai.Tools.NormalizeAllowed(entered, out List<string> unknown);
                if (unknown.Count > 0)
                    errors["tools.allowed"] = "Unknown tools: " + String.Join(", ", unknown) + ". Registered: " + String.Join(", ", _Ai.Tools.Tools.Select(x => x.Name)) + ".";
                else a.AllowedTools = allowed;
            }
            if (AiFields.Has(t, "maxToolIterations", out v)) a.MaxToolIterations = AiFields.Int(v, "tools.maxToolIterations", 1, 10, errors) ?? 4;
            if (AiFields.Has(t, "maxToolCallsPerTurn", out v)) a.MaxToolCallsPerTurn = AiFields.Int(v, "tools.maxToolCallsPerTurn", 1, AiConstants.MaxToolCallsPerReply, errors) ?? 6;
            if (AiFields.Has(t, "toolCallTimeoutMs", out v)) a.ToolCallTimeoutMs = AiFields.Int(v, "tools.toolCallTimeoutMs", 100, 60000, errors) ?? 15000;
            if (AiFields.Has(t, "maxToolOutputCharacters", out v)) a.MaxToolOutputCharacters = AiFields.Int(v, "tools.maxToolOutputCharacters", 200, 100000, errors) ?? 8000;
            if (AiFields.Has(t, "maxTokensPerTurn", out v))
            {
                int? tokens = AiFields.Int(v, "tools.maxTokensPerTurn", 0, 1000000, errors);
                if (tokens != null && tokens.Value != 0 && tokens.Value < 1000) errors["tools.maxTokensPerTurn"] = "Must be 0 (no extra cap) or 1000 to 1000000.";
                else a.MaxTokensPerTurn = tokens ?? 32000;
            }
            if (AiFields.Has(t, "showToolActivity", out v)) a.ShowToolActivity = AiFields.Bool(v, "tools.showToolActivity", errors) ?? true;
            if (a.ToolsEnabled && a.AllowedTools.Count == 0 && !errors.ContainsKey("tools.allowed")) errors["tools.allowed"] = "Choose at least one tool, or turn tools off.";
        }

        private void ApplyKnowledge(Assistant a, Dictionary<string, JsonElement> k, Dictionary<string, string> errors)
        {
            if (AiFields.Has(k, "enabled", out JsonElement v)) a.KnowledgeEnabled = AiFields.Bool(v, "knowledge.enabled", errors) ?? false;
            if (AiFields.Has(k, "mode", out v)) a.KnowledgeMode = AiFields.Enum<AssistantKnowledgeModeEnum>(v, "knowledge.mode", errors) ?? AssistantKnowledgeModeEnum.Keyword;
            if (AiFields.Has(k, "maxContextItems", out v)) a.MaxContextItems = AiFields.Int(v, "knowledge.maxContextItems", 1, 50, errors) ?? 6;
            if (AiFields.Has(k, "maxContextCharacters", out v)) a.MaxContextCharacters = AiFields.Int(v, "knowledge.maxContextCharacters", 500, 200000, errors) ?? 12000;
            if (AiFields.Has(k, "citations", out v)) a.Citations = AiFields.Bool(v, "knowledge.citations", errors) ?? true;
            if (!AiFields.Has(k, "sources", out v)) return;
            List<KnowledgeSourceRequest>? sources;
            try
            {
                sources = AiFields.IsNull(v) ? new List<KnowledgeSourceRequest>() : v.Deserialize<List<KnowledgeSourceRequest>>(ApiJson.Api);
            }
            catch (JsonException)
            {
                errors["knowledge.sources"] = "Must be a list of sources.";
                return;
            }
            sources ??= new List<KnowledgeSourceRequest>();
            if (sources.Count > _Ai.App.Settings.Ai.MaxKnowledgeSources)
            {
                errors["knowledge.sources"] = "At most " + _Ai.App.Settings.Ai.MaxKnowledgeSources + " sources.";
                return;
            }
            List<AssistantKnowledgeSource> next = new List<AssistantKnowledgeSource>();
            for (int i = 0; i < sources.Count; i++)
            {
                KnowledgeSourceRequest s = sources[i];
                string path = "knowledge.sources[" + i + "]";
                TargetTypeEnum type = s.TargetType ?? TargetTypeEnum.FeedPost;
                if (type != TargetTypeEnum.FeedPost && !KnowledgeTypes.IsAvailable(_Ai.App, type))
                    errors[path + ".targetType"] = type + " is not available; use " + String.Join(", ", KnowledgeTypes.Available(_Ai.App)) + ".";
                List<string> labels = (s.Labels ?? new List<string>()).Select(l => (l ?? String.Empty).Trim()).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                Dictionary<string, string> tags = (s.Tags ?? new Dictionary<string, string>()).Where(t => !String.IsNullOrWhiteSpace(t.Key)).ToDictionary(t => t.Key.Trim(), t => (t.Value ?? String.Empty).Trim(), StringComparer.OrdinalIgnoreCase);
                if (labels.Count > 20 || labels.Any(l => l.Length > 64)) errors[path + ".labels"] = "At most 20 labels of up to 64 characters.";
                if (tags.Count > 20 || tags.Any(t => t.Key.Length > 64 || t.Value.Length > 256)) errors[path + ".tags"] = "At most 20 tags.";
                int maxItems = s.MaxItems ?? 200;
                if (maxItems < 1 || maxItems > 2000) errors[path + ".maxItems"] = "Must be 1 to 2000.";
                string id = !String.IsNullOrEmpty(s.Id) && a.Sources.Any(x => x.Id == s.Id) ? s.Id : IdGenerator.Generate(AiConstants.KnowledgeSourceIdPrefix);
                next.Add(new AssistantKnowledgeSource { Id = id, AssistantId = a.Id, Position = i, TargetType = type, Labels = labels, LabelMatch = s.LabelMatch ?? LabelMatchEnum.All, Tags = tags, MaxItems = Math.Clamp(maxItems, 1, 2000) });
            }
            a.Sources = next;
        }

        #endregion
    }
}
