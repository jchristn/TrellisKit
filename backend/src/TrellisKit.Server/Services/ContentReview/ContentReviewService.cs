namespace TrellisKit.Server.Services.ContentReview
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
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
    using TrellisKit.Server.Services.Ai;
    using TrellisKit.Server.Services.Email.Templates;
    using TrellisKit.Server.Services.Jobs;

    /// <summary>
    /// The content changelog and its Terms of Service review (docs/INTEGRATION.md content review contract). Rows are written
    /// by database triggers in the same transaction as every member contribution; this service reviews Pending rows
    /// through the Moderation task role (the content.review job), warns administrators about flagged rows (one in-app
    /// and push notification per row, and the content.reviewDigest email), serves the admin queue, and routes the
    /// queue's actions through the existing moderation and member actions. Thread safety: safe for concurrent use.
    /// </summary>
    public class ContentReviewService
    {
        #region Public-Members

        /// <summary>
        /// Key of the review job.
        /// </summary>
        public const string ReviewJobKey = "content.review";

        /// <summary>
        /// Key of the daily digest job.
        /// </summary>
        public const string DigestJobKey = "content.reviewDigest";

        /// <summary>
        /// Key of the retention job.
        /// </summary>
        public const string PruneJobKey = "content.changesPrune";

        /// <summary>
        /// Task role every review goes through.
        /// </summary>
        public const string Role = "Moderation";

        /// <summary>
        /// Shortest action reason (characters).
        /// </summary>
        public const int MinReasonLength = 3;

        /// <summary>
        /// Longest action reason (characters).
        /// </summary>
        public const int MaxReasonLength = 500;

        /// <summary>
        /// Clock (tests replace it). Default UTC now.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public Func<DateTime> Clock
        {
            get { return _Clock; }
            set { _Clock = value ?? throw new ArgumentNullException(nameof(Clock)); }
        }

        #endregion

        #region Private-Members

        private const string _AiOff = "AiOff";
        private const string _NoExecutors = "NoExecutors";
        private const string _JobDisabled = "JobDisabled";
        private const string _JobsOff = "JobsOff";
        private const int _ExcerptLength = 160;
        private readonly ApplicationServices _Services;
        private Func<DateTime> _Clock = () => DateTime.UtcNow;
        private int _Attached = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">services is null.</exception>
        public ContentReviewService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register the review, digest, and retention jobs and wire account deletion (idempotent).
        /// </summary>
        public void Attach()
        {
            if (Interlocked.Exchange(ref _Attached, 1) != 0) return;
            _Services.Jobs.Registry.Register(new ContentReviewJobHandler(this));
            _Services.Jobs.Registry.Register(new ContentReviewDigestJobHandler(this));
            _Services.Jobs.Registry.Register(new ContentChangePruneJobHandler());
            Func<string, CancellationToken, Task>? previous = _Services.Accounts.AccountClosed;
            _Services.Accounts.AccountClosed = async (userId, token) =>
            {
                if (previous != null) await previous(userId, token).ConfigureAwait(false);
                await _Services.Database.ContentChanges.EraseForUserAsync(userId, Clock(), token).ConfigureAwait(false);
            };
        }

        /// <summary>
        /// Why the reviewer cannot run now, or null when it can: AiOff (AI is off for the property), NoExecutors (the
        /// Moderation role has no enabled executor), and, unless <paramref name="forPlanning"/>, JobsOff (the job engine
        /// is off on this instance) and JobDisabled (the content.review job is disabled).
        /// </summary>
        /// <param name="forPlanning">True from the job itself (only the AI checks apply).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Problem code, or null.</returns>
        public async Task<string?> ProblemAsync(bool forPlanning, CancellationToken token = default)
        {
            if (!_Services.Ai.IsAvailable) return _AiOff;
            if (!await _Services.Ai.Roles.HasExecutorsAsync(Role, token).ConfigureAwait(false)) return _NoExecutors;
            if (forPlanning) return null;
            if (!_Services.Settings.Jobs.Enabled) return _JobsOff;
            await _Services.Jobs.EnsureDefinitionsAsync(token).ConfigureAwait(false);
            JobDefinition? definition = await _Services.Database.Jobs.ReadDefinitionAsync(ReviewJobKey, token).ConfigureAwait(false);
            if (definition != null && !definition.Enabled) return _JobDisabled;
            return null;
        }

        /// <summary>
        /// The words of a problem code.
        /// </summary>
        /// <param name="problem">Problem code, or null.</param>
        /// <returns>Message, or null.</returns>
        public static string? ProblemMessage(string? problem)
        {
            switch (problem)
            {
                case null:
                    return null;
                case _AiOff:
                    return "AI is off for this property, so contributions wait in Pending. Turn AI on in AI settings to review them.";
                case _NoExecutors:
                    return "The Moderation task role has no executors, so contributions wait in Pending. Add an executor in AI task roles.";
                case _JobsOff:
                    return "The job engine is off on this instance (jobs.enabled), so the content.review job does not run here.";
                case _JobDisabled:
                    return "The content.review job is disabled in Jobs, so contributions wait in Pending.";
                default:
                    return problem;
            }
        }

        /// <summary>
        /// The content.review plan: one item per Pending row (oldest first, at most contentReview.batchSize), or none
        /// while the reviewer cannot run.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Items.</returns>
        public async Task<List<JobItemPlan>> PlanReviewAsync(CancellationToken token = default)
        {
            if (await ProblemAsync(true, token).ConfigureAwait(false) != null) return new List<JobItemPlan>();
            List<ContentChangeRecord> pending = await _Services.Database.ContentChanges.ListPendingAsync(_Services.Settings.ContentReview.BatchSize, token).ConfigureAwait(false);
            return pending.Select(p => new JobItemPlan(p.Id)).ToList();
        }

        /// <summary>
        /// Review one Pending row through the Moderation role and record the verdict; flagged rows notify
        /// administrators. Rows without text are marked Clean (their media references stay recorded for image review).
        /// Model errors and unreadable verdicts count toward contentReview.maxAttempts, after which the row is flagged
        /// for an administrator instead.
        /// </summary>
        /// <param name="id">Row identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Outcome for the job item.</returns>
        public async Task<JobItemOutcome> ReviewAsync(string id, CancellationToken token = default)
        {
            using (Activity? activity = TrellisKitTelemetry.StartSpan("content.review"))
            {
                ContentChangeRecord? row = await _Services.Database.ContentChanges.ReadAsync(id, token).ConfigureAwait(false);
                if (row == null) return JobItemOutcome.Ok("{\"skipped\":\"missing\"}");
                if (row.ReviewStatus != ContentReviewStatusEnum.Pending) return JobItemOutcome.Ok("{\"skipped\":\"" + row.ReviewStatus + "\"}");
                DateTime now = Clock();
                if (String.IsNullOrWhiteSpace(row.Snapshot))
                {
                    string note = row.MediaRefs.Count > 0 ? "No text to review; the media references are recorded for image review." : "No text or media to review.";
                    await _Services.Database.ContentChanges.CompleteReviewAsync(row.Id, ContentReviewStatusEnum.Clean, new List<ContentReviewCategoryEnum>(), ContentReviewSeverityEnum.None, note,
                        "system", ContentReviewerKindEnum.System, now, token).ConfigureAwait(false);
                    return JobItemOutcome.Ok("{\"status\":\"Clean\",\"reviewer\":\"system\"}");
                }

                string system = await _Services.Ai.Prompts.GetAsync(PromptCatalog.ContentReviewClassifier, token).ConfigureAwait(false);
                RoleOutcome outcome = await _Services.Ai.Roles.PromptAsync(Role, Prompt(row), system, _Services.Settings.ContentReview.MaxOutputTokens, token).ConfigureAwait(false);
                if (!outcome.Success)
                {
                    string code = outcome.ErrorCode ?? "ModelError";
                    // Configuration gaps are not the row's fault: it waits for the next run without counting an attempt.
                    if (code == "NotAvailable" || code == "NoExecutors" || code == "UnknownRole") return JobItemOutcome.Fail(code, false);
                    await FailAsync(row, code + (outcome.Escalations.Count > 0 ? ": " + String.Join(" | ", outcome.Escalations) : String.Empty), token).ConfigureAwait(false);
                    return JobItemOutcome.Fail(code, outcome.Retryable || code == "ExecutorsExhausted");
                }
                if (!ContentReviewVerdict.TryParse(outcome.Text, out ContentReviewVerdict? verdict) || verdict == null)
                {
                    string text = outcome.Text ?? String.Empty;
                    await FailAsync(row, "UnreadableVerdict: " + (text.Length > 200 ? text.Substring(0, 200) : text), token).ConfigureAwait(false);
                    return JobItemOutcome.Fail("UnreadableVerdict", true);
                }

                bool saved = await _Services.Database.ContentChanges.CompleteReviewAsync(row.Id, verdict.Status, verdict.Categories, verdict.Severity, verdict.Note,
                    "model:" + (outcome.Model ?? "unknown"), ContentReviewerKindEnum.Model, Clock(), token).ConfigureAwait(false);
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.ModerationActions, "action", "content_review_" + verdict.Status.ToString().ToLowerInvariant(), "target_type", row.TargetType);
                if (saved && verdict.Status == ContentReviewStatusEnum.Flagged)
                {
                    ContentChangeRecord flagged = (await _Services.Database.ContentChanges.ReadAsync(row.Id, token).ConfigureAwait(false)) ?? row;
                    await NotifyFlaggedAsync(flagged, token).ConfigureAwait(false);
                }
                activity?.SetTag("content_review.status", verdict.Status.ToString());
                activity?.SetStatus(ActivityStatusCode.Ok);
                return JobItemOutcome.Ok(JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    { "status", verdict.Status.ToString() },
                    { "categories", verdict.Categories.Select(c => c.ToString()).ToList() },
                    { "severity", verdict.Severity.ToString() }
                }));
            }
        }

        /// <summary>
        /// Notify administrators about a flagged row: one in-app and push notification (ContentFlagged, category
        /// Moderation) to each SystemAdmin, at most contentReview.maxNotifiedAdmins, once per row.
        /// </summary>
        /// <param name="row">Flagged row.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Notifications created.</returns>
        /// <exception cref="ArgumentNullException">row is null.</exception>
        public async Task<int> NotifyFlaggedAsync(ContentChangeRecord row, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(row);
            if (!await _Services.Database.ContentChanges.MarkNotifiedAsync(row.Id, Clock(), token).ConfigureAwait(false)) return 0;
            List<RoleAssignment> admins = await _Services.Database.RoleAssignments.EnumerateByRoleAsync(RoleEnum.SystemAdmin, _Services.Settings.ContentReview.MaxNotifiedAdmins, token).ConfigureAwait(false);
            List<string> recipients = admins.Select(a => a.UserId).Distinct(StringComparer.Ordinal).ToList();
            if (recipients.Count == 0) return 0;
            NotificationDraft draft = new NotificationDraft
            {
                Type = NotificationTypeEnum.ContentFlagged,
                TargetType = TargetTypeEnum.ContentChange,
                TargetId = row.Id,
                SubjectNoun = Noun(row.TargetType),
                TitleText = Title(row),
                Body = Excerpt(row.Snapshot),
                Path = "/admin/content-review?id=" + Uri.EscapeDataString(row.Id),
                EventUtc = Clock(),
                QueuePush = true
            };
            return await _Services.Notifications.CreateForRecipientsAsync(draft, recipients, token).ConfigureAwait(false);
        }

        /// <summary>
        /// The daily digest: when anything was flagged in the last 24 hours, one email (staff_content_review_digest) to
        /// the SystemAdmins (or the template's configured staff recipients), once per UTC day.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows flagged in the window (0 sends nothing).</returns>
        public async Task<long> SendDigestAsync(CancellationToken token = default)
        {
            DateTime now = Clock();
            ContentReviewCounts counts = await _Services.Database.ContentChanges.CountsAsync(now.AddHours(-24), token).ConfigureAwait(false);
            if (counts.FlaggedSince == 0) return 0;
            ContentChangeFilter filter = new ContentChangeFilter { Statuses = new List<ContentReviewStatusEnum> { ContentReviewStatusEnum.Flagged } };
            EnumerationRequest request = _Services.Enumeration.Prepare(new EnumerationQuery { MaxResults = 1, Ordering = "SeverityDescending", CreatedAfterUtc = now.AddDays(-30) }, ContentReviewCollections.Queue, true);
            EnumerationResult<ContentChangeRecord> top = await _Services.Database.ContentChanges.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            string highest = top.Objects.Count == 0 ? "none" : top.Objects[0].Severity.ToString();
            await _Services.Email.NotifyStaffAsync(StaffContentReviewDigestEmail.TemplateKey, "contentreview:" + now.ToString("yyyyMMdd", CultureInfo.InvariantCulture), token,
                ("count", counts.FlaggedSince.ToString(CultureInfo.InvariantCulture)),
                ("highest", highest),
                ("open", counts.Flagged.ToString(CultureInfo.InvariantCulture)),
                ("pending", counts.Pending.ToString(CultureInfo.InvariantCulture)),
                ("link", _Services.Email.Link("/admin/content-review?status=Flagged"))).ConfigureAwait(false);
            return counts.FlaggedSince;
        }

        /// <summary>
        /// POST /api/v1/admin/content-review/enumerate.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page with exact totals.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden.</exception>
        public async Task<EnumerationResult<ContentChangeView>> EnumerateAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            RequireAdmin(actor);
            EnumerationRequest request = _Services.Enumeration.Prepare(query, ContentReviewCollections.Queue, true);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            ContentChangeFilter filter = new ContentChangeFilter
            {
                Statuses = DirectoryFilters.Enums<ContentReviewStatusEnum>(request.Query, "status", errors),
                Severities = DirectoryFilters.Enums<ContentReviewSeverityEnum>(request.Query, "severity", errors),
                Clients = DirectoryFilters.Enums<ContentChangeClientEnum>(request.Query, "client", errors),
                Actions = DirectoryFilters.Enums<ContentChangeActionEnum>(request.Query, "action", errors),
                TargetTypes = DirectoryFilters.Strings(request.Query, "targetType"),
                TargetId = request.Query.FilterString("targetId"),
                ActorUserId = request.Query.FilterString("actorUserId"),
                AuthorUserId = request.Query.FilterString("authorUserId")
            };
            foreach (string raw in DirectoryFilters.Strings(request.Query, "category"))
            {
                ContentReviewCategoryEnum? category = ContentReviewVerdict.CategoryOf(raw);
                if (category == null || Int32.TryParse(raw, out int _)) errors.TryAdd("filters.category", "Use one of " + String.Join(", ", Enum.GetNames<ContentReviewCategoryEnum>()) + ".");
                else filter.Categories.Add(category.Value);
            }
            string? minimum = request.Query.FilterString("minimumSeverity");
            if (!String.IsNullOrWhiteSpace(minimum))
            {
                if (!Int32.TryParse(minimum, out int _) && Enum.TryParse(minimum.Trim(), true, out ContentReviewSeverityEnum parsed) && Enum.IsDefined(parsed)) filter.MinimumSeverity = parsed;
                else errors.TryAdd("filters.minimumSeverity", "Use one of " + String.Join(", ", Enum.GetNames<ContentReviewSeverityEnum>()) + ".");
            }
            if (filter.TargetTypes.Any(t => t.Length > 64)) errors.TryAdd("filters.targetType", "At most 64 characters each.");
            DirectoryFilters.ThrowIfAny(errors);
            EnumerationResult<ContentChangeRecord> page = await _Services.Database.ContentChanges.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            return page.With(await ViewsAsync(page.Objects, token).ConfigureAwait(false));
        }

        /// <summary>
        /// GET /api/v1/admin/content-review/{id}.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Row identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Row.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task<ContentChangeView> GetAsync(ContentActor actor, string id, CancellationToken token = default)
        {
            RequireAdmin(actor);
            ContentChangeRecord row = await RequireAsync(id, token).ConfigureAwait(false);
            return (await ViewsAsync(new List<ContentChangeRecord> { row }, token).ConfigureAwait(false))[0];
        }

        /// <summary>
        /// GET /api/v1/admin/content-review/summary.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Summary.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden.</exception>
        public async Task<ContentReviewSummary> SummaryAsync(ContentActor actor, CancellationToken token = default)
        {
            RequireAdmin(actor);
            DateTime now = Clock();
            ContentReviewCounts counts = await _Services.Database.ContentChanges.CountsAsync(now.AddHours(-24), token).ConfigureAwait(false);
            string? problem = await ProblemAsync(false, token).ConfigureAwait(false);
            JobDefinition? definition = await _Services.Database.Jobs.ReadDefinitionAsync(ReviewJobKey, token).ConfigureAwait(false);
            Dictionary<string, JobRun> latest = await _Services.Database.Jobs.ReadLatestRunsAsync(new[] { ReviewJobKey }, token).ConfigureAwait(false);
            latest.TryGetValue(ReviewJobKey, out JobRun? run);
            return new ContentReviewSummary
            {
                Pending = counts.Pending,
                Flagged = counts.Flagged,
                FlaggedLast24Hours = counts.FlaggedSince,
                Failing = counts.Failing,
                OldestPendingUtc = counts.OldestPendingUtc,
                ReviewerReady = problem == null,
                ReviewerProblem = problem,
                ReviewerMessage = ProblemMessage(problem),
                LastRunStatus = run?.Status.ToString(),
                LastRunUtc = run?.CreatedUtc,
                NextRunUtc = definition?.Enabled == true ? definition.NextRunUtc : null
            };
        }

        /// <summary>
        /// POST /api/v1/admin/content-review/{id}/action: Dismiss marks the row Dismissed; Hide and Remove run the
        /// moderation action on the content (POST /api/v1/admin/moderation/actions); Warn records a Warn moderation action
        /// and notifies the author; Mute and Ban run the member actions on the author. Every action but Dismiss marks the
        /// row Actioned. Audited as contentReview.&lt;action&gt;.
        /// </summary>
        /// <param name="actor">Administrator.</param>
        /// <param name="info">Request information.</param>
        /// <param name="id">Row identifier.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound, 409 NotModeratable or NoAuthor, and the errors of the routed action.</exception>
        public async Task<ContentReviewActionResult> ActAsync(ContentActor actor, RequestInfo info, string id, ContentReviewActionRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            RequireAdmin(actor);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            ContentReviewActionEnum action = ContentReviewActionEnum.Dismiss;
            if (String.IsNullOrWhiteSpace(request.Action) || Int32.TryParse(request.Action, out int _) || !Enum.TryParse(request.Action.Trim(), true, out action) || !Enum.IsDefined(action))
                errors["action"] = "Use Dismiss, Hide, Remove, Warn, Mute, or Ban.";
            string reason = (request.Reason ?? String.Empty).Trim();
            if (action != ContentReviewActionEnum.Dismiss && (reason.Length < MinReasonLength || reason.Length > MaxReasonLength)) errors["reason"] = "Use " + MinReasonLength + " to " + MaxReasonLength + " characters.";
            if (reason.Length > MaxReasonLength) errors["reason"] = "Use " + MinReasonLength + " to " + MaxReasonLength + " characters.";
            string? note = String.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
            if (note != null && note.Length > 2000) errors["note"] = "At most 2000 characters.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);

            ContentChangeRecord row = await RequireAsync(id, token).ConfigureAwait(false);
            string? author = row.AuthorUserId ?? row.ActorUserId;
            TargetTypeEnum? moderationTarget = ModerationTargetOf(row.TargetType);
            string? moderationActionId = null;
            string summary = action + (reason.Length > 0 ? ": " + reason : String.Empty) + (note != null ? " (" + note + ")" : String.Empty);
            DateTime now = Clock();
            switch (action)
            {
                case ContentReviewActionEnum.Dismiss:
                    break;
                case ContentReviewActionEnum.Hide:
                case ContentReviewActionEnum.Remove:
                    if (moderationTarget == null || !_Services.Moderation.SupportsActions(moderationTarget.Value))
                        throw new ApiErrorException(409, "NotModeratable", "A " + row.TargetType + " cannot be hidden or removed from the moderation console. Warn, mute, or ban the author instead.");
                    ModerationActionResult result = await _Services.Moderation.ActAsync(actor, info, new ModerationActionRequest
                    {
                        TargetType = moderationTarget.Value.ToString(),
                        TargetId = row.TargetId,
                        Action = action.ToString(),
                        Reason = reason,
                        Note = note,
                        NotifyAuthor = request.NotifyAuthor
                    }, token).ConfigureAwait(false);
                    moderationActionId = result.Action.Id;
                    break;
                case ContentReviewActionEnum.Warn:
                    if (author == null) throw new ApiErrorException(409, "NoAuthor", "This contribution has no author to warn.");
                    moderationActionId = await WarnAsync(actor, info, row, author, moderationTarget, reason, note, token).ConfigureAwait(false);
                    break;
                case ContentReviewActionEnum.Mute:
                    if (author == null) throw new ApiErrorException(409, "NoAuthor", "This contribution has no author to mute.");
                    await _Services.AdminMembers.MuteAsync(author, new MuteRequest { UntilUtc = request.UntilUtc, Reason = reason }, info, token).ConfigureAwait(false);
                    break;
                case ContentReviewActionEnum.Ban:
                    if (author == null) throw new ApiErrorException(409, "NoAuthor", "This contribution has no author to ban.");
                    await _Services.AdminMembers.BanAsync(author, reason, info, token).ConfigureAwait(false);
                    break;
            }

            ContentReviewStatusEnum status = action == ContentReviewActionEnum.Dismiss ? ContentReviewStatusEnum.Dismissed : ContentReviewStatusEnum.Actioned;
            await _Services.Database.ContentChanges.ResolveAsync(row.Id, status, summary, ReviewerOf(actor), moderationActionId, now, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "contentReview." + action.ToString().ToLowerInvariant(), "ContentChange", row.Id, summary.Length > 500 ? summary.Substring(0, 500) : summary, token).ConfigureAwait(false);
            TrellisKitTelemetry.Increment(TrellisKitTelemetry.ModerationActions, "action", "content_review_" + action.ToString().ToLowerInvariant(), "target_type", row.TargetType);
            ContentChangeRecord updated = await RequireAsync(row.Id, token).ConfigureAwait(false);
            return new ContentReviewActionResult
            {
                Change = (await ViewsAsync(new List<ContentChangeRecord> { updated }, token).ConfigureAwait(false))[0],
                Action = action.ToString(),
                ModerationActionId = moderationActionId
            };
        }

        /// <summary>
        /// The moderation target type of a content changelog target type (VenueReviewReply acts on its VenueReview), or
        /// null when it is not a target type.
        /// </summary>
        /// <param name="targetType">Content changelog target type.</param>
        /// <returns>Target type, or null.</returns>
        public static TargetTypeEnum? ModerationTargetOf(string? targetType)
        {
            if (String.IsNullOrWhiteSpace(targetType)) return null;
            if (String.Equals(targetType, "VenueReviewReply", StringComparison.Ordinal)) return TargetTypeEnum.VenueReview;
            if (Int32.TryParse(targetType, out int _)) return null;
            return Enum.TryParse(targetType, false, out TargetTypeEnum parsed) && Enum.IsDefined(parsed) ? parsed : null;
        }

        #endregion

        #region Private-Methods

        private static void RequireAdmin(ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (!actor.IsSystemAdmin) throw ApiErrorException.Forbidden("Only administrators can read or act on the content review queue.");
        }

        private async Task<ContentChangeRecord> RequireAsync(string id, CancellationToken token)
        {
            return await _Services.Database.ContentChanges.ReadAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Content change");
        }

        private async Task FailAsync(ContentChangeRecord row, string error, CancellationToken token)
        {
            int attempts = await _Services.Database.ContentChanges.RecordFailureAsync(row.Id, error, Clock(), token).ConfigureAwait(false);
            if (attempts < _Services.Settings.ContentReview.MaxAttempts) return;
            string note = "The reviewer could not classify this contribution after " + attempts.ToString(CultureInfo.InvariantCulture) + " attempts; an administrator should look at it.";
            bool flagged = await _Services.Database.ContentChanges.CompleteReviewAsync(row.Id, ContentReviewStatusEnum.Flagged, new List<ContentReviewCategoryEnum>(), ContentReviewSeverityEnum.Low, note,
                "system", ContentReviewerKindEnum.System, Clock(), token).ConfigureAwait(false);
            if (!flagged) return;
            ContentChangeRecord current = (await _Services.Database.ContentChanges.ReadAsync(row.Id, token).ConfigureAwait(false)) ?? row;
            await NotifyFlaggedAsync(current, token).ConfigureAwait(false);
        }

        private async Task<string> WarnAsync(ContentActor actor, RequestInfo info, ContentChangeRecord row, string author, TargetTypeEnum? moderationTarget, string reason, string? note, CancellationToken token)
        {
            TargetTypeEnum targetType = moderationTarget ?? TargetTypeEnum.User;
            string targetId = moderationTarget == null ? author : row.TargetId;
            Notification? created = await _Services.Notifications.CreateAsync(new NotificationDraft
            {
                RecipientUserId = author,
                Type = NotificationTypeEnum.ModerationAction,
                TargetType = targetType,
                TargetId = targetId,
                SubjectNoun = Noun(row.TargetType),
                ModerationAction = ModerationActionTypeEnum.Warn,
                ModerationReason = reason,
                Body = Excerpt(row.Snapshot),
                QueuePush = true
            }, token).ConfigureAwait(false);
            ModerationAction recorded = await _Services.Database.ModerationActions.CreateAsync(new ModerationAction
            {
                Id = IdGenerator.Generate(Constants.ModerationActionIdPrefix),
                ModeratorUserId = actor.UserId,
                BreakGlass = actor.IsBreakGlass,
                TargetType = targetType,
                TargetId = targetId,
                TargetAuthorUserId = author,
                Action = ModerationActionTypeEnum.Warn,
                Reason = reason,
                Note = note,
                NotifiedAuthor = created != null,
                CreatedUtc = Clock()
            }, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "moderation.warn", targetType.ToString(), targetId, reason, token).ConfigureAwait(false);
            return recorded.Id;
        }

        private async Task<List<ContentChangeView>> ViewsAsync(List<ContentChangeRecord> rows, CancellationToken token)
        {
            List<string?> people = rows.SelectMany(r => new[] { r.ActorUserId, r.AuthorUserId }).Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            Dictionary<string, UserSummary> summaries = people.Count == 0 ? new Dictionary<string, UserSummary>(StringComparer.Ordinal) : await _Services.Summaries.BuildAsync(people, token).ConfigureAwait(false);
            List<ContentChangeView> views = rows.Select(r =>
            {
                TargetTypeEnum? target = ModerationTargetOf(r.TargetType);
                return new ContentChangeView
                {
                    Id = r.Id,
                    TargetType = r.TargetType,
                    TargetId = r.TargetId,
                    ContextType = r.ContextType,
                    ContextId = r.ContextId,
                    Action = r.Action.ToString(),
                    Actor = r.ActorUserId != null && summaries.TryGetValue(r.ActorUserId, out UserSummary? a) ? a : null,
                    Author = r.AuthorUserId != null && summaries.TryGetValue(r.AuthorUserId, out UserSummary? b) ? b : null,
                    Client = r.Client.ToString(),
                    Snapshot = r.Snapshot,
                    MediaRefs = r.MediaRefs.Take(50).ToList(),
                    ReviewStatus = r.ReviewStatus.ToString(),
                    ReviewedUtc = r.ReviewedUtc,
                    ReviewedBy = r.ReviewedBy,
                    ReviewerKind = r.ReviewerKind?.ToString(),
                    Categories = r.Categories.Select(c => c.ToString()).ToList(),
                    Severity = r.Severity.ToString(),
                    ReviewerNote = r.ReviewerNote,
                    ReviewAttempts = r.ReviewAttempts,
                    LastError = r.LastError,
                    NotifiedUtc = r.NotifiedUtc,
                    ModerationActionId = r.ModerationActionId,
                    ModerationTargetType = target != null && _Services.Moderation.SupportsActions(target.Value) ? target.Value.ToString() : null,
                    CreatedUtc = r.CreatedUtc,
                    UpdatedUtc = r.UpdatedUtc
                };
            }).ToList();
            return await _Services.Metadata.AttachAsync(TargetTypeEnum.ContentChange, views, v => v.Id, true, token).ConfigureAwait(false);
        }

        private string Prompt(ContentChangeRecord row)
        {
            int max = _Services.Settings.ContentReview.MaxReviewCharacters;
            string text = row.Snapshot.Length > max ? row.Snapshot.Substring(0, max) : row.Snapshot;
            StringBuilder prompt = new StringBuilder();
            prompt.Append("Contribution: ").Append(row.TargetType).Append(" (").Append(row.Action == ContentChangeActionEnum.Create ? "new" : "edited").Append(')');
            if (!String.IsNullOrEmpty(row.ContextType)) prompt.Append(", in a ").Append(row.ContextType);
            prompt.Append(".\n");
            if (row.MediaRefs.Count > 0) prompt.Append("It also has ").Append(row.MediaRefs.Count.ToString(CultureInfo.InvariantCulture)).Append(" media attachments that are not shown.\n");
            if (text.Length < row.Snapshot.Length) prompt.Append("The text is cut to its first ").Append(max.ToString(CultureInfo.InvariantCulture)).Append(" characters.\n");
            prompt.Append("CONTENT START\n").Append(text).Append("\nCONTENT END");
            return prompt.ToString();
        }

        private static string ReviewerOf(ContentActor actor)
        {
            if (!String.IsNullOrEmpty(actor.UserId)) return "user:" + actor.UserId;
            return actor.IsBreakGlass ? "admin-token" : "admin";
        }

        private static string Title(ContentChangeRecord row)
        {
            string categories = row.Categories.Count == 0 ? "needs a look" : String.Join(", ", row.Categories.Select(Label));
            return "Content review flagged " + Article(Noun(row.TargetType)) + " " + Noun(row.TargetType) + ": " + categories + " (" + row.Severity + ")";
        }

        private static string Label(ContentReviewCategoryEnum category)
        {
            switch (category)
            {
                case ContentReviewCategoryEnum.SexualContent:
                    return "sexual content";
                case ContentReviewCategoryEnum.HarassmentAndHate:
                    return "harassment and hate";
                case ContentReviewCategoryEnum.IllegalActivity:
                    return "illegal activity";
                case ContentReviewCategoryEnum.SelfHarm:
                    return "self-harm";
                case ContentReviewCategoryEnum.PersonalData:
                    return "personal data";
                default:
                    return category.ToString().ToLowerInvariant();
            }
        }

        private static string Noun(string targetType)
        {
            switch (targetType)
            {
                case "FeedPost":
                    return "post";
                case "Comment":
                    return "comment";
                case "Thread":
                    return "thread";
                case "PollOption":
                    return "poll option";
                case "Message":
                    return "message";
                case "VenueReview":
                    return "review";
                case "VenueReviewReply":
                    return "review reply";
                case "User":
                    return "profile";
                case "Media":
                case "VenueMedia":
                    return "media upload";
                case "DirectorySubmission":
                    return "listing submission";
                case "Event":
                    return "event";
                case "Lesson":
                    return "lesson";
                case "Idea":
                    return "idea";
                case "Ticket":
                case "TicketMessage":
                    return "support request";
                case "AssistantMessage":
                    return "assistant message";
                case "Favorite":
                    return "saved item note";
                default:
                    return "contribution";
            }
        }

        private static string Article(string noun)
        {
            return noun.Length > 0 && "aeiouAEIOU".IndexOf(noun[0]) >= 0 ? "an" : "a";
        }

        private static string Excerpt(string snapshot)
        {
            string text = (snapshot ?? String.Empty).Replace('\n', ' ').Trim();
            return text.Length > _ExcerptLength ? text.Substring(0, _ExcerptLength) : text;
        }

        #endregion
    }
}
