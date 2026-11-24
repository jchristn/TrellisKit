namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Serialization;
    using TrellisKit.Server.Services.Ai.Benchmark;

    /// <summary>
    /// Administrator-editable prompts of one property (docs/INTEGRATION.md owner requirement R7). A prompt's text is the
    /// administrator's published version, else the property's ai/prompts/&lt;key&gt;.md, else the engine default in
    /// <see cref="PromptCatalog"/>. Saving creates a draft; publishing runs the scope benchmark gate
    /// (docs/BENCHMARKS.md) before the draft goes live. Published texts are cached and reloaded after
    /// <see cref="CacheSeconds"/> so every server instance picks up a change. Thread safety: safe for concurrent use; the
    /// cache is replaced atomically, and overlays are per async flow.
    /// </summary>
    public class PromptService
    {
        #region Public-Members

        /// <summary>
        /// Seconds a loaded set of saved prompts is reused. Default 30, minimum 1, maximum 3600.
        /// </summary>
        public int CacheSeconds
        {
            get
            {
                return _CacheSeconds;
            }
            set
            {
                _CacheSeconds = Math.Clamp(value, 1, 3600);
            }
        }

        #endregion

        #region Private-Members

        private const string _VersionPrefix = "apv_";
        private readonly AiServices _Ai;
        private readonly SemaphoreSlim _Load = new SemaphoreSlim(1, 1);
        private readonly AsyncLocal<Dictionary<string, string>?> _Overlay = new AsyncLocal<Dictionary<string, string>?>();
        private PromptSnapshot? _Snapshot = null;
        private Dictionary<string, string?>? _PropertyDefaults = null;
        private int _CacheSeconds = 30;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="ai">AI services of the property.</param>
        /// <exception cref="ArgumentNullException">ai is null.</exception>
        public PromptService(AiServices ai)
        {
            _Ai = ai ?? throw new ArgumentNullException(nameof(ai));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The rendered text of a prompt (placeholders substituted).
        /// </summary>
        /// <param name="key">Catalog key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Text.</returns>
        /// <exception cref="ArgumentException">The key is not in the catalog.</exception>
        public async Task<string> GetAsync(string key, CancellationToken token = default)
        {
            await EnsureLoadedAsync(token).ConfigureAwait(false);
            return Cached(key);
        }

        /// <summary>
        /// The rendered text of a prompt from the loaded cache (the defaults when nothing has been loaded yet). Call
        /// <see cref="EnsureLoadedAsync"/> first on request paths.
        /// </summary>
        /// <param name="key">Catalog key.</param>
        /// <returns>Text.</returns>
        /// <exception cref="ArgumentException">The key is not in the catalog.</exception>
        public string Cached(string key)
        {
            PromptDefinition definition = PromptCatalog.Find(key) ?? throw new ArgumentException("Unknown prompt key: " + key, nameof(key));
            Dictionary<string, string>? overlay = _Overlay.Value;
            if (overlay != null && overlay.TryGetValue(definition.Key, out string? candidate)) return Render(candidate);
            PromptSnapshot? snapshot = _Snapshot;
            string raw = snapshot != null && snapshot.Saved.TryGetValue(definition.Key, out AiPromptTemplate? saved) ? saved.Content : DefaultFor(definition);
            return Render(raw);
        }

        /// <summary>
        /// Use other raw texts for some prompts in the current async flow only (the scope benchmark measures a draft this
        /// way without affecting member traffic). Dispose the result to restore the previous texts.
        /// </summary>
        /// <param name="raw">Raw texts (placeholders not substituted) by catalog key.</param>
        /// <returns>Scope that restores the previous overlay.</returns>
        /// <exception cref="ArgumentNullException">raw is null.</exception>
        public IDisposable Overlay(Dictionary<string, string> raw)
        {
            ArgumentNullException.ThrowIfNull(raw);
            Dictionary<string, string>? previous = _Overlay.Value;
            Dictionary<string, string> next = previous == null ? new Dictionary<string, string>(StringComparer.Ordinal) : new Dictionary<string, string>(previous, StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in raw) next[pair.Key] = pair.Value;
            _Overlay.Value = next;
            return new PromptOverlayScope(_Overlay, previous);
        }

        /// <summary>
        /// Where a prompt's default comes from: Property (the property's ai/prompts file) or Engine.
        /// </summary>
        /// <param name="key">Catalog key.</param>
        /// <returns>Property or Engine.</returns>
        public string DefaultSource(string key)
        {
            return PropertyDefault(key) != null ? "Property" : "Engine";
        }

        /// <summary>
        /// Load the saved prompts when the cache is empty or older than <see cref="CacheSeconds"/>.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task EnsureLoadedAsync(CancellationToken token = default)
        {
            PromptSnapshot? snapshot = _Snapshot;
            if (snapshot != null && (DateTime.UtcNow - snapshot.LoadedUtc).TotalSeconds < _CacheSeconds) return;
            await _Load.WaitAsync(token).ConfigureAwait(false);
            try
            {
                snapshot = _Snapshot;
                if (snapshot != null && (DateTime.UtcNow - snapshot.LoadedUtc).TotalSeconds < _CacheSeconds) return;
                Dictionary<string, AiPromptTemplate> saved = await _Ai.App.Database.Ai.Prompts.ReadAllAsync(token).ConfigureAwait(false);
                _Snapshot = new PromptSnapshot(saved, DateTime.UtcNow);
            }
            finally
            {
                _Load.Release();
            }
        }

        /// <summary>
        /// The guard every model call starts with: the engine floor followed by the rendered scope guardrail.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Guard text.</returns>
        public async Task<string?> GuardAsync(CancellationToken token = default)
        {
            try
            {
                return ScopeGuard.Compose(await GetAsync(PromptCatalog.ScopeGuardrail, token).ConfigureAwait(false), _Ai.App.Property.Property.AiPolicy.AllowCode);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // A database outage must never send a call without the guard: fall back to the defaults.
                return ScopeGuard.Compose(Cached(PromptCatalog.ScopeGuardrail), _Ai.App.Property.Property.AiPolicy.AllowCode);
            }
        }

        /// <summary>
        /// GET /api/v1/admin/ai/prompts (SystemAdmin).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Prompt page.</returns>
        /// <exception cref="ArgumentNullException">actor is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden.</exception>
        public async Task<EnumerationResult<AiPromptView>> EnumerateAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            RequireAdmin(actor);
            EnumerationRequest request = _Ai.App.Enumeration.Prepare(query, AiCollections.AdminPrompts, true);
            List<AiPromptView> source = await ListCatalogAsync(actor, token).ConfigureAwait(false);
            return CatalogEnumeration.Page(request, source, item => item.Key, search: item => item.Key + " " + item.Title + " " + item.Description, labels: item => item.Labels, tags: item => item.Tags);
        }

        private async Task<List<AiPromptView>> ListCatalogAsync(ContentActor actor, CancellationToken token = default)
        {
            RequireAdmin(actor);
            Dictionary<string, AiPromptTemplate> saved = await _Ai.App.Database.Ai.Prompts.ReadAllAsync(token).ConfigureAwait(false);
            Dictionary<string, AiPromptTemplateVersion> newest = await _Ai.App.Database.Ai.Prompts.ReadNewestVersionsAsync(token).ConfigureAwait(false);
            List<AiPromptView> views = new List<AiPromptView>();
            foreach (PromptDefinition d in PromptCatalog.All) views.Add(await WithDraftAsync(View(d, saved), newest, token).ConfigureAwait(false));
            return views;
        }

        /// <summary>
        /// GET /api/v1/admin/ai/prompts/{key} (SystemAdmin).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="key">Catalog key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Prompt.</returns>
        /// <exception cref="ArgumentNullException">actor is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task<AiPromptView> GetViewAsync(ContentActor actor, string key, CancellationToken token = default)
        {
            RequireAdmin(actor);
            PromptDefinition definition = Require(key);
            Dictionary<string, AiPromptTemplate> saved = await _Ai.App.Database.Ai.Prompts.ReadAllAsync(token).ConfigureAwait(false);
            Dictionary<string, AiPromptTemplateVersion> newest = await _Ai.App.Database.Ai.Prompts.ReadNewestVersionsAsync(token).ConfigureAwait(false);
            return await WithDraftAsync(View(definition, saved), newest, token).ConfigureAwait(false);
        }

        /// <summary>
        /// PUT /api/v1/admin/ai/prompts/{key} (SystemAdmin): save a draft version. The draft is not used by any model call
        /// until <see cref="PublishAsync"/> passes the scope benchmark gate. Audited as ai.prompt.update.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="key">Catalog key.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Prompt.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound.</exception>
        public async Task<AiPromptView> SetAsync(ContentActor actor, RequestInfo info, string key, AiPromptRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            RequireAdmin(actor);
            PromptDefinition definition = Require(key);
            string content = (request.Content ?? String.Empty).Replace("\r\n", "\n").Trim();
            Dictionary<string, string> errors = new Dictionary<string, string>();
            if (content.Length < definition.MinLength || content.Length > definition.MaxLength)
                errors["content"] = definition.MinLength + " to " + definition.MaxLength + " characters.";
            List<string> missing = definition.RequiredWords.Where(w => content.IndexOf(w, StringComparison.OrdinalIgnoreCase) < 0).ToList();
            if (missing.Count > 0) errors["content"] = "Must contain: " + String.Join(", ", missing) + ".";
            if (!_Ai.App.Property.Property.AiPolicy.AllowCode && OffScopeDetector.ContainsCode(content)) errors["content"] = "Prompts may not contain code.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            AiPromptTemplateVersion draft = await _Ai.App.Database.Ai.Prompts.SaveDraftAsync(definition.Key, content, IdGenerator.Generate(_VersionPrefix), actor.UserId, DateTime.UtcNow, token).ConfigureAwait(false);
            await _Ai.App.Audit.WriteAsync(info, "ai.prompt.update", "AiPrompt", definition.Key, definition.Title + " (draft version " + draft.Version + ")", token).ConfigureAwait(false);
            return await GetViewAsync(actor, definition.Key, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/ai/prompts/{key}/publish (SystemAdmin): make the pending draft the text every model call
        /// uses, after the deterministic scope gate passes and, when ai.promptPublishRequiresBenchmark is on, a complete
        /// live scope benchmark of that exact draft clears its thresholds. Audited as ai.prompt.publish.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="key">Catalog key.</param>
        /// <param name="request">Body, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Prompt.</returns>
        /// <exception cref="ArgumentNullException">info is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound, 409 NoDraft, DraftChanged, ScopeGateFailed, BenchmarkRequired, BenchmarkRunning, BenchmarkFailed, BenchmarkStale.</exception>
        public async Task<AiPromptView> PublishAsync(ContentActor actor, RequestInfo info, string key, AiPromptPublishRequest? request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(info);
            RequireAdmin(actor);
            PromptDefinition definition = Require(key);
            AiPromptTemplateVersion draft = await RequireDraftAsync(definition.Key, token).ConfigureAwait(false);
            if (!String.IsNullOrWhiteSpace(request?.VersionId) && !String.Equals(request.VersionId.Trim(), draft.Id, StringComparison.Ordinal))
                throw new ApiErrorException(409, "DraftChanged", "A newer draft was saved. Review it before publishing.");
            await _Ai.Benchmarks.EnsurePublishableAsync(definition.Key, draft, token).ConfigureAwait(false);
            AiPromptTemplate? published = await _Ai.App.Database.Ai.Prompts.PublishAsync(definition.Key, draft.Id, actor.UserId, DateTime.UtcNow, token).ConfigureAwait(false);
            if (published == null) throw new ApiErrorException(409, "DraftChanged", "The draft was published or replaced by someone else. Reload and try again.");
            _Snapshot = null;
            await _Ai.App.Audit.WriteAsync(info, "ai.prompt.publish", "AiPrompt", definition.Key, definition.Title + " (version " + published.Version + ")", token).ConfigureAwait(false);
            return await GetViewAsync(actor, definition.Key, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/ai/prompts/{key}/benchmark (SystemAdmin): queue the ai.scopeBenchmark job for the key's
        /// pending draft (or, without a draft, for the prompts in use). Audited as job.run.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="key">Catalog key.</param>
        /// <param name="request">Body, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The queued run.</returns>
        /// <exception cref="ArgumentNullException">info is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound, 409 RunActive, 409 BudgetExceeded.</exception>
        public async Task<JobRunView> BenchmarkAsync(ContentActor actor, RequestInfo info, string key, AiPromptBenchmarkRequest? request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(info);
            RequireAdmin(actor);
            PromptDefinition definition = Require(key);
            AiPromptBenchmarkRequest body = request ?? new AiPromptBenchmarkRequest();
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string role = String.IsNullOrWhiteSpace(body.ReplyRole) ? ScopeBenchmarkParameters.DefaultReplyRole : body.ReplyRole.Trim();
            TaskRoleDefinition? roleDefinition = _Ai.Roles.FindDefinition(role);
            if (roleDefinition == null || roleDefinition.Kind != ModelEndpointKindEnum.Completion) errors["replyRole"] = "A completion task role.";
            string? assistantId = String.IsNullOrWhiteSpace(body.AssistantId) ? null : body.AssistantId.Trim();
            if (assistantId != null && await _Ai.App.Database.Ai.Assistants.ReadAsync(assistantId, token).ConfigureAwait(false) == null) errors["assistantId"] = "Unknown assistant.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            Dictionary<string, AiPromptTemplateVersion> newest = await _Ai.App.Database.Ai.Prompts.ReadNewestVersionsAsync(token).ConfigureAwait(false);
            AiPromptTemplateVersion? draft = newest.TryGetValue(definition.Key, out AiPromptTemplateVersion? v) && IsDraft(v) ? v : null;
            ScopeBenchmarkParameters parameters = new ScopeBenchmarkParameters
            {
                PromptKey = draft == null ? null : definition.Key,
                VersionId = draft?.Id,
                AssistantId = assistantId,
                ReplyRole = role
            };
            return await _Ai.App.Jobs.QueueAsync(actor, info, ScopeBenchmarkService.JobKey, ApiJson.Serialize(parameters), body.DryRun, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/ai/prompts/{key}/reset (SystemAdmin): return to the default. Audited as ai.prompt.reset.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="key">Catalog key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Prompt.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task<AiPromptView> ResetAsync(ContentActor actor, RequestInfo info, string key, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(info);
            RequireAdmin(actor);
            PromptDefinition definition = Require(key);
            await _Ai.App.Database.Ai.Prompts.ResetAsync(definition.Key, DefaultFor(definition), IdGenerator.Generate(_VersionPrefix), actor.UserId, DateTime.UtcNow, token).ConfigureAwait(false);
            _Snapshot = null;
            await _Ai.App.Audit.WriteAsync(info, "ai.prompt.reset", "AiPrompt", definition.Key, definition.Title, token).ConfigureAwait(false);
            return await GetViewAsync(actor, definition.Key, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/ai/prompts/{key}/versions/enumerate (SystemAdmin): newest first.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="key">Catalog key.</param>
        /// <param name="query">Query, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">actor is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task<EnumerationResult<AiPromptVersionView>> VersionsAsync(ContentActor actor, string key, EnumerationQuery? query, CancellationToken token = default)
        {
            RequireAdmin(actor);
            PromptDefinition definition = Require(key);
            EnumerationRequest request = _Ai.App.Enumeration.Prepare(query, AiCollections.AdminPromptVersions, true);
            EnumerationResult<AiPromptTemplateVersion> page = await _Ai.App.Database.Ai.Prompts.EnumerateVersionsAsync(request, definition.Key, token).ConfigureAwait(false);
            Dictionary<string, AiPromptTemplateVersion> newest = await _Ai.App.Database.Ai.Prompts.ReadNewestVersionsAsync(token).ConfigureAwait(false);
            string? pendingId = newest.TryGetValue(definition.Key, out AiPromptTemplateVersion? latest) && IsDraft(latest) ? latest.Id : null;
            return page.With(page.Objects.Select(v => VersionView(v, v.Id == pendingId)).ToList());
        }

        /// <summary>
        /// Whether a version is a pending draft (unpublished and not a reset).
        /// </summary>
        /// <param name="version">Version.</param>
        /// <returns>True for a draft.</returns>
        /// <exception cref="ArgumentNullException">version is null.</exception>
        public static bool IsDraft(AiPromptTemplateVersion version)
        {
            ArgumentNullException.ThrowIfNull(version);
            return version.PublishedUtc == null && !version.IsReset;
        }

        #endregion

        #region Private-Methods

        private static void RequireAdmin(ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (!actor.IsSystemAdmin) throw new ApiErrorException(403, "Forbidden", "Only administrators can read or change AI prompts.");
        }

        private static PromptDefinition Require(string key)
        {
            return PromptCatalog.Find(key) ?? throw ApiErrorException.NotFound("Prompt");
        }

        private async Task<AiPromptTemplateVersion> RequireDraftAsync(string key, CancellationToken token)
        {
            Dictionary<string, AiPromptTemplateVersion> newest = await _Ai.App.Database.Ai.Prompts.ReadNewestVersionsAsync(token).ConfigureAwait(false);
            if (!newest.TryGetValue(key, out AiPromptTemplateVersion? version) || !IsDraft(version))
                throw new ApiErrorException(409, "NoDraft", "There is no draft to publish. Save a draft first.");
            return version;
        }

        private async Task<AiPromptView> WithDraftAsync(AiPromptView view, Dictionary<string, AiPromptTemplateVersion> newest, CancellationToken token)
        {
            view.RequiresBenchmark = _Ai.App.Settings.Ai.PromptPublishRequiresBenchmark;
            if (!newest.TryGetValue(view.Key, out AiPromptTemplateVersion? version) || !IsDraft(version)) return view;
            view.Draft = VersionView(version, true);
            view.DraftBenchmark = await _Ai.App.Jobs.FindLatestRunAsync(ScopeBenchmarkService.JobKey, "versionId", version.Id, token).ConfigureAwait(false);
            return view;
        }

        private static AiPromptVersionView VersionView(AiPromptTemplateVersion v, bool pending)
        {
            return new AiPromptVersionView
            {
                Id = v.Id,
                Key = v.Key,
                Version = v.Version,
                Content = v.Content,
                IsReset = v.IsReset,
                IsDraft = pending,
                CreatedByUserId = v.CreatedByUserId,
                CreatedUtc = v.CreatedUtc,
                PublishedUtc = v.PublishedUtc
            };
        }

        private AiPromptView View(PromptDefinition definition, Dictionary<string, AiPromptTemplate> saved)
        {
            string? propertyDefault = PropertyDefault(definition.Key);
            saved.TryGetValue(definition.Key, out AiPromptTemplate? current);
            return new AiPromptView
            {
                Key = definition.Key,
                Title = definition.Title,
                Description = definition.Description,
                Content = current?.Content ?? propertyDefault ?? definition.DefaultContent,
                DefaultContent = propertyDefault ?? definition.DefaultContent,
                DefaultSource = propertyDefault != null ? "Property" : "Engine",
                IsDefault = current == null,
                Version = current?.Version ?? 0,
                MinLength = definition.MinLength,
                MaxLength = definition.MaxLength,
                Variables = new List<string>(definition.Variables),
                RequiredWords = new List<string>(definition.RequiredWords),
                LockedPrefix = definition.Key == PromptCatalog.ScopeGuardrail ? ScopeGuard.ForPolicy(_Ai.App.Property.Property.AiPolicy.AllowCode) : null,
                UpdatedByUserId = current?.UpdatedByUserId,
                UpdatedUtc = current?.UpdatedUtc
            };
        }

        private string DefaultFor(PromptDefinition definition)
        {
            string content = PropertyDefault(definition.Key) ?? definition.DefaultContent;
            if (_Ai.App.Property.Property.AiPolicy.AllowCode && definition.Key == PromptCatalog.ModerationClassifier && PropertyDefault(definition.Key) == null)
                content = content.Replace("and requests to write, explain, or debug computer code or to act as a general-purpose assistant (category OffScope)", "and requests outside the property's community scope (category OffScope)", StringComparison.Ordinal);
            return content;
        }

        private string? PropertyDefault(string key)
        {
            Dictionary<string, string?>? defaults = _PropertyDefaults;
            if (defaults == null)
            {
                defaults = new Dictionary<string, string?>(StringComparer.Ordinal);
                string directory = _Ai.App.Property.Property.Directory;
                foreach (PromptDefinition d in PromptCatalog.All)
                {
                    string? text = null;
                    if (!String.IsNullOrEmpty(directory))
                    {
                        string path = Path.Combine(directory, "ai", "prompts", d.Key + ".md");
                        if (File.Exists(path))
                        {
                            string read = File.ReadAllText(path).Replace("\r\n", "\n").Trim();
                            if (read.Length >= d.MinLength && read.Length <= d.MaxLength) text = read;
                        }
                    }
                    defaults[d.Key] = text;
                }
                _PropertyDefaults = defaults;
            }

            return defaults.TryGetValue(key, out string? value) ? value : null;
        }

        private string Render(string raw)
        {
            string name = _Ai.App.Property.Name;
            string shortName = String.IsNullOrWhiteSpace(_Ai.App.Property.Property.ShortName) ? name : _Ai.App.Property.Property.ShortName;
            return raw.Replace("{name}", name, StringComparison.Ordinal).Replace("{shortName}", shortName, StringComparison.Ordinal);
        }

        #endregion
    }
}
