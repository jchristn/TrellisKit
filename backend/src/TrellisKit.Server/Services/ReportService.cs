namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Member reports (contract section 9.1): POST /api/v1/reports on a feed post, comment, or member the caller can
    /// see. Muted members and members without canPost may report. One Open report per reporter and target (a repeat
    /// returns the existing report), moderation.reportsPerHour, and auto-hide through
    /// <see cref="ModerationService.TryAutoHideAsync"/>. Reports carry staff-only labels and tags (type Report).
    /// Thread safety: safe to call concurrently; duplicates are prevented by ux_reports_open.
    /// </summary>
    public class ReportService
    {
        #region Public-Members

        /// <summary>
        /// Rate limit bucket of report creation (moderation.reportsPerHour).
        /// </summary>
        public const string RateBucket = "reports";

        /// <summary>
        /// Longest details text.
        /// </summary>
        public const int MaxDetailsLength = 1000;

        /// <summary>
        /// Shortest details text when the reason is Other.
        /// </summary>
        public const int MinOtherDetailsLength = 10;

        /// <summary>
        /// Resolves forum report targets (set by the forum module, Phase 3 section 11.1): threads, comments on threads,
        /// promoted feed cards (reported against their thread), and LinkedFeedPost threads (reported against their feed
        /// post). Returns null when the target is not a forum target. A non-null ForumId files the report in Forum scope.
        /// </summary>
        public Func<ContentActor, TargetTypeEnum, string, CancellationToken, Task<ModerationTarget?>>? ForumTargets { get; set; } = null;

        /// <summary>
        /// Resolvers for report target types added after Phase 3 (direct messages and conversations, ideas), tried in order
        /// before the built-in types. Each returns null for target types it does not handle and throws the route error for
        /// a target it handles but the caller may not report. Register during module attachment only.
        /// </summary>
        public List<Func<ContentActor, TargetTypeEnum, string, CancellationToken, Task<ModerationTarget?>>> ExtraTargets { get; } =
            new List<Func<ContentActor, TargetTypeEnum, string, CancellationToken, Task<ModerationTarget?>>>();

        /// <summary>
        /// Report target types added by modules (for example Idea), accepted by targetType validation and resolved by
        /// <see cref="ExtraTargets"/>. Register during module attachment only.
        /// </summary>
        public HashSet<TargetTypeEnum> ExtraTargetTypes { get; } = new HashSet<TargetTypeEnum>();

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="services">Application services (read on each call).</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public ReportService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// File a report. Validation (including the optional labels and tags), then visibility (404 when the caller cannot
        /// see the target), then CannotReportSelf, then an existing Open report is returned (Created false, not charged,
        /// metadata unchanged), then the rate limit. Labels and tags are written on the new report; non-staff callers
        /// cannot set staff: entries.
        /// </summary>
        /// <param name="actor">Caller (a member session).</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Outcome (Created true for 201, false for 200).</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or CannotReportSelf, 403 Forbidden (admin token), 404 NotFound, 429 RateLimited.</exception>
        public async Task<ReportCreateOutcome> CreateAsync(ContentActor actor, ReportRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            string userId = actor.RequireUserId();
            using (Activity? activity = TrellisKitTelemetry.StartSpan("reports.create"))
            {
                Dictionary<string, string> errors = new Dictionary<string, string>();
                TargetTypeEnum? targetType = ParseTargetType(request.TargetType, errors);
                string targetId = (request.TargetId ?? String.Empty).Trim();
                if (targetId.Length == 0) errors["targetId"] = "Required.";
                ReportReasonEnum? reason = ParseReason(request.Reason, errors);
                string? details = NormalizeDetails(request.Details, reason, errors);
                if (errors.Count > 0) throw ApiErrorException.Validation(errors);

                await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, actor.IsStaffFor(TargetTypeEnum.Report), token, TargetTypeEnum.Report).ConfigureAwait(false);

                ModerationTarget target = await RequireVisibleTargetAsync(actor, targetType!.Value, targetId, token).ConfigureAwait(false);
                RequireReasonFor(target, reason!.Value);
                if (String.Equals(target.AuthorUserId, userId, StringComparison.Ordinal))
                    throw new ApiErrorException(400, "CannotReportSelf", target.TargetType == TargetTypeEnum.User ? "You cannot report yourself." : "You cannot report your own content.");

                Report? existing = await _Services.Database.Reports.ReadOpenByReporterAsync(userId, target.TargetType, target.TargetId, token).ConfigureAwait(false);
                if (existing != null) return await OutcomeAsync(actor, existing, false, false, token).ConfigureAwait(false);

                _Services.Limits.EnforceMember(actor, RateBucket, _Services.Settings.Moderation.ReportsPerHour, RateLimitService.Hour);
                DateTime now = DateTime.UtcNow;
                Report report = new Report
                {
                    Id = IdGenerator.Generate(Constants.ReportIdPrefix),
                    ReporterUserId = userId,
                    TargetType = target.TargetType,
                    TargetId = target.TargetId,
                    TargetAuthorUserId = String.IsNullOrEmpty(target.AuthorUserId) ? null : target.AuthorUserId,
                    ScopeType = target.ScopeType ?? (target.AssistantId != null ? ModerationService.AssistantScope : target.ForumId != null ? ModerationService.ForumScope : ModerationService.FeedScope),
                    ScopeId = target.ScopeType != null ? (target.ScopeId ?? "*") : (target.AssistantId ?? target.ForumId ?? "*"),
                    Reason = reason!.Value,
                    Details = details,
                    Status = ReportStatusEnum.Open,
                    DueUtc = now.AddHours(_Services.Settings.Moderation.ReviewTargetHours),
                    CreatedUtc = now
                };
                Report? stored = await _Services.Database.Reports.CreateAsync(report, token).ConfigureAwait(false);
                if (stored == null)
                {
                    existing = await _Services.Database.Reports.ReadOpenByReporterAsync(userId, target.TargetType, target.TargetId, token).ConfigureAwait(false);
                    if (existing != null) return await OutcomeAsync(actor, existing, false, false, token).ConfigureAwait(false);
                    throw new InvalidOperationException("The report was neither stored nor found.");
                }
                if (request.Labels != null || request.Tags != null) await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.Report, stored.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
                await _Services.Email.NotifyStaffAsync("staff_new_report", "report:" + stored.Id, token, ("targetType", Email.EmailLabels.TargetType(target.TargetType)), ("reason", Email.EmailLabels.ReportReason(stored.Reason)), ("link", _Services.Email.Link("/admin/moderation"))).ConfigureAwait(false);
                TrellisKitTelemetry.Add(TrellisKitTelemetry.Reports, 1, "target_type", target.TargetType.ToString(), "reason", stored.Reason.ToString());
                if (target.TargetType == TargetTypeEnum.FeedPost) await _Services.Feed.RecomputeOpenReportCountAsync(target.TargetId, token).ConfigureAwait(false);
                else if (target.TargetType == TargetTypeEnum.Thread) await _Services.Database.Threads.RecomputeOpenReportCountAsync(target.TargetId, token).ConfigureAwait(false);
                ContentTargetProvider? provider = ProviderOf(target.TargetType);
                if (provider != null)
                {
                    await provider.RecomputeReportsAsync(target, token).ConfigureAwait(false);
                    await provider.ReportedAsync(target, stored, token).ConfigureAwait(false);
                }
                if (provider == null && target.TargetType != TargetTypeEnum.User && _Services.Moderation.ExtensionFor(target.TargetType) != null)
                    await _Services.Moderation.ExtensionFor(target.TargetType)!.RecomputeReportsAsync(target, token).ConfigureAwait(false);
                bool hidden = false;
                // Assistant messages: snapshot the conversation for review (it outlives the conversation); auto-hide does not apply.
                if (target.TargetType == TargetTypeEnum.AssistantMessage) await _Services.Ai.Reports.SnapshotAsync(target.TargetId, Ai.AssistantReportService.ReasonReport, stored.Id, null, token).ConfigureAwait(false);
                else if (provider != null) hidden = await _Services.Moderation.TryAutoHideTargetAsync(provider, target, token).ConfigureAwait(false);
                else if (target.TargetType != TargetTypeEnum.User && !target.SkipAutoHide) hidden = await _Services.Moderation.TryAutoHideAsync(target.TargetType, target.TargetId, target.AuthorUserId, token).ConfigureAwait(false);
                if (target.AfterReport != null) await target.AfterReport(stored, token).ConfigureAwait(false);
                activity?.SetTag("report.target_type", target.TargetType.ToString());
                activity?.SetStatus(ActivityStatusCode.Ok);
                return await OutcomeAsync(actor, stored, true, hidden, token).ConfigureAwait(false);
            }
        }

        #endregion

        #region Private-Methods

        private async Task<ReportCreateOutcome> OutcomeAsync(ContentActor actor, Report report, bool created, bool hidden, CancellationToken token)
        {
            ObjectMetadata metadata = await _Services.Metadata.LoadOneAsync(TargetTypeEnum.Report, report.Id, actor.IsStaffFor(TargetTypeEnum.Report), token).ConfigureAwait(false);
            return new ReportCreateOutcome
            {
                Created = created,
                AutoHidden = hidden,
                Report = new ReportCreatedView { Id = report.Id, Status = report.Status, CreatedUtc = report.CreatedUtc, Labels = metadata.Labels, Tags = metadata.Tags }
            };
        }

        private async Task<ModerationTarget> RequireVisibleTargetAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, CancellationToken token)
        {
            foreach (Func<ContentActor, TargetTypeEnum, string, CancellationToken, Task<ModerationTarget?>> resolver in ExtraTargets)
            {
                ModerationTarget? extra = await resolver(actor, targetType, targetId, token).ConfigureAwait(false);
                if (extra != null) return extra;
            }
            if (ForumTargets != null)
            {
                ModerationTarget? forum = await ForumTargets(actor, targetType, targetId, token).ConfigureAwait(false);
                if (forum != null) return forum;
            }
            if (targetType == TargetTypeEnum.Thread) throw ApiErrorException.NotFound("Thread");
            ContentTargetProvider? provider = ProviderOf(targetType);
            if (provider != null)
            {
                if (!await provider.IsVisibleAsync(actor, targetType, targetId, token).ConfigureAwait(false)) throw ApiErrorException.NotFound(targetType.ToString());
                ModerationTarget? own = await provider.LoadModerationTargetAsync(targetType, targetId, token).ConfigureAwait(false);
                if (own == null || own.IsDeleted) throw ApiErrorException.NotFound(targetType.ToString());
                own.ScopeType = provider.ScopeType(targetType);
                return own;
            }
            switch (targetType)
            {
                case TargetTypeEnum.AssistantMessage:
                    return await _Services.Ai.Reports.RequireReportableAsync(actor, targetId, token).ConfigureAwait(false);
                case TargetTypeEnum.FeedPost:
                    FeedPost post = await _Services.ContentAccess.RequireVisiblePostAsync(actor, targetId, token).ConfigureAwait(false);
                    if ((post.Kind == FeedPostKindEnum.PromotedLesson || post.Kind == FeedPostKindEnum.PromotedEvent) && post.SourceId != null)
                    {
                        // A promoted lesson's or event's card resolves to its source, as promoted threads do (section 6.3).
                        return await RequireVisibleTargetAsync(actor, post.Kind == FeedPostKindEnum.PromotedLesson ? TargetTypeEnum.Lesson : TargetTypeEnum.Event, post.SourceId, token).ConfigureAwait(false);
                    }
                    return new ModerationTarget { TargetType = targetType, TargetId = post.Id, AuthorUserId = post.AuthorUserId, Post = post };
                case TargetTypeEnum.Comment:
                    Comment comment = await _Services.ContentAccess.RequireCommentAsync(actor, targetId, token).ConfigureAwait(false);
                    if (!ContentAccessService.CanSeeCommentContent(comment, actor)) throw ApiErrorException.NotFound("Comment");
                    ModerationTarget commentTarget = new ModerationTarget { TargetType = targetType, TargetId = comment.Id, AuthorUserId = comment.AuthorUserId, Comment = comment };
                    ContentTargetProvider? host = ProviderOf(comment.TargetType);
                    if (host != null)
                    {
                        commentTarget.HostType = comment.TargetType;
                        commentTarget.ScopeType = host.ScopeType(comment.TargetType);
                    }
                    return commentTarget;
                default:
                    User? user = await _Services.Database.Users.ReadAsync(targetId, token).ConfigureAwait(false);
                    if (user == null || user.IsDeleted) throw ApiErrorException.NotFound("Member");
                    return new ModerationTarget { TargetType = TargetTypeEnum.User, TargetId = user.Id, AuthorUserId = user.Id, User = user };
            }
        }

        private TargetTypeEnum? ParseTargetType(string? raw, Dictionary<string, string> errors)
        {
            if (ModerationService.TryParseEnum(raw, out TargetTypeEnum type) && (type == TargetTypeEnum.FeedPost || type == TargetTypeEnum.Comment || type == TargetTypeEnum.User || type == TargetTypeEnum.Thread ||
                type == TargetTypeEnum.AssistantMessage || ProviderOf(type) != null || ExtraTargetTypes.Contains(type))) return type;
            List<string> names = new List<string> { "FeedPost", "Thread", "Comment", "User", "AssistantMessage" };
            names.AddRange(_Services.ContentTargets.TargetTypes.Concat(ExtraTargetTypes).Distinct().Select(t => t.ToString()).OrderBy(t => t, StringComparer.Ordinal));
            errors["targetType"] = "Use " + String.Join(", ", names) + ".";
            return null;
        }

        private static ReportReasonEnum? ParseReason(string? raw, Dictionary<string, string> errors)
        {
            if (ModerationService.TryParseEnum(raw, out ReportReasonEnum reason) && reason != ReportReasonEnum.Filter) return reason;
            errors["reason"] = "Use Spam, Harassment, HateSpeech, SexualContent, Violence, SelfHarm, Impersonation, IntellectualProperty, Other, Inaccurate, or PermanentlyClosed.";
            return null;
        }

        private void RequireReasonFor(ModerationTarget target, ReportReasonEnum reason)
        {
            ContentTargetProvider? provider = ProviderOf(target.TargetType);
            bool accepted = provider != null
                ? provider.AcceptsReportReason(target.TargetType, reason)
                : reason != ReportReasonEnum.Inaccurate && reason != ReportReasonEnum.PermanentlyClosed;
            if (!accepted) throw ApiErrorException.Field("reason", reason + " does not apply to " + target.TargetType + " reports.");
        }

        private ContentTargetProvider? ProviderOf(TargetTypeEnum targetType)
        {
            if (targetType == TargetTypeEnum.FeedPost || targetType == TargetTypeEnum.Thread || targetType == TargetTypeEnum.Comment || targetType == TargetTypeEnum.User || targetType == TargetTypeEnum.AssistantMessage) return null;
            return _Services.ContentTargets.Find(targetType);
        }

        private static string? NormalizeDetails(string? raw, ReportReasonEnum? reason, Dictionary<string, string> errors)
        {
            string? details = null;
            if (raw != null)
            {
                if (!MarkdownNormalizer.IsWellFormed(raw))
                {
                    errors["details"] = "Must be valid UTF-8 text.";
                    return null;
                }
                details = raw.Trim();
                if (details.Length == 0) details = null;
            }
            int length = details == null ? 0 : MarkdownNormalizer.Length(details);
            if (length > MaxDetailsLength) errors["details"] = "At most " + MaxDetailsLength + " characters.";
            else if (reason == ReportReasonEnum.Other && length < MinOtherDetailsLength) errors["details"] = "Tell us more (at least " + MinOtherDetailsLength + " characters) when the reason is Other.";
            return details;
        }

        #endregion
    }
}
