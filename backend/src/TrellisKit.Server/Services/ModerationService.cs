namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Moderation (contract sections 9.1, 9.3, and 10.2): auto-hide, moderation actions (Hide, Unhide, Remove,
    /// Restore) with scope and staff-content protection, the queue (one row per reported target), queue detail and
    /// dismiss, the actions log, a member's content history, the open and overdue counts, the overdue notices, and the
    /// removed-content purge. Scope: SystemAdmin sees every scope; FeedModerators (and Moderators with a Global or
    /// Feed scope) see the Feed scope, which holds every Phase 2 report. Thread safety: safe to call concurrently;
    /// auto-hide hides exactly once per episode and overdue notices are claimed with a conditional update.
    /// </summary>
    public class ModerationService
    {
        #region Public-Members

        /// <summary>
        /// The Feed moderation scope (every Phase 2 report).
        /// </summary>
        public const string FeedScope = "Feed";

        /// <summary>
        /// Report scope of forum content (Phase 3 section 11.1); the scope id is the forum.
        /// </summary>
        public const string ForumScope = "Forum";

        /// <summary>
        /// Report scope of assistant messages (docs/AI_CONTRACT.md section 12.3); the scope id is the assistant. Only
        /// SystemAdmin sees and handles these reports.
        /// </summary>
        public const string AssistantScope = "Assistant";

        /// <summary>
        /// Report scope of ideas and idea comments (docs/PHASE5_CONTRACT.md section 11); the scope id is Idea or Comment.
        /// </summary>
        public const string IdeasScopeType = "Ideas";

        /// <summary>
        /// Moderation of the Phase 3 target types (forum threads), set by the forum module; null leaves them unsupported.
        /// </summary>
        public IModerationTargetExtension? Extension { get; set; } = null;

        /// <summary>
        /// Moderation of target types added after Phase 3 (ideas; later messages), consulted by target type after
        /// <see cref="Extension"/>. Register during module attachment only.
        /// </summary>
        public List<IModerationTargetExtension> Extensions { get; } = new List<IModerationTargetExtension>();

        /// <summary>
        /// Actor name recorded for moderation by the break-glass admin token (distinct from "system").
        /// </summary>
        public const string BreakGlassName = "break-glass token";

        /// <summary>
        /// Shortest action reason.
        /// </summary>
        public const int MinReasonLength = 3;

        /// <summary>
        /// Longest action reason.
        /// </summary>
        public const int MaxReasonLength = 500;

        /// <summary>
        /// Longest moderator note.
        /// </summary>
        public const int MaxNoteLength = 500;

        /// <summary>
        /// Days of author history in the queue detail.
        /// </summary>
        public const int AuthorHistoryDays = 90;

        /// <summary>
        /// Clock (tests set a fixed time). Default DateTime.UtcNow. Never null.
        /// </summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public Func<DateTime> Clock
        {
            get { return _Clock; }
            set { _Clock = value ?? throw new ArgumentNullException(nameof(Clock)); }
        }

        #endregion

        #region Private-Members

        private const string _AdminBase = Constants.ApiPrefix + "/admin/moderation";
        private readonly ApplicationServices _Services;
        private Func<DateTime> _Clock = () => DateTime.UtcNow;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="services">Application services (read on each call).</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public ModerationService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The moderation scopes a caller sees: null for every scope (SystemAdmin), the Feed scope for feed
        /// moderators, and none otherwise.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <returns>Scope types, or null for all.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        public static List<string>? ScopeTypesFor(ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (actor.IsSystemAdmin) return null;
            List<string> scopes = new List<string>();
            if (actor.IsFeedModerator) scopes.Add(FeedScope);
            if (actor.AllForumsModerator) scopes.Add(ForumScope);
            else scopes.AddRange(actor.ModeratedForumIds.OrderBy(i => i, StringComparer.Ordinal).Select(id => ForumScope + ":" + id));
            return scopes;
        }

        /// <summary>
        /// The report scope entries a caller moderates: <see cref="ScopeTypesFor"/> plus the content target provider
        /// scopes (Directory, Events, Library, or "{scope}/Comment" for comment-only moderation;
        /// <see cref="ContentTargetRegistry.ScopesFor"/>) and the scopes of the registered moderation extensions (for
        /// example Ideas for SupportAgent). Null means every scope (SystemAdmin).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <returns>Scope entries, or null for every scope.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        public List<string>? ScopesFor(ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            List<string>? scopes = ScopeTypesFor(actor);
            if (scopes == null) return null;
            foreach (string scope in _Services.ContentTargets.ScopesFor(actor)) if (!scopes.Contains(scope)) scopes.Add(scope);
            foreach (IModerationTargetExtension extension in Extensions)
                foreach (string scope in extension.ScopesFor(actor))
                    if (!scopes.Contains(scope, StringComparer.Ordinal)) scopes.Add(scope);
            return scopes;
        }

        /// <summary>
        /// Auto-hide a content target provider object after a report when the provider allows it
        /// (<see cref="ContentTargetProvider.AllowsAutoHide"/>) and moderation.autoHideReportThreshold distinct members
        /// have open reports on it. Never hides content authored by a SystemAdmin.
        /// </summary>
        /// <param name="provider">Provider of the type.</param>
        /// <param name="target">Reported object.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the object was hidden by this call.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<bool> TryAutoHideTargetAsync(ContentTargetProvider provider, ModerationTarget target, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(provider);
            ArgumentNullException.ThrowIfNull(target);
            int threshold = _Services.Settings.Moderation.AutoHideReportThreshold;
            if (threshold <= 0 || !provider.AllowsAutoHide(target.TargetType) || target.ModerationState != ModerationStateEnum.None || target.IsDeleted) return false;
            if (!String.IsNullOrEmpty(target.AuthorUserId))
            {
                HashSet<string> admins = await _Services.Summaries.SystemAdminsAsync(new[] { target.AuthorUserId }, token).ConfigureAwait(false);
                if (admins.Contains(target.AuthorUserId)) return false;
            }
            long open = await _Services.Database.Reports.CountOpenOnTargetAsync(target.TargetType, target.TargetId, null, token).ConfigureAwait(false);
            if (open < threshold) return false;
            using (Activity? activity = TrellisKitTelemetry.StartSpan("moderation.auto_hide"))
            {
                string reason = "Reported by " + threshold + (threshold == 1 ? " member" : " members");
                await provider.SetModerationStateAsync(target, ModerationStateEnum.Hidden, null, reason, token).ConfigureAwait(false);
                await _Services.Database.ModerationActions.CreateAsync(new ModerationAction
                {
                    Id = IdGenerator.Generate(Constants.ModerationActionIdPrefix),
                    TargetType = target.TargetType,
                    TargetId = target.TargetId,
                    TargetAuthorUserId = String.IsNullOrEmpty(target.AuthorUserId) ? null : target.AuthorUserId,
                    Action = ModerationActionTypeEnum.AutoHide,
                    Reason = reason,
                    CreatedUtc = _Clock()
                }, token).ConfigureAwait(false);
                await _Services.Forums.Promotions.SyncTargetAsync(target.TargetType, target.TargetId, token).ConfigureAwait(false);
                await _Services.Email.NotifyStaffAsync("staff_content_auto_hidden", "autohide:" + target.TargetType + ":" + target.TargetId + ":" + _Clock().Ticks, token,
                    ("targetType", Email.EmailLabels.TargetType(target.TargetType)), ("link", _Services.Email.Link("/admin/moderation"))).ConfigureAwait(false);
                TrellisKitTelemetry.Add(TrellisKitTelemetry.ModerationActions, 1, "action", "auto_hide", "target_type", target.TargetType.ToString());
                activity?.SetStatus(ActivityStatusCode.Ok);
                return true;
            }
        }

        /// <summary>
        /// The registered extension (not the forum <see cref="Extension"/>) that handles a target type, or null.
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <returns>Extension, or null.</returns>
        public IModerationTargetExtension? ExtensionFor(TargetTypeEnum targetType)
        {
            foreach (IModerationTargetExtension extension in Extensions)
                if (extension.Handles(targetType)) return extension;
            return null;
        }

        /// <summary>
        /// Whether POST /api/v1/admin/moderation/actions can hide and remove a target type (the same rule as its
        /// validation): feed posts, comments, threads with the forum extension, content target providers, and
        /// registered extensions.
        /// </summary>
        /// <param name="targetType">Target type.</param>
        /// <returns>True when Hide and Remove apply.</returns>
        public bool SupportsActions(TargetTypeEnum targetType)
        {
            return targetType == TargetTypeEnum.FeedPost || targetType == TargetTypeEnum.Comment || (targetType == TargetTypeEnum.Thread && Extension != null) ||
                ProviderOf(targetType) != null || ExtensionFor(targetType) != null;
        }

        /// <summary>
        /// Parse an enum name (case-insensitive, never a number).
        /// </summary>
        /// <typeparam name="T">Enum type.</typeparam>
        /// <param name="raw">Text.</param>
        /// <param name="value">Parsed value.</param>
        /// <returns>True when parsed.</returns>
        public static bool TryParseEnum<T>(string? raw, out T value) where T : struct, Enum
        {
            value = default;
            if (String.IsNullOrWhiteSpace(raw)) return false;
            string text = raw.Trim();
            if (Int32.TryParse(text, out int _)) return false;
            return Enum.TryParse(text, true, out value) && Enum.IsDefined(value);
        }

        /// <summary>
        /// Auto-hide (contract section 9.1): hide a FeedPost or Comment with a system AutoHide action when the distinct
        /// member reporters with Open reports reach moderation.autoHideReportThreshold (0 disables). Never for content by
        /// a SystemAdmin; the author is not notified; system Filter reports do not count. Safe under concurrent reports:
        /// exactly one caller hides.
        /// </summary>
        /// <param name="targetType">FeedPost or Comment.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="authorUserId">Author of the content.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when this call hid the content.</returns>
        public async Task<bool> TryAutoHideAsync(TargetTypeEnum targetType, string targetId, string? authorUserId, CancellationToken token = default)
        {
            int threshold = _Services.Settings.Moderation.AutoHideReportThreshold;
            if (threshold <= 0 || String.IsNullOrEmpty(targetId)) return false;
            IModerationTargetExtension? extra = ExtensionFor(targetType);
            if (targetType != TargetTypeEnum.FeedPost && targetType != TargetTypeEnum.Comment && !(targetType == TargetTypeEnum.Thread && Extension != null) && extra == null) return false;
            if (!String.IsNullOrEmpty(authorUserId))
            {
                HashSet<string> admins = await _Services.Summaries.SystemAdminsAsync(new[] { authorUserId }, token).ConfigureAwait(false);
                if (admins.Contains(authorUserId)) return false;
            }
            using (Activity? activity = TrellisKitTelemetry.StartSpan("moderation.auto_hide"))
            {
                string reason = "Reported by " + threshold + (threshold == 1 ? " member" : " members");
                ModerationAction action = new ModerationAction
                {
                    Id = IdGenerator.Generate(Constants.ModerationActionIdPrefix),
                    TargetType = targetType,
                    TargetId = targetId,
                    TargetAuthorUserId = authorUserId,
                    Action = ModerationActionTypeEnum.AutoHide,
                    Reason = reason,
                    CreatedUtc = _Clock()
                };
                if (!await _Services.Database.Reports.TryAutoHideAsync(action, threshold, token).ConfigureAwait(false)) return false;
                await _Services.Email.NotifyStaffAsync("staff_content_auto_hidden", "autohide:" + action.Id, token, ("targetType", Email.EmailLabels.TargetType(targetType)), ("link", _Services.Email.Link("/admin/moderation"))).ConfigureAwait(false);
                if (targetType == TargetTypeEnum.FeedPost) await _Services.Feed.SetModerationStateAsync(targetId, ModerationStateEnum.Hidden, null, reason, token).ConfigureAwait(false);
                else if (targetType == TargetTypeEnum.Thread)
                {
                    ModerationTarget? thread = await Extension!.LoadAsync(targetType, targetId, token).ConfigureAwait(false);
                    if (thread != null) await Extension.SetStateAsync(thread, ModerationStateEnum.Hidden, null, reason, token).ConfigureAwait(false);
                }
                else if (extra != null)
                {
                    ModerationTarget? loaded = await extra.LoadAsync(targetType, targetId, token).ConfigureAwait(false);
                    if (loaded != null) await extra.SetStateAsync(loaded, ModerationStateEnum.Hidden, null, reason, token).ConfigureAwait(false);
                }
                else await _Services.Comments.SetModerationStateAsync(targetId, ModerationStateEnum.Hidden, null, reason, token).ConfigureAwait(false);
                TrellisKitTelemetry.Add(TrellisKitTelemetry.ModerationActions, 1, "action", "auto_hide", "target_type", targetType.ToString());
                activity?.SetStatus(ActivityStatusCode.Ok);
                return true;
            }
        }

        /// <summary>
        /// POST /api/v1/admin/moderation/actions (contract section 9.3).
        /// </summary>
        /// <param name="actor">Moderator.</param>
        /// <param name="info">Request information (audit).</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>{ action, target }.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound, 409 InvalidModerationState.</exception>
        public async Task<ModerationActionResult> ActAsync(ContentActor actor, RequestInfo info, ModerationActionRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            using (Activity? activity = TrellisKitTelemetry.StartSpan("moderation.action"))
            {
                Dictionary<string, string> errors = new Dictionary<string, string>();
                TargetTypeEnum targetType = TargetTypeEnum.FeedPost;
                if (!TryParseEnum(request.TargetType, out targetType) || (targetType != TargetTypeEnum.FeedPost && targetType != TargetTypeEnum.Comment && !(targetType == TargetTypeEnum.Thread && Extension != null) &&
                    ProviderOf(targetType) == null && ExtensionFor(targetType) == null))
                    errors["targetType"] = "Use FeedPost, Thread, Comment" + ProviderTypeList() + String.Concat(Extensions.SelectMany(e => Enum.GetValues<TargetTypeEnum>().Where(e.Handles)).Select(t => ", " + t)) + ".";
                string targetId = (request.TargetId ?? String.Empty).Trim();
                if (targetId.Length == 0) errors["targetId"] = "Required.";
                ModerationActionTypeEnum kind = ModerationActionTypeEnum.Hide;
                if (!TryParseEnum(request.Action, out kind) || kind == ModerationActionTypeEnum.AutoHide || kind == ModerationActionTypeEnum.Dismiss) errors["action"] = "Use Hide, Unhide, Remove, or Restore.";
                string reason = (request.Reason ?? String.Empty).Trim();
                if (reason.Length < MinReasonLength || reason.Length > MaxReasonLength) errors["reason"] = "Use " + MinReasonLength + " to " + MaxReasonLength + " characters.";
                string? note = NormalizeNote(request.Note, errors);
                if (errors.Count > 0) throw ApiErrorException.Validation(errors);
                RequireModerator(actor);
                await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, actor.IsStaffFor(TargetTypeEnum.ModerationAction), token, TargetTypeEnum.ModerationAction).ConfigureAwait(false);

                IModerationTargetExtension? extra = ExtensionFor(targetType);
                ModerationTarget target = await LoadTargetAsync(targetType, targetId, token).ConfigureAwait(false);
                if (target.IsDeleted) throw ApiErrorException.NotFound(targetType == TargetTypeEnum.FeedPost ? "Feed post" : targetType == TargetTypeEnum.Thread ? "Thread" : targetType == TargetTypeEnum.Comment ? "Comment" : targetType.ToString());
                HashSet<string> admins = await _Services.Summaries.SystemAdminsAsync(new[] { target.AuthorUserId }, token).ConfigureAwait(false);
                if (!CanAct(actor, target, admins.Contains(target.AuthorUserId)))
                    throw ApiErrorException.Forbidden(actor.IsFeedModerator || actor.ModeratesAnyForum || actor.ModeratesAnyTargetScope || HoldsExtensionScope(actor) ? "Moderators cannot act on content by a SystemAdmin or outside their scope." : "You cannot moderate this content.");
                ModerationStateEnum next = NextState(kind, target.ModerationState, target.IsPurged);

                if (kind == ModerationActionTypeEnum.Restore) await _Services.Media.RestoreTargetAsync(targetType, targetId, token).ConfigureAwait(false);
                if (targetType == TargetTypeEnum.FeedPost) await _Services.Feed.SetModerationStateAsync(targetId, next, actor.UserId, reason, token).ConfigureAwait(false);
                else if (targetType == TargetTypeEnum.Thread) await Extension!.SetStateAsync(target, next, actor.UserId, reason, token).ConfigureAwait(false);
                else if (ProviderOf(targetType) != null)
                {
                    await ProviderOf(targetType)!.SetModerationStateAsync(target, next, actor.UserId, reason, token).ConfigureAwait(false);
                    target.State = next;
                    await _Services.Forums.Promotions.SyncTargetAsync(targetType, target.TargetId, token).ConfigureAwait(false);
                }
                else if (extra != null) await extra.SetStateAsync(target, next, actor.UserId, reason, token).ConfigureAwait(false);
                else await _Services.Comments.SetModerationStateAsync(targetId, next, actor.UserId, reason, token).ConfigureAwait(false);
                if (kind == ModerationActionTypeEnum.Remove) await _Services.Media.RetainTargetAsync(targetType, targetId, token).ConfigureAwait(false);

                DateTime now = _Clock();
                string actionId = IdGenerator.Generate(Constants.ModerationActionIdPrefix);
                bool notify = request.NotifyAuthor ?? (kind == ModerationActionTypeEnum.Remove || kind == ModerationActionTypeEnum.Restore);
                bool notified = false;
                if (notify && kind == ModerationActionTypeEnum.Remove && (targetType == TargetTypeEnum.FeedPost || targetType == TargetTypeEnum.Comment))
                {
                    User? author = await _Services.Database.Users.ReadAsync(target.AuthorUserId, token).ConfigureAwait(false);
                    string excerpt = target.Post?.Excerpt ?? target.Comment?.Excerpt ?? String.Empty;
                    await _Services.Email.SendToMemberAsync("content_removed", author, "removed:" + actionId, token, ("contentType", targetType == TargetTypeEnum.FeedPost ? "post" : "comment"),
                        ("excerpt", excerpt.Length > 80 ? excerpt.Substring(0, 80) : excerpt), ("reason", reason)).ConfigureAwait(false);
                }
                if (notify && !String.IsNullOrEmpty(target.AuthorUserId))
                {
                    Notification? created = await _Services.Notifications.CreateAsync(new NotificationDraft
                    {
                        RecipientUserId = target.AuthorUserId,
                        Type = NotificationTypeEnum.ModerationAction,
                        TargetType = targetType,
                        TargetId = targetId,
                        FeedPostId = target.FeedPostId,
                        CommentId = target.Comment?.Id,
                        ParentCommentId = target.Comment?.ParentCommentId,
                        ThreadId = target.Thread?.Id ?? (target.Comment != null && target.Comment.TargetType == TargetTypeEnum.Thread ? target.Comment.TargetId : null),
                        ForumId = target.ForumId,
                        Path = target.ExtensionPath ?? (target.Thread != null ? "/threads/" + target.Thread.Id
                            : (target.Comment != null && target.Comment.TargetType == TargetTypeEnum.Thread ? "/threads/" + target.Comment.TargetId + "?comment=" + target.Comment.Id : ProviderPath(target))),
                        SubjectNoun = target.Thread != null ? "thread" : (ProviderOf(targetType) != null ? Email.EmailLabels.TargetType(targetType) : target.ExtensionNoun),
                        ModerationAction = kind,
                        ModerationReason = reason,
                        Body = target.Post?.Excerpt ?? target.Comment?.Excerpt ?? target.Thread?.Title ?? target.Title ?? target.ExtensionExcerpt
                    }, token).ConfigureAwait(false);
                    notified = created != null;
                }
                ModerationAction action = await _Services.Database.ModerationActions.CreateAsync(new ModerationAction
                {
                    Id = actionId,
                    ModeratorUserId = actor.UserId,
                    BreakGlass = actor.IsBreakGlass,
                    TargetType = targetType,
                    TargetId = targetId,
                    TargetAuthorUserId = target.AuthorUserId,
                    Action = kind,
                    Reason = reason,
                    Note = note,
                    NotifiedAuthor = notified,
                    CreatedUtc = now
                }, token).ConfigureAwait(false);
                if (request.Labels != null || request.Tags != null) await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.ModerationAction, action.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
                if (request.ResolveReports ?? true)
                {
                    await _Services.Database.Reports.ResolveOpenAsync(targetType, targetId, ReportStatusEnum.ActionTaken, actor.UserId, note ?? reason, actionId, now, ScopesFor(actor), token).ConfigureAwait(false);
                }
                if (targetType == TargetTypeEnum.FeedPost) await _Services.Feed.RecomputeOpenReportCountAsync(targetId, token).ConfigureAwait(false);
                else if (targetType == TargetTypeEnum.Thread) await Extension!.RecomputeReportsAsync(target, token).ConfigureAwait(false);
                else if (ProviderOf(targetType) != null) await ProviderOf(targetType)!.RecomputeReportsAsync(target, token).ConfigureAwait(false);
                else if (extra != null) await extra.RecomputeReportsAsync(target, token).ConfigureAwait(false);
                string label = ActionLabel(kind);
                await _Services.Audit.WriteAsync(info, "moderation." + label, targetType.ToString(), targetId, reason, token).ConfigureAwait(false);
                TrellisKitTelemetry.Add(TrellisKitTelemetry.ModerationActions, 1, "action", label, "target_type", targetType.ToString());
                activity?.SetTag("moderation.action", label);
                activity?.SetStatus(ActivityStatusCode.Ok);
                return new ModerationActionResult
                {
                    Action = (await ActionViewsAsync(new List<ModerationAction> { action }, true, token).ConfigureAwait(false))[0],
                    Target = await ModeratorTargetViewAsync(actor, target, token).ConfigureAwait(false)
                };
            }
        }

        /// <summary>
        /// GET /api/v1/admin/moderation/queue (ModerationCollections.Queue).
        /// </summary>
        /// <param name="actor">Moderator.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page with exact totals.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 403 Forbidden.</exception>
        public async Task<EnumerationResult<ModerationQueueItem>> QueueAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            RequireModerator(actor);
            EnumerationQuery q = query ?? new EnumerationQuery();
            Dictionary<string, string> errors = new Dictionary<string, string>();
            DateTime now = _Clock();
            ModerationQueueFilter filter = new ModerationQueueFilter { NowUtc = now, ScopeTypes = ScopesFor(actor) };
            string? status = q.FilterString("status");
            if (status != null)
            {
                if (String.Equals(status, "Resolved", StringComparison.OrdinalIgnoreCase)) filter.Resolved = true;
                else if (!String.Equals(status, "Open", StringComparison.OrdinalIgnoreCase)) errors["filters.status"] = "Use Open or Resolved.";
            }
            filter.TargetType = ParseFilterTarget(q, errors, true);
            filter.Reason = q.FilterEnum<ReportReasonEnum>("reason", errors);
            filter.Overdue = q.FilterBool("overdue", errors);
            string? scopeType = q.FilterString("scopeType");
            if (scopeType != null)
            {
                if (String.Equals(scopeType, FeedScope, StringComparison.OrdinalIgnoreCase)) filter.ScopeTypeFilter = FeedScope;
                else if (String.Equals(scopeType, ForumScope, StringComparison.OrdinalIgnoreCase)) filter.ScopeTypeFilter = ForumScope;
                else if (String.Equals(scopeType, AssistantScope, StringComparison.OrdinalIgnoreCase)) filter.ScopeTypeFilter = AssistantScope;
                else if (ProviderScope(scopeType) != null) filter.ScopeTypeFilter = ProviderScope(scopeType);
                else if (Extensions.Any(e => e.ScopeType != null && String.Equals(e.ScopeType, scopeType, StringComparison.OrdinalIgnoreCase)))
                    filter.ScopeTypeFilter = Extensions.First(e => e.ScopeType != null && String.Equals(e.ScopeType, scopeType, StringComparison.OrdinalIgnoreCase)).ScopeType;
                else errors["filters.scopeType"] = "Use Feed, Forum, Assistant" + ProviderScopeList() + String.Concat(Extensions.Where(e => e.ScopeType != null).Select(e => ", " + e.ScopeType)) + ".";
            }
            filter.ForumIds = ForumFilters.List(q, "forumIds") ?? new List<string>();
            if (filter.ForumIds.Count > 50) errors["filters.forumIds"] = "At most 50 forums.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, ModerationCollections.Queue, actor.IsStaffFor(TargetTypeEnum.FeedPost), "queue|" + ScopeKey(filter.ScopeTypes));
            EnumerationResult<ModerationQueueRow> page = await _Services.Database.Reports.EnumerateQueueAsync(request, filter, token).ConfigureAwait(false);
            return page.With(await QueueItemsAsync(actor, page.Objects, filter.ScopeTypes, now, token).ConfigureAwait(false));
        }

        /// <summary>
        /// GET /api/v1/admin/moderation/queue/{targetType}/{targetId}: the target (any state) with bounded previews of
        /// its reports, actions, and revisions (at most 5 each, with enumerate paths) and the author's history.
        /// </summary>
        /// <param name="actor">Moderator.</param>
        /// <param name="rawType">FeedPost, Comment, or User.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Detail.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound.</exception>
        public async Task<ModerationQueueDetail> DetailAsync(ContentActor actor, string? rawType, string targetId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            RequireModerator(actor);
            TargetTypeEnum targetType = ParsePathTarget(rawType);
            ModerationTarget target = await LoadTargetAsync(targetType, targetId, token).ConfigureAwait(false);
            await RequireInScopeAsync(actor, target, token).ConfigureAwait(false);
            return await BuildDetailAsync(actor, target, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/moderation/queue/{targetType}/{targetId}/dismiss: mark the Open reports in the caller's
        /// scope Dismissed (content unchanged), with a Dismiss action row and the moderation.dismiss audit record when
        /// any report was dismissed.
        /// </summary>
        /// <param name="actor">Moderator.</param>
        /// <param name="info">Request information (audit).</param>
        /// <param name="rawType">FeedPost, Comment, or User.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="request">Body (optional note).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The detail after dismissing.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound.</exception>
        public async Task<ModerationQueueDetail> DismissAsync(ContentActor actor, RequestInfo info, string? rawType, string targetId, ModerationDismissRequest? request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            RequireModerator(actor);
            TargetTypeEnum targetType = ParsePathTarget(rawType);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string? note = NormalizeNote(request?.Note, errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            await _Services.Metadata.ValidateAsync(actor, request?.Labels, request?.Tags, actor.IsStaffFor(TargetTypeEnum.ModerationAction), token, TargetTypeEnum.ModerationAction).ConfigureAwait(false);
            ModerationTarget target = await LoadTargetAsync(targetType, targetId, token).ConfigureAwait(false);
            await RequireInScopeAsync(actor, target, token).ConfigureAwait(false);
            using (Activity? activity = TrellisKitTelemetry.StartSpan("moderation.dismiss"))
            {
                DateTime now = _Clock();
                string actionId = IdGenerator.Generate(Constants.ModerationActionIdPrefix);
                int dismissed = await _Services.Database.Reports.ResolveOpenAsync(targetType, target.TargetId, ReportStatusEnum.Dismissed, actor.UserId, note, actionId, now, ScopesFor(actor), token).ConfigureAwait(false);
                if (dismissed > 0)
                {
                    await _Services.Database.ModerationActions.CreateAsync(new ModerationAction
                    {
                        Id = actionId,
                        ModeratorUserId = actor.UserId,
                        BreakGlass = actor.IsBreakGlass,
                        TargetType = targetType,
                        TargetId = target.TargetId,
                        TargetAuthorUserId = target.AuthorUserId,
                        Action = ModerationActionTypeEnum.Dismiss,
                        Reason = note ?? "Reports dismissed",
                        Note = note,
                        CreatedUtc = now
                    }, token).ConfigureAwait(false);
                    if (request != null && (request.Labels != null || request.Tags != null))
                        await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.ModerationAction, actionId, request.Labels, request.Tags, token).ConfigureAwait(false);
                    if (targetType == TargetTypeEnum.FeedPost) await _Services.Feed.RecomputeOpenReportCountAsync(target.TargetId, token).ConfigureAwait(false);
                    else if (ProviderOf(targetType) != null) await ProviderOf(targetType)!.RecomputeReportsAsync(target, token).ConfigureAwait(false);
                    else if (ExtensionFor(targetType) != null) await ExtensionFor(targetType)!.RecomputeReportsAsync(target, token).ConfigureAwait(false);
                    await _Services.Audit.WriteAsync(info, "moderation.dismiss", targetType.ToString(), target.TargetId, dismissed + (dismissed == 1 ? " report" : " reports") + " dismissed", token).ConfigureAwait(false);
                    TrellisKitTelemetry.Add(TrellisKitTelemetry.ModerationActions, 1, "action", "dismiss", "target_type", targetType.ToString());
                }
                activity?.SetTag("moderation.dismissed", dismissed);
                activity?.SetStatus(ActivityStatusCode.Ok);
            }
            return await BuildDetailAsync(actor, target, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Reports of one target in the caller's scope (ModerationCollections.Reports), newest first.
        /// </summary>
        /// <param name="actor">Moderator.</param>
        /// <param name="rawType">FeedPost, Comment, or User.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page with exact totals.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 403 Forbidden.</exception>
        public async Task<EnumerationResult<ReportView>> ReportsAsync(ContentActor actor, string? rawType, string targetId, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            RequireModerator(actor);
            TargetTypeEnum targetType = ParsePathTarget(rawType);
            EnumerationQuery q = query ?? new EnumerationQuery();
            Dictionary<string, string> errors = new Dictionary<string, string>();
            ReportFilter filter = new ReportFilter
            {
                TargetType = targetType,
                TargetId = targetId ?? String.Empty,
                Status = q.FilterEnum<ReportStatusEnum>("status", errors),
                Reason = q.FilterEnum<ReportReasonEnum>("reason", errors),
                ScopeTypes = ScopesFor(actor)
            };
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            if (!actor.IsSystemAdmin) await RequireInScopeAsync(actor, await LoadTargetAsync(targetType, targetId ?? String.Empty, token).ConfigureAwait(false), token).ConfigureAwait(false);
            bool staff = actor.IsStaffFor(TargetTypeEnum.Report);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, ModerationCollections.Reports, staff, "reports|" + targetType + "|" + targetId + "|" + ScopeKey(filter.ScopeTypes));
            EnumerationResult<Report> page = await _Services.Database.Reports.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            return page.With(await ReportViewsAsync(page.Objects, staff, token).ConfigureAwait(false));
        }

        /// <summary>
        /// GET /api/v1/admin/moderation/actions: the actions log (ModerationCollections.Actions).
        /// </summary>
        /// <param name="actor">Moderator.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page with exact totals.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 403 Forbidden.</exception>
        public Task<EnumerationResult<ModerationActionView>> ActionsAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            RequireModerator(actor);
            return ActionsCoreAsync(actor, query, null, null, token);
        }

        /// <summary>
        /// Moderation actions on one target (the queue detail's full list), newest first.
        /// </summary>
        /// <param name="actor">Moderator.</param>
        /// <param name="rawType">FeedPost, Comment, or User.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page with exact totals.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 403 Forbidden.</exception>
        public Task<EnumerationResult<ModerationActionView>> TargetActionsAsync(ContentActor actor, string? rawType, string targetId, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            RequireModerator(actor);
            TargetTypeEnum targetType = ParsePathTarget(rawType);
            return ActionsCoreAsync(actor, query, targetType, targetId ?? String.Empty, token);
        }

        /// <summary>
        /// The messages of an AssistantMessage target's moderation snapshot (docs/AI_CONTRACT.md section 12.3),
        /// oldest first. SystemAdmin only.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="rawType">Must be AssistantMessage.</param>
        /// <param name="targetId">Reported message identifier.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound.</exception>
        public Task<EnumerationResult<AssistantSnapshotMessageView>> SnapshotMessagesAsync(ContentActor actor, string? rawType, string targetId, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            RequireModerator(actor);
            if (ParsePathTarget(rawType) != TargetTypeEnum.AssistantMessage) throw ApiErrorException.NotFound("Snapshot");
            if (!actor.IsSystemAdmin) throw ApiErrorException.Forbidden("Reports on assistant messages are handled by a SystemAdmin.");
            return _Services.Ai.Reports.EnumerateSnapshotAsync(targetId, query, token);
        }

        /// <summary>
        /// GET /api/v1/admin/moderation/content: a member's posts and comments in any state (ModerationCollections.Content).
        /// </summary>
        /// <param name="actor">Moderator.</param>
        /// <param name="query">Query (filters.authorUserId required).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page with exact totals.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 403 Forbidden.</exception>
        public async Task<EnumerationResult<ModerationContentItem>> ContentAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            RequireModerator(actor);
            EnumerationQuery q = query ?? new EnumerationQuery();
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string? author = q.FilterString("authorUserId");
            if (String.IsNullOrWhiteSpace(author)) errors["filters.authorUserId"] = "Required.";
            TargetTypeEnum? targetType = ParseFilterTarget(q, errors, false);
            string? state = q.FilterString("state");
            if (state != null && !new[] { "Visible", "Hidden", "Removed", "Deleted" }.Contains(state, StringComparer.OrdinalIgnoreCase)) errors["filters.state"] = "Use Visible, Hidden, Removed, or Deleted.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            ModerationContentFilter filter = new ModerationContentFilter { AuthorUserId = author!.Trim(), TargetType = targetType, State = state };
            bool staff = actor.IsStaffFor(TargetTypeEnum.FeedPost);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, ModerationCollections.Content, staff, "content|" + filter.AuthorUserId);
            EnumerationResult<ModerationContentRow> page = await _Services.Database.ModerationActions.EnumerateContentAsync(request, filter, token).ConfigureAwait(false);
            List<string> postIds = page.Objects.Where(r => r.TargetType == TargetTypeEnum.FeedPost).Select(r => r.TargetId).ToList();
            List<string> commentIds = page.Objects.Where(r => r.TargetType == TargetTypeEnum.Comment).Select(r => r.TargetId).ToList();
            Dictionary<string, FeedPost> posts = (await _Services.Database.FeedPosts.ReadManyAsync(postIds, token).ConfigureAwait(false)).ToDictionary(p => p.Id, StringComparer.Ordinal);
            Dictionary<string, Comment> comments = (await _Services.Database.Comments.ReadManyAsync(commentIds, token).ConfigureAwait(false)).ToDictionary(c => c.Id, StringComparer.Ordinal);
            Dictionary<string, ObjectMetadata> postMeta = await _Services.Metadata.LoadAsync(TargetTypeEnum.FeedPost, postIds, staff, token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> commentMeta = await _Services.Metadata.LoadAsync(TargetTypeEnum.Comment, commentIds, actor.IsStaffFor(TargetTypeEnum.Comment), token).ConfigureAwait(false);
            DateTime now = _Clock();
            List<ModerationContentItem> items = new List<ModerationContentItem>();
            foreach (ModerationContentRow row in page.Objects)
            {
                ModerationContentItem item = new ModerationContentItem { TargetType = row.TargetType, TargetId = row.TargetId, CreatedUtc = row.CreatedUtc, ReportCount = row.ReportCount };
                ObjectMetadata? metadata = null;
                if (row.TargetType == TargetTypeEnum.FeedPost && posts.TryGetValue(row.TargetId, out FeedPost? post))
                {
                    item.State = post.StateAt(now).ToString();
                    item.FeedPostId = post.Id;
                    item.Excerpt = String.IsNullOrEmpty(post.Excerpt) ? post.Title : post.Excerpt;
                    item.ReactionTotal = post.ReactionTotal;
                    postMeta.TryGetValue(post.Id, out metadata);
                }
                else if (row.TargetType == TargetTypeEnum.Comment && comments.TryGetValue(row.TargetId, out Comment? comment))
                {
                    item.State = CommentState(comment).ToString();
                    item.FeedPostId = comment.TargetType == TargetTypeEnum.FeedPost ? comment.TargetId : null;
                    item.Excerpt = comment.Excerpt;
                    item.ReactionTotal = comment.ReactionTotal;
                    commentMeta.TryGetValue(comment.Id, out metadata);
                }
                item.Labels = metadata?.Labels ?? new List<string>();
                item.Tags = metadata?.Tags ?? new Dictionary<string, string>();
                items.Add(item);
            }
            return page.With(items);
        }

        /// <summary>
        /// GET /api/v1/admin/moderation/summary: open and overdue reports in the caller's scope (admin Home tiles).
        /// </summary>
        /// <param name="actor">Moderator.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>{ open, overdue, openTargets }.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden.</exception>
        public async Task<ModerationSummary> SummaryAsync(ContentActor actor, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            RequireModerator(actor);
            List<string>? scope = ScopesFor(actor);
            return new ModerationSummary
            {
                Open = await _Services.Database.Reports.CountOpenAsync(scope, null, token).ConfigureAwait(false),
                Overdue = await _Services.Database.Reports.CountOpenAsync(scope, _Clock(), token).ConfigureAwait(false),
                OpenTargets = await _Services.Database.Reports.CountOpenTargetsAsync(scope, token).ConfigureAwait(false)
            };
        }

        /// <summary>
        /// The moderation actions preview of the admin feed detail (FeedService.ModerationActionsPreview): at most 5,
        /// newest first, with the enumerate path of the post's actions.
        /// </summary>
        /// <param name="postId">Post identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Preview.</returns>
        public async Task<BoundedPreview<object>> FeedPostActionsPreviewAsync(string postId, CancellationToken token = default)
        {
            ContentActor admin = new ContentActor { IsSystemAdmin = true, IsStaff = true };
            EnumerationResult<ModerationActionView> page = await ActionsCoreAsync(admin, new EnumerationQuery { MaxResults = BoundedPreview<object>.MaxObjects }, TargetTypeEnum.FeedPost, postId ?? String.Empty, token).ConfigureAwait(false);
            return new BoundedPreview<object>
            {
                Objects = page.Objects.Cast<object>().ToList(),
                TotalRecords = page.TotalRecords ?? page.Objects.Count,
                Enumerate = TargetBase(TargetTypeEnum.FeedPost, postId ?? String.Empty) + "/actions/enumerate"
            };
        }

        /// <summary>
        /// The moderation due worker's pass: claim Open reports past their review target that were not notified yet,
        /// and send one ModerationOverdue notification (in-app and push) per reported target to every moderator in the
        /// report's scope.
        /// </summary>
        /// <param name="limit">Most reports per pass.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Reports claimed.</returns>
        public async Task<int> NotifyOverdueAsync(int limit, CancellationToken token = default)
        {
            DateTime now = _Clock();
            List<ModerationOverdueClaim> claims = await _Services.Database.Reports.ClaimOverdueAsync(now, Math.Max(1, limit), token).ConfigureAwait(false);
            if (claims.Count == 0) return 0;
            using (Activity? activity = TrellisKitTelemetry.StartSpan("moderation.overdue"))
            {
                Dictionary<string, Comment> comments = (await _Services.Database.Comments.ReadManyAsync(
                    claims.Where(c => c.TargetType == TargetTypeEnum.Comment).Select(c => c.TargetId).Distinct(StringComparer.Ordinal).ToList(), token).ConfigureAwait(false))
                    .ToDictionary(c => c.Id, StringComparer.Ordinal);
                foreach (IGrouping<string, ModerationOverdueClaim> scope in claims.GroupBy(c => c.ScopeType + "|" + (c.ScopeId ?? "*"), StringComparer.Ordinal))
                {
                    ModerationOverdueClaim sample = scope.First();
                    IModerationTargetExtension? scoped = Extensions.FirstOrDefault(e => e.ScopeType != null && String.Equals(e.ScopeType, sample.ScopeType, StringComparison.Ordinal));
                    List<string> recipients = scoped != null
                        ? await scoped.ModeratorIdsAsync(sample.ScopeId ?? "*", token).ConfigureAwait(false)
                        : String.Equals(sample.ScopeType, ForumScope, StringComparison.Ordinal) && Extension != null && sample.ScopeId != null && sample.ScopeId != "*"
                        ? await Extension.ModeratorIdsAsync(sample.ScopeId, token).ConfigureAwait(false)
                        : ProviderScope(sample.ScopeType) != null
                            ? await ProviderModeratorIdsAsync(sample.ScopeType, scope.Any(c => c.TargetType == TargetTypeEnum.Comment), token).ConfigureAwait(false)
                            : await ModeratorIdsInScopeAsync(sample.ScopeType, token).ConfigureAwait(false);
                    if (recipients.Count == 0) continue;
                    foreach (ModerationOverdueClaim claim in scope.GroupBy(c => c.TargetType + ":" + c.TargetId, StringComparer.Ordinal).Select(g => g.First()))
                    {
                        token.ThrowIfCancellationRequested();
                        comments.TryGetValue(claim.TargetId, out Comment? comment);
                        NotificationDraft draft = new NotificationDraft
                        {
                            Type = NotificationTypeEnum.ModerationOverdue,
                            TargetType = claim.TargetType,
                            TargetId = claim.TargetId,
                            FeedPostId = claim.TargetType == TargetTypeEnum.FeedPost ? claim.TargetId : (comment != null && comment.TargetType == TargetTypeEnum.FeedPost ? comment.TargetId : null),
                            CommentId = claim.TargetType == TargetTypeEnum.Comment ? claim.TargetId : null,
                            ParentCommentId = comment?.ParentCommentId,
                            EventUtc = now,
                            QueuePush = true
                        };
                        await _Services.Notifications.CreateForRecipientsAsync(draft, recipients, token).ConfigureAwait(false);
                    }
                }
                if (claims.Count > 0)
                    await _Services.Email.NotifyStaffAsync("staff_moderation_overdue", "overdue:" + now.ToString("yyyyMMddHH", System.Globalization.CultureInfo.InvariantCulture), token,
                        ("count", claims.Select(c => c.TargetType + ":" + c.TargetId).Distinct(StringComparer.Ordinal).Count().ToString(System.Globalization.CultureInfo.InvariantCulture)), ("link", _Services.Email.Link("/admin/moderation"))).ConfigureAwait(false);
                activity?.SetTag("job.batch_size", claims.Count);
                activity?.SetStatus(ActivityStatusCode.Ok);
            }
            return claims.Count;
        }

        /// <summary>
        /// Count Open reports past their review target (every scope) and publish the trelliskit.moderation.overdue gauge.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Overdue Open reports.</returns>
        public async Task<long> RefreshOverdueGaugeAsync(CancellationToken token = default)
        {
            long overdue = await _Services.Database.Reports.CountOpenAsync(null, _Clock(), token).ConfigureAwait(false);
            TrellisKitTelemetry.SetModerationOverdue(overdue);
            return overdue;
        }

        /// <summary>
        /// Members who moderate a scope: every SystemAdmin, and for the Feed scope every FeedModerator and every
        /// Moderator with a Global or Feed scope (not deleted or banned).
        /// </summary>
        /// <param name="scopeType">Scope type.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Member identifiers.</returns>
        public async Task<List<string>> ModeratorIdsInScopeAsync(string scopeType, CancellationToken token = default)
        {
            List<string> candidates = await _Services.Notifications.ModeratorIdsAsync(token).ConfigureAwait(false);
            if (candidates.Count == 0) return candidates;
            List<RoleAssignment> roles = await _Services.Database.RoleAssignments.EnumerateByUsersAsync(candidates, token).ConfigureAwait(false);
            bool feed = String.Equals(scopeType, FeedScope, StringComparison.OrdinalIgnoreCase);
            return roles.Where(r => r.Role == RoleEnum.SystemAdmin || (r.Role == RoleEnum.Moderator && r.ResourceType == ResourceTypeEnum.Global) || (feed && ActorService.IsFeedModeratorRole(r)))
                .Select(r => r.UserId).Distinct(StringComparer.Ordinal).OrderBy(i => i, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// The content purge worker's pass: posts, threads, ideas, and comments removed or deleted more than
        /// moderation.retainRemovedDays ago with no Open report are purged (media including originals deleted, bodies
        /// and revisions cleared, labels and tags deleted, purgedutc set).
        /// </summary>
        /// <param name="limit">Most items of each type per pass.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Items purged.</returns>
        public async Task<int> PurgeDueAsync(int limit, CancellationToken token = default)
        {
            DateTime now = _Clock();
            DateTime cutoff = now.AddDays(-_Services.Settings.Moderation.RetainRemovedDays);
            List<PurgeCandidate> candidates = await _Services.Database.ModerationActions.ListPurgeCandidatesAsync(cutoff, Math.Max(1, limit), token).ConfigureAwait(false);
            if (candidates.Count == 0) return 0;
            int purged = 0;
            using (Activity? activity = TrellisKitTelemetry.StartSpan("moderation.purge"))
            {
                foreach (PurgeCandidate candidate in candidates)
                {
                    token.ThrowIfCancellationRequested();
                    await _Services.Media.PurgeTargetAsync(candidate.TargetType, candidate.TargetId, token).ConfigureAwait(false);
                    IModerationTargetExtension? owner = candidate.TargetType == TargetTypeEnum.Thread ? Extension : ExtensionFor(candidate.TargetType);
                    bool done = candidate.TargetType == TargetTypeEnum.FeedPost
                        ? await _Services.Database.FeedPosts.PurgeAsync(candidate.TargetId, now, token).ConfigureAwait(false)
                        : candidate.TargetType == TargetTypeEnum.Comment
                            ? await _Services.Database.Comments.PurgeAsync(candidate.TargetId, now, token).ConfigureAwait(false)
                            : owner != null && await owner.PurgeAsync(candidate.TargetId, now, token).ConfigureAwait(false);
                    if (done) purged++;
                }
                activity?.SetTag("job.batch_size", candidates.Count);
                activity?.SetStatus(ActivityStatusCode.Ok);
            }
            return purged;
        }

        #endregion

        #region Private-Methods

        private bool HoldsExtensionScope(ContentActor actor)
        {
            return Extensions.Any(e => e.ScopesFor(actor).Count > 0);
        }

        private void RequireModerator(ContentActor actor)
        {
            if (actor.IsSystemAdmin || actor.IsFeedModerator || actor.ModeratesAnyForum || actor.ModeratesAnyTargetScope || actor.IsDirectoryModerator || actor.IsEventManager || actor.IsLibraryEditor || HoldsExtensionScope(actor)) return;
            throw ApiErrorException.Forbidden("A moderator role for this scope is required.");
        }

        private async Task RequireInScopeAsync(ContentActor actor, ModerationTarget target, CancellationToken token)
        {
            if (actor.IsSystemAdmin) return;
            if (target.TargetType == TargetTypeEnum.AssistantMessage) throw ApiErrorException.Forbidden("Reports on assistant messages are handled by a SystemAdmin.");
            if (target.TargetType != TargetTypeEnum.FeedPost && target.TargetType != TargetTypeEnum.Comment && target.TargetType != TargetTypeEnum.Thread && ProviderOf(target.TargetType) == null && ExtensionFor(target.TargetType) == null)
                throw ApiErrorException.Forbidden("Reports on members are handled by a SystemAdmin.");
            HashSet<string> admins = await _Services.Summaries.SystemAdminsAsync(new[] { target.AuthorUserId }, token).ConfigureAwait(false);
            if (!CanAct(actor, target, admins.Contains(target.AuthorUserId)))
                throw ApiErrorException.Forbidden("Moderators cannot act on content by a SystemAdmin or outside their scope.");
        }

        private bool CanAct(ContentActor actor, ModerationTarget target, bool authorIsSystemAdmin)
        {
            if (actor.IsSystemAdmin) return true;
            ContentTargetProvider? own = ProviderOf(target.TargetType);
            if (own != null) return !authorIsSystemAdmin && own.Moderates(actor, own.ScopeType(target.TargetType));
            if (target.HostType != null)
            {
                ContentTargetProvider? host = ProviderOf(target.HostType.Value);
                return host != null && !authorIsSystemAdmin && host.ModeratesComments(actor, target.HostType.Value);
            }
            if (target.ScopeType != null)
            {
                IModerationTargetExtension? scoped = Extensions.FirstOrDefault(e => e.ScopeType != null && String.Equals(e.ScopeType, target.ScopeType, StringComparison.Ordinal));
                bool? decision = scoped?.CanAct(actor, target, authorIsSystemAdmin);
                if (decision != null) return decision.Value;
            }
            if (target.ForumId != null) return actor.CanModerateForum(target.ForumId) && !authorIsSystemAdmin;
            return ContentAccessService.CanModerate(actor, authorIsSystemAdmin);
        }

        private static ModerationStateEnum NextState(ModerationActionTypeEnum kind, ModerationStateEnum current, bool purged)
        {
            switch (kind)
            {
                case ModerationActionTypeEnum.Hide:
                    if (current != ModerationStateEnum.None) throw InvalidState(current == ModerationStateEnum.Hidden ? "The content is already hidden." : "Removed content cannot be hidden; restore it first.");
                    return ModerationStateEnum.Hidden;
                case ModerationActionTypeEnum.Unhide:
                    if (current != ModerationStateEnum.Hidden) throw InvalidState("Only hidden content can be unhidden.");
                    return ModerationStateEnum.None;
                case ModerationActionTypeEnum.Remove:
                    if (current == ModerationStateEnum.Removed) throw InvalidState("The content is already removed.");
                    return ModerationStateEnum.Removed;
                default:
                    if (current != ModerationStateEnum.Removed) throw InvalidState("Only removed content can be restored.");
                    if (purged) throw InvalidState("The content was purged after the retention period and cannot be restored.");
                    return ModerationStateEnum.None;
            }
        }

        private static ApiErrorException InvalidState(string message)
        {
            return new ApiErrorException(409, "InvalidModerationState", message);
        }

        private static string ActionLabel(ModerationActionTypeEnum kind)
        {
            return kind == ModerationActionTypeEnum.AutoHide ? "auto_hide" : kind.ToString().ToLowerInvariant();
        }

        private static string? NormalizeNote(string? raw, Dictionary<string, string> errors)
        {
            if (raw == null) return null;
            string note = raw.Trim();
            if (note.Length == 0) return null;
            if (note.Length > MaxNoteLength) errors["note"] = "At most " + MaxNoteLength + " characters.";
            return note;
        }

        private static string ScopeKey(List<string>? scopeTypes)
        {
            return scopeTypes == null ? "*" : String.Join(",", scopeTypes);
        }

        private static string TargetBase(TargetTypeEnum targetType, string targetId)
        {
            return _AdminBase + "/queue/" + targetType + "/" + Uri.EscapeDataString(targetId);
        }

        private TargetTypeEnum ParsePathTarget(string? raw)
        {
            if (TryParseEnum(raw, out TargetTypeEnum type) && (type == TargetTypeEnum.FeedPost || type == TargetTypeEnum.Comment || type == TargetTypeEnum.User || type == TargetTypeEnum.Thread ||
                type == TargetTypeEnum.AssistantMessage || ProviderOf(type) != null || ExtensionFor(type) != null)) return type;
            throw ApiErrorException.Field("targetType", "Use FeedPost, Thread, Comment, User, AssistantMessage" + ProviderTypeList() + ".");
        }

        private TargetTypeEnum? ParseFilterTarget(EnumerationQuery q, Dictionary<string, string> errors, bool allowUser)
        {
            string? raw = q.FilterString("targetType");
            if (raw == null) return null;
            if (TryParseEnum(raw, out TargetTypeEnum type) && (type == TargetTypeEnum.FeedPost || type == TargetTypeEnum.Comment ||
                (allowUser && (type == TargetTypeEnum.User || type == TargetTypeEnum.Thread || type == TargetTypeEnum.AssistantMessage || ProviderOf(type) != null || ExtensionFor(type) != null)))) return type;
            errors["filters.targetType"] = allowUser ? "Use FeedPost, Thread, Comment, User, AssistantMessage" + ProviderTypeList() + "." : "Use FeedPost or Comment.";
            return null;
        }

        private ContentTargetProvider? ProviderOf(TargetTypeEnum targetType)
        {
            if (targetType == TargetTypeEnum.FeedPost || targetType == TargetTypeEnum.Comment || targetType == TargetTypeEnum.Thread || targetType == TargetTypeEnum.User || targetType == TargetTypeEnum.AssistantMessage) return null;
            return _Services.ContentTargets.Find(targetType);
        }

        private string ProviderTypeList()
        {
            List<string> names = _Services.ContentTargets.TargetTypes.Select(t => t.ToString()).OrderBy(t => t, StringComparer.Ordinal).ToList();
            return names.Count == 0 ? String.Empty : ", " + String.Join(", ", names);
        }

        private string? ProviderScope(string? scopeType)
        {
            ContentTargetProvider? provider = _Services.ContentTargets.ForScope(scopeType);
            if (provider == null) return null;
            foreach (TargetTypeEnum type in provider.TargetTypes)
            {
                string scope = provider.ScopeType(type);
                if (String.Equals(scope, scopeType, StringComparison.OrdinalIgnoreCase)) return scope;
            }
            return null;
        }

        private string ProviderScopeList()
        {
            List<string> scopes = _Services.ContentTargets.Providers.SelectMany(p => p.TargetTypes.Select(t => p.ScopeType(t))).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            return scopes.Count == 0 ? String.Empty : ", " + String.Join(", ", scopes);
        }

        private string? ProviderPath(ModerationTarget target)
        {
            ContentTargetProvider? own = ProviderOf(target.TargetType);
            if (own != null) return own.PathOf(target.TargetType, target.TargetId);
            if (target.HostType != null && target.Comment != null)
            {
                ContentTargetProvider? host = ProviderOf(target.HostType.Value);
                if (host != null) return host.PathOf(target.HostType.Value, target.Comment.TargetId) + "?comment=" + target.Comment.Id;
            }
            return null;
        }

        private async Task<List<string>> ProviderModeratorIdsAsync(string scopeType, bool includeCommentModerators, CancellationToken token)
        {
            List<string> candidates = await _Services.Notifications.ModeratorIdsAsync(token).ConfigureAwait(false);
            List<string> result = new List<string>();
            foreach (string userId in candidates)
            {
                token.ThrowIfCancellationRequested();
                ContentActor actor;
                try
                {
                    actor = await _Services.Actors.ForUserAsync(userId, token).ConfigureAwait(false);
                }
                catch (ApiErrorException)
                {
                    continue;
                }
                if (actor.IsSystemAdmin || actor.ModeratedTargetScopes.Contains(scopeType)) result.Add(userId);
                else if (includeCommentModerators && _Services.ContentTargets.ScopesFor(actor).Contains(scopeType + "/Comment")) result.Add(userId);
            }
            return result;
        }

        private static CommentStateEnum CommentState(Comment comment)
        {
            if (comment.DeletedUtc != null) return CommentStateEnum.Deleted;
            if (comment.ModerationState == ModerationStateEnum.Removed) return CommentStateEnum.Removed;
            if (comment.ModerationState == ModerationStateEnum.Hidden) return CommentStateEnum.Hidden;
            return CommentStateEnum.Visible;
        }

        private static string UserState(User user)
        {
            if (user.IsDeleted) return "Deleted";
            return user.IsBanned ? "Banned" : "Active";
        }

        private async Task<ModerationTarget> LoadTargetAsync(TargetTypeEnum targetType, string targetId, CancellationToken token)
        {
            string id = (targetId ?? String.Empty).Trim();
            switch (targetType)
            {
                case TargetTypeEnum.FeedPost:
                    FeedPost? post = id.Length == 0 ? null : await _Services.Database.FeedPosts.ReadAsync(id, token).ConfigureAwait(false);
                    if (post == null) throw ApiErrorException.NotFound("Feed post");
                    return new ModerationTarget { TargetType = targetType, TargetId = post.Id, AuthorUserId = post.AuthorUserId, Post = post };
                case TargetTypeEnum.Comment:
                    Comment? comment = id.Length == 0 ? null : await _Services.Database.Comments.ReadAsync(id, token).ConfigureAwait(false);
                    if (comment == null) throw ApiErrorException.NotFound("Comment");
                    ModerationTarget commentTarget = new ModerationTarget { TargetType = targetType, TargetId = comment.Id, AuthorUserId = comment.AuthorUserId, Comment = comment };
                    ContentTargetProvider? commentHost = ProviderOf(comment.TargetType);
                    if (commentHost != null)
                    {
                        commentTarget.HostType = comment.TargetType;
                        commentTarget.ScopeType = commentHost.ScopeType(comment.TargetType);
                        return commentTarget;
                    }
                    if (Extension != null) commentTarget.ForumId = await Extension.ForumOfCommentAsync(commentTarget, token).ConfigureAwait(false);
                    foreach (IModerationTargetExtension extension in Extensions)
                        if (await extension.ClaimCommentAsync(commentTarget, token).ConfigureAwait(false)) break;
                    return commentTarget;
                case TargetTypeEnum.Thread:
                    ModerationTarget? thread = Extension == null ? null : await Extension.LoadAsync(targetType, id, token).ConfigureAwait(false);
                    if (thread == null) throw ApiErrorException.NotFound("Thread");
                    return thread;
                case TargetTypeEnum.AssistantMessage:
                    return await _Services.Ai.Reports.LoadTargetAsync(id, token).ConfigureAwait(false);
                case TargetTypeEnum.User:
                    User? member = id.Length == 0 ? null : await _Services.Database.Users.ReadAsync(id, token).ConfigureAwait(false);
                    if (member == null) throw ApiErrorException.NotFound("Member");
                    return new ModerationTarget { TargetType = TargetTypeEnum.User, TargetId = member.Id, AuthorUserId = member.Id, User = member };
                default:
                    ContentTargetProvider? provider = ProviderOf(targetType);
                    if (provider != null)
                    {
                        ModerationTarget? own = id.Length == 0 ? null : await provider.LoadModerationTargetAsync(targetType, id, token).ConfigureAwait(false);
                        if (own == null) throw ApiErrorException.NotFound(targetType.ToString());
                        own.ScopeType = provider.ScopeType(targetType);
                        return own;
                    }
                    IModerationTargetExtension? extra = targetType == TargetTypeEnum.User ? null : ExtensionFor(targetType);
                    if (extra != null) return await extra.LoadAsync(targetType, id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound(targetType.ToString());
                    User? user = id.Length == 0 ? null : await _Services.Database.Users.ReadAsync(id, token).ConfigureAwait(false);
                    if (user == null) throw ApiErrorException.NotFound("Member");
                    return new ModerationTarget { TargetType = TargetTypeEnum.User, TargetId = user.Id, AuthorUserId = user.Id, User = user };
            }
        }

        private async Task<object> ModeratorTargetViewAsync(ContentActor actor, ModerationTarget target, CancellationToken token)
        {
            if (target.TargetType == TargetTypeEnum.FeedPost) return await _Services.Feed.ModeratorViewAsync(actor, target.TargetId, token).ConfigureAwait(false);
            if (target.TargetType == TargetTypeEnum.Comment) return await _Services.Comments.ModeratorViewAsync(actor, target.TargetId, token).ConfigureAwait(false);
            if (target.TargetType == TargetTypeEnum.Thread && Extension != null) return await Extension.ViewAsync(actor, target, token).ConfigureAwait(false);
            if (target.TargetType == TargetTypeEnum.AssistantMessage) return await _Services.Ai.Reports.ViewAsync(target.TargetId, token).ConfigureAwait(false);
            ContentTargetProvider? provider = ProviderOf(target.TargetType);
            if (provider != null) return await provider.ModerationViewAsync(actor, target, token).ConfigureAwait(false);
            IModerationTargetExtension? extra = target.TargetType == TargetTypeEnum.User ? null : ExtensionFor(target.TargetType);
            if (extra != null) return await extra.ViewAsync(actor, target, token).ConfigureAwait(false);
            return await _Services.Summaries.BuildOneAsync(target.TargetId, token).ConfigureAwait(false);
        }

        private async Task<ModerationQueueDetail> BuildDetailAsync(ContentActor actor, ModerationTarget target, CancellationToken token)
        {
            DateTime now = _Clock();
            string targetBase = TargetBase(target.TargetType, target.TargetId);
            ModerationQueueDetail detail = new ModerationQueueDetail
            {
                TargetType = target.TargetType,
                TargetId = target.TargetId,
                Target = await ModeratorTargetViewAsync(actor, target, token).ConfigureAwait(false),
                Context = new ModerationContext
                {
                    FeedPostId = target.FeedPostId,
                    ParentCommentId = target.Comment?.ParentCommentId,
                    ThreadId = target.Thread?.Id ?? (target.Comment != null && target.Comment.TargetType == TargetTypeEnum.Thread ? target.Comment.TargetId : null),
                    ForumId = target.ForumId
                },
                OpenReports = await _Services.Database.Reports.CountOpenOnTargetAsync(target.TargetType, target.TargetId, ScopesFor(actor), token).ConfigureAwait(false)
            };
            EnumerationQuery preview = new EnumerationQuery { MaxResults = BoundedPreview<object>.MaxObjects };
            detail.Reports = BoundedPreview<ReportView>.From(await ReportsAsync(actor, target.TargetType.ToString(), target.TargetId, preview, token).ConfigureAwait(false), targetBase + "/reports/enumerate");
            detail.Actions = BoundedPreview<ModerationActionView>.From(await ActionsCoreAsync(actor, new EnumerationQuery { MaxResults = BoundedPreview<object>.MaxObjects }, target.TargetType, target.TargetId, token).ConfigureAwait(false),
                targetBase + "/actions/enumerate");
            detail.Revisions = await RevisionsPreviewAsync(target, token).ConfigureAwait(false);

            DateTime since = now.AddDays(-AuthorHistoryDays);
            if (String.IsNullOrEmpty(target.AuthorUserId))
            {
                // Assistant replies have no member author: no history to show.
                detail.AuthorHistory = new ModerationAuthorHistory();
                ObjectMetadata targetMetadata = await _Services.Metadata.LoadOneAsync(target.TargetType, target.TargetId, actor.IsStaffFor(target.TargetType), token).ConfigureAwait(false);
                detail.Labels = targetMetadata.Labels;
                detail.Tags = targetMetadata.Tags;
                return detail;
            }
            User? author = target.User ?? await _Services.Database.Users.ReadAsync(target.AuthorUserId, token).ConfigureAwait(false);
            detail.AuthorHistory = new ModerationAuthorHistory
            {
                RemovedCount90d = await _Services.Database.ModerationActions.CountByAuthorAsync(target.AuthorUserId, new[] { ModerationActionTypeEnum.Remove }, since, token).ConfigureAwait(false),
                HiddenCount90d = await _Services.Database.ModerationActions.CountByAuthorAsync(target.AuthorUserId, new[] { ModerationActionTypeEnum.Hide, ModerationActionTypeEnum.AutoHide }, since, token).ConfigureAwait(false),
                ReportsAgainst90d = await _Services.Database.Reports.CountAgainstAuthorAsync(target.AuthorUserId, since, token).ConfigureAwait(false),
                IsMuted = author != null && author.IsMutedAt(now),
                MutedUntilUtc = author != null && author.IsMutedAt(now) ? author.MutedUntilUtc : null
            };
            ObjectMetadata metadata = await _Services.Metadata.LoadOneAsync(target.TargetType, target.TargetId, actor.IsStaffFor(target.TargetType), token).ConfigureAwait(false);
            detail.Labels = metadata.Labels;
            detail.Tags = metadata.Tags;
            return detail;
        }

        private async Task<BoundedPreview<object>> RevisionsPreviewAsync(ModerationTarget target, CancellationToken token)
        {
            EnumerationQuery q = new EnumerationQuery { MaxResults = BoundedPreview<object>.MaxObjects };
            if (target.TargetType == TargetTypeEnum.FeedPost)
            {
                EnumerationRequest request = _Services.Enumeration.Prepare(q, ContentCollections.FeedPostRevisions, true, "revisions|" + target.TargetId, BoundedPreview<object>.MaxObjects);
                EnumerationResult<FeedPostRevision> page = await _Services.Database.FeedPosts.EnumerateRevisionsAsync(target.TargetId, request, token).ConfigureAwait(false);
                Dictionary<string, UserSummary> editors = await _Services.Summaries.BuildAsync(page.Objects.Select(r => r.EditedByUserId), token).ConfigureAwait(false);
                List<FeedPostRevisionView> views = page.Objects.Select(r =>
                {
                    UserSummary editor = r.EditedByUserId != null && editors.TryGetValue(r.EditedByUserId, out UserSummary? u) ? u : UserSummary.Deleted();
                    return new FeedPostRevisionView
                    {
                        Id = r.Id,
                        Revision = r.Revision,
                        Title = r.Title,
                        Body = r.Body,
                        AttachmentSummary = r.AttachmentSummary,
                        EditedBy = editor,
                        EditedByUserId = r.EditedByUserId,
                        EditedByName = r.EditedByUserId == null ? "admin token" : editor.DisplayName,
                        CreatedUtc = r.CreatedUtc
                    };
                }).ToList();
                await _Services.Metadata.AttachAsync(TargetTypeEnum.FeedPostRevision, views, v => v.Id, true, token).ConfigureAwait(false);
                return new BoundedPreview<object>
                {
                    Objects = views.Select(v => (object)v).ToList(),
                    TotalRecords = page.TotalRecords ?? page.Objects.Count,
                    Enumerate = Constants.ApiPrefix + "/feed/" + target.TargetId + "/revisions/enumerate"
                };
            }
            if (target.TargetType == TargetTypeEnum.Comment)
            {
                EnumerationRequest request = _Services.Enumeration.Prepare(q, ContentCollections.CommentRevisions, true, "revisions|" + target.TargetId, BoundedPreview<object>.MaxObjects);
                EnumerationResult<CommentRevision> page = await _Services.Database.Comments.EnumerateRevisionsAsync(target.TargetId, request, token).ConfigureAwait(false);
                List<CommentRevisionView> views = page.Objects.Select(r => new CommentRevisionView { Id = r.Id, Revision = r.Revision, Body = r.Body, AttachmentSummary = r.AttachmentSummary, CreatedUtc = r.CreatedUtc }).ToList();
                await _Services.Metadata.AttachAsync(TargetTypeEnum.CommentRevision, views, v => v.Id, true, token).ConfigureAwait(false);
                return new BoundedPreview<object>
                {
                    Objects = views.Select(v => (object)v).ToList(),
                    TotalRecords = page.TotalRecords ?? page.Objects.Count,
                    Enumerate = Constants.ApiPrefix + "/comments/" + target.TargetId + "/revisions/enumerate"
                };
            }
            if (target.TargetType == TargetTypeEnum.Thread && Extension != null) return await Extension.RevisionsAsync(target, token).ConfigureAwait(false);
            IModerationTargetExtension? extra = target.TargetType == TargetTypeEnum.User ? null : ExtensionFor(target.TargetType);
            if (extra != null) return await extra.RevisionsAsync(target, token).ConfigureAwait(false);
            return new BoundedPreview<object>();
        }

        private async Task<EnumerationResult<ModerationActionView>> ActionsCoreAsync(ContentActor actor, EnumerationQuery? query, TargetTypeEnum? targetType, string? targetId, CancellationToken token)
        {
            EnumerationQuery q = query ?? new EnumerationQuery();
            Dictionary<string, string> errors = new Dictionary<string, string>();
            ModerationActionFilter filter = new ModerationActionFilter
            {
                TargetAuthorUserId = q.FilterString("targetAuthorUserId"),
                Action = q.FilterEnum<ModerationActionTypeEnum>("action", errors)
            };
            string? moderator = q.FilterString("moderatorUserId");
            if (String.Equals(moderator, "system", StringComparison.OrdinalIgnoreCase)) filter.SystemOnly = true;
            else filter.ModeratorUserId = moderator;
            if (targetType != null)
            {
                filter.TargetType = targetType;
                filter.TargetId = targetId;
            }
            else
            {
                filter.TargetType = ParseFilterTarget(q, errors, true);
                filter.TargetId = q.FilterString("targetId");
            }
            DateTime? from = q.FilterUtc("fromUtc", errors);
            DateTime? to = q.FilterUtc("toUtc", errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            if (from != null && q.CreatedAfterUtc == null) q.CreatedAfterUtc = from;
            if (to != null && q.CreatedBeforeUtc == null) q.CreatedBeforeUtc = to;
            bool staff = actor.IsStaffFor(TargetTypeEnum.ModerationAction);
            string scope = targetType == null ? "actions" : "actions|" + targetType + "|" + targetId;
            EnumerationRequest request = _Services.Enumeration.Prepare(q, ModerationCollections.Actions, staff, scope);
            EnumerationResult<ModerationAction> page = await _Services.Database.ModerationActions.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            return page.With(await ActionViewsAsync(page.Objects, staff, token).ConfigureAwait(false));
        }

        private async Task<List<ModerationActionView>> ActionViewsAsync(List<ModerationAction> actions, bool includeStaff, CancellationToken token)
        {
            Dictionary<string, UserSummary> users = await _Services.Summaries.BuildAsync(actions.Select(a => a.ModeratorUserId).Concat(actions.Select(a => a.TargetAuthorUserId)), token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.ModerationAction, actions.Select(a => a.Id), includeStaff, token).ConfigureAwait(false);
            return actions.Select(a => new ModerationActionView
            {
                Id = a.Id,
                Moderator = a.ModeratorUserId != null && users.TryGetValue(a.ModeratorUserId, out UserSummary? m) ? m : null,
                ModeratorName = a.ModeratorUserId != null
                    ? (users.TryGetValue(a.ModeratorUserId, out UserSummary? named) ? named.DisplayName : UserSummary.Deleted().DisplayName)
                    : (a.BreakGlass ? BreakGlassName : "system"),
                TargetType = a.TargetType,
                TargetId = a.TargetId,
                TargetAuthor = a.TargetAuthorUserId != null && users.TryGetValue(a.TargetAuthorUserId, out UserSummary? t) ? t : UserSummary.Deleted(),
                Action = a.Action,
                Reason = a.Reason,
                Note = a.Note,
                NotifiedAuthor = a.NotifiedAuthor,
                CreatedUtc = a.CreatedUtc,
                Labels = metadata.TryGetValue(a.Id, out ObjectMetadata? md) ? md.Labels : new List<string>(),
                Tags = md != null ? md.Tags : new Dictionary<string, string>()
            }).ToList();
        }

        private async Task<List<ReportView>> ReportViewsAsync(List<Report> reports, bool includeStaff, CancellationToken token)
        {
            Dictionary<string, UserSummary> users = await _Services.Summaries.BuildAsync(reports.Select(r => r.ReporterUserId).Concat(reports.Select(r => r.ResolvedByUserId)), token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.Report, reports.Select(r => r.Id), includeStaff, token).ConfigureAwait(false);
            return reports.Select(r =>
            {
                string? resolvedBy = null;
                if (r.Status != ReportStatusEnum.Open)
                    resolvedBy = r.ResolvedByUserId == null ? BreakGlassName : (users.TryGetValue(r.ResolvedByUserId, out UserSummary? resolver) ? resolver.DisplayName : UserSummary.Deleted().DisplayName);
                return new ReportView
                {
                    Id = r.Id,
                    Reporter = r.ReporterUserId == null ? null : (users.TryGetValue(r.ReporterUserId, out UserSummary? reporter) ? reporter : UserSummary.Deleted()),
                    TargetType = r.TargetType,
                    TargetId = r.TargetId,
                    Reason = r.Reason,
                    Details = r.Details,
                    Status = r.Status,
                    CreatedUtc = r.CreatedUtc,
                    DueUtc = r.DueUtc,
                    ResolvedUtc = r.ResolvedUtc,
                    ResolvedByName = resolvedBy,
                    ResolutionNote = r.ResolutionNote,
                    ModerationActionId = r.ModerationActionId,
                    Labels = metadata.TryGetValue(r.Id, out ObjectMetadata? m) ? m.Labels : new List<string>(),
                    Tags = m != null ? m.Tags : new Dictionary<string, string>()
                };
            }).ToList();
        }

        private async Task<List<ModerationQueueItem>> QueueItemsAsync(ContentActor actor, List<ModerationQueueRow> rows, List<string>? scopeTypes, DateTime now, CancellationToken token)
        {
            List<string> postIds = rows.Where(r => r.TargetType == TargetTypeEnum.FeedPost).Select(r => r.TargetId).ToList();
            List<string> commentIds = rows.Where(r => r.TargetType == TargetTypeEnum.Comment).Select(r => r.TargetId).ToList();
            List<string> userIds = rows.Where(r => r.TargetType == TargetTypeEnum.User).Select(r => r.TargetId).ToList();
            List<string> threadIds = rows.Where(r => r.TargetType == TargetTypeEnum.Thread).Select(r => r.TargetId).ToList();
            Dictionary<string, ForumThread> threads = threadIds.Count == 0 ? new Dictionary<string, ForumThread>(StringComparer.Ordinal)
                : (await _Services.Database.Threads.ReadManyAsync(threadIds, token).ConfigureAwait(false)).ToDictionary(t => t.Id, StringComparer.Ordinal);
            Dictionary<string, ObjectMetadata> threadMeta = await _Services.Metadata.LoadAsync(TargetTypeEnum.Thread, threadIds, actor.IsStaffFor(TargetTypeEnum.Thread), token).ConfigureAwait(false);
            List<string> assistantMessageIds = rows.Where(r => r.TargetType == TargetTypeEnum.AssistantMessage).Select(r => r.TargetId).ToList();
            Dictionary<string, AiModerationSnapshot> snapshots = assistantMessageIds.Count == 0 ? new Dictionary<string, AiModerationSnapshot>(StringComparer.Ordinal)
                : await _Services.Database.Ai.Moderation.ReadManyByMessageAsync(assistantMessageIds, token).ConfigureAwait(false);
            Dictionary<string, string> assistantNames = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string assistantId in rows.Where(r => r.ScopeType == AssistantScope && r.ScopeId != null).Select(r => r.ScopeId!).Distinct(StringComparer.Ordinal))
            {
                Assistant? assistant = await _Services.Database.Ai.Assistants.ReadAsync(assistantId, token).ConfigureAwait(false);
                if (assistant != null) assistantNames[assistantId] = assistant.Name;
            }
            Dictionary<string, string?> forumNames = new Dictionary<string, string?>(StringComparer.Ordinal);
            if (Extension != null)
                foreach (string forumId in rows.Where(r => r.ScopeType == ForumScope && r.ScopeId != null).Select(r => r.ScopeId!).Distinct(StringComparer.Ordinal))
                    forumNames[forumId] = await Extension.ForumNameAsync(forumId, token).ConfigureAwait(false);
            Dictionary<string, FeedPost> posts = (await _Services.Database.FeedPosts.ReadManyAsync(postIds, token).ConfigureAwait(false)).ToDictionary(p => p.Id, StringComparer.Ordinal);
            Dictionary<string, Comment> comments = (await _Services.Database.Comments.ReadManyAsync(commentIds, token).ConfigureAwait(false)).ToDictionary(c => c.Id, StringComparer.Ordinal);
            Dictionary<string, User> users = userIds.Count == 0 ? new Dictionary<string, User>(StringComparer.Ordinal)
                : (await _Services.Database.Users.ReadManyAsync(userIds, token).ConfigureAwait(false)).ToDictionary(u => u.Id, StringComparer.Ordinal);
            Dictionary<string, UserSummary> authors = await _Services.Summaries.BuildAsync(rows.Select(r => r.TargetAuthorUserId), token).ConfigureAwait(false);
            Dictionary<string, Dictionary<string, long>> reasons = await _Services.Database.Reports.ReasonCountsAsync(rows, scopeTypes, token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> postMeta = await _Services.Metadata.LoadAsync(TargetTypeEnum.FeedPost, postIds, actor.IsStaffFor(TargetTypeEnum.FeedPost), token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> commentMeta = await _Services.Metadata.LoadAsync(TargetTypeEnum.Comment, commentIds, actor.IsStaffFor(TargetTypeEnum.Comment), token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> userMeta = await _Services.Metadata.LoadAsync(TargetTypeEnum.User, userIds, actor.IsStaffFor(TargetTypeEnum.User), token).ConfigureAwait(false);
            Dictionary<string, ModerationTarget> providerTargets = new Dictionary<string, ModerationTarget>(StringComparer.Ordinal);
            Dictionary<string, ObjectMetadata> providerMeta = new Dictionary<string, ObjectMetadata>(StringComparer.Ordinal);
            foreach (IGrouping<TargetTypeEnum, ModerationQueueRow> group in rows.Where(r => ProviderOf(r.TargetType) != null).GroupBy(r => r.TargetType))
            {
                ContentTargetProvider provider = ProviderOf(group.Key)!;
                foreach (ModerationQueueRow row in group)
                {
                    ModerationTarget? loaded = await provider.LoadModerationTargetAsync(row.TargetType, row.TargetId, token).ConfigureAwait(false);
                    if (loaded != null) providerTargets[row.TargetType + ":" + row.TargetId] = loaded;
                }
                Dictionary<string, ObjectMetadata> meta = await _Services.Metadata.LoadAsync(group.Key, group.Select(r => r.TargetId).Distinct(StringComparer.Ordinal), actor.IsStaffFor(group.Key), token).ConfigureAwait(false);
                foreach (KeyValuePair<string, ObjectMetadata> pair in meta) providerMeta[group.Key + ":" + pair.Key] = pair.Value;
            }
            List<ModerationQueueItem> items = new List<ModerationQueueItem>();
            foreach (ModerationQueueRow row in rows)
            {
                ModerationQueueItem item = new ModerationQueueItem
                {
                    TargetType = row.TargetType,
                    TargetId = row.TargetId,
                    TargetState = "Missing",
                    Author = row.TargetAuthorUserId != null && authors.TryGetValue(row.TargetAuthorUserId, out UserSummary? a) ? a : UserSummary.Deleted(),
                    ReportCount = row.ReportCount,
                    ReporterCount = row.ReporterCount,
                    Reasons = reasons.TryGetValue(row.TargetType + ":" + row.TargetId, out Dictionary<string, long>? r) ? r : new Dictionary<string, long>(),
                    FirstReportedUtc = row.FirstReportedUtc,
                    LastReportedUtc = row.LastReportedUtc,
                    DueUtc = row.DueUtc,
                    IsOverdue = row.HasOpen && row.DueUtc < now,
                    Status = row.HasOpen ? "Open" : "Resolved",
                    Scope = new ModerationScopeView
                    {
                        Type = row.ScopeType,
                        Id = row.ScopeId ?? "*",
                        Name = row.ScopeType == ForumScope && row.ScopeId != null && forumNames.TryGetValue(row.ScopeId, out string? forumName) ? forumName
                            : row.ScopeType == AssistantScope && row.ScopeId != null && assistantNames.TryGetValue(row.ScopeId, out string? assistantName) ? assistantName : null
                    }
                };
                ObjectMetadata? metadata = null;
                if (row.TargetType == TargetTypeEnum.FeedPost && posts.TryGetValue(row.TargetId, out FeedPost? post))
                {
                    item.TargetState = post.StateAt(now).ToString();
                    item.Preview = new ModerationPreview { Title = post.Title, Excerpt = post.Excerpt, HasMedia = post.AttachmentCount > 0 };
                    postMeta.TryGetValue(post.Id, out metadata);
                }
                else if (row.TargetType == TargetTypeEnum.Comment && comments.TryGetValue(row.TargetId, out Comment? comment))
                {
                    item.TargetState = CommentState(comment).ToString();
                    item.Preview = new ModerationPreview { Excerpt = comment.Excerpt, HasMedia = comment.AttachmentCount > 0 };
                    commentMeta.TryGetValue(comment.Id, out metadata);
                }
                else if (row.TargetType == TargetTypeEnum.Thread && threads.TryGetValue(row.TargetId, out ForumThread? thread))
                {
                    item.TargetState = ForumAccessService.StateOf(thread).ToString();
                    item.Preview = new ModerationPreview { Title = thread.Title, Excerpt = thread.Excerpt, HasMedia = thread.AttachmentCount > 0 };
                    threadMeta.TryGetValue(thread.Id, out metadata);
                }
                else if (row.TargetType == TargetTypeEnum.AssistantMessage && snapshots.TryGetValue(row.TargetId, out AiModerationSnapshot? snapshot))
                {
                    AiModerationSnapshotMessage? reported = (await _Services.Database.Ai.Moderation.LastMessagesAsync(snapshot.Id, 1, token).ConfigureAwait(false)).FirstOrDefault();
                    item.TargetState = reported?.Status.ToString() ?? "Retained";
                    item.Preview = new ModerationPreview { Title = assistantNames.TryGetValue(snapshot.AssistantId, out string? title) ? title : null, Excerpt = reported == null ? null : MarkdownNormalizer.Excerpt(reported.Content) };
                }
                else if (providerTargets.TryGetValue(row.TargetType + ":" + row.TargetId, out ModerationTarget? providerTarget))
                {
                    item.TargetState = providerTarget.IsDeleted ? "Deleted" : providerTarget.ModerationState == ModerationStateEnum.None ? "Visible" : providerTarget.ModerationState.ToString();
                    item.Preview = new ModerationPreview { Title = providerTarget.Title, Excerpt = providerTarget.Excerpt, HasMedia = providerTarget.HasMedia };
                    providerMeta.TryGetValue(row.TargetType + ":" + row.TargetId, out metadata);
                }
                else if (row.TargetType == TargetTypeEnum.User && users.TryGetValue(row.TargetId, out User? user))
                {
                    item.TargetState = UserState(user);
                    item.Preview = new ModerationPreview { Title = user.IsDeleted ? UserSummary.Deleted().DisplayName : user.DisplayName, Excerpt = user.IsDeleted ? null : MarkdownNormalizer.Excerpt(user.Bio ?? String.Empty) };
                    userMeta.TryGetValue(user.Id, out metadata);
                }
                else if (row.TargetType != TargetTypeEnum.User && ExtensionFor(row.TargetType) != null)
                {
                    IModerationTargetExtension described = ExtensionFor(row.TargetType)!;
                    ModerationTarget? loaded = await described.LoadAsync(row.TargetType, row.TargetId, token).ConfigureAwait(false);
                    if (loaded != null) await described.DescribeAsync(loaded, item, token).ConfigureAwait(false);
                    metadata = await _Services.Metadata.LoadOneAsync(row.TargetType, row.TargetId, actor.IsStaffFor(row.TargetType), token).ConfigureAwait(false);
                }
                item.Labels = metadata?.Labels ?? new List<string>();
                item.Tags = metadata?.Tags ?? new Dictionary<string, string>();
                items.Add(item);
            }
            return items;
        }

        #endregion
    }
}
