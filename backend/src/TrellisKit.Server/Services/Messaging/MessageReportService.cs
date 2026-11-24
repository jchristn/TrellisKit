namespace TrellisKit.Server.Services.Messaging
{
    using System;
    using System.Collections.Generic;
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
    using TrellisKit.Server.Services.Realtime;

    /// <summary>
    /// Reports of direct messages and conversations (docs/PHASE5_CONTRACT.md sections 3.9 and 11): the report target
    /// resolver (scope Messaging, no auto-hide), the frozen snapshot written when a report is created, the Messaging
    /// moderation queue, the audited snapshot read, the Remove and Restore actions on the live message, and the
    /// RevokeMessaging member action. Only SystemAdmin and Messaging moderators may use the moderator methods, and no
    /// method opens a conversation.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class MessageReportService
    {
        #region Public-Members

        /// <summary>
        /// Report scope type of direct message reports.
        /// </summary>
        public const string Scope = "Messaging";

        /// <summary>
        /// Audit action of a snapshot read.
        /// </summary>
        public const string SnapshotViewAction = "message.snapshot.view";

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;
        private readonly MessagingModule _Module;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <param name="module">Messaging module.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public MessageReportService(ApplicationServices services, MessagingModule module)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            _Module = module ?? throw new ArgumentNullException(nameof(module));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The report target resolver (registered in ReportService.ExtraTargets): a message or conversation the reporter
        /// belongs to and can see.
        /// </summary>
        /// <param name="actor">Reporter.</param>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Target, or null for other types.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<ModerationTarget?> ReportTargetAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (targetType != TargetTypeEnum.Message && targetType != TargetTypeEnum.Conversation) return null;
            _Module.Policy.RequireFeature();
            string me = actor.RequireUserId();
            if (targetType == TargetTypeEnum.Message)
            {
                MessageContext context = await _Module.Messages.RequireMessageAsync(me, targetId, token).ConfigureAwait(false);
                if (context.Message.State == MessageStateEnum.Deleted) throw ApiErrorException.NotFound("Message");
                return new ModerationTarget
                {
                    TargetType = TargetTypeEnum.Message,
                    TargetId = context.Message.Id,
                    AuthorUserId = context.Message.AuthorUserId ?? String.Empty,
                    ScopeType = Scope,
                    ScopeId = "*",
                    SkipAutoHide = true,
                    AfterReport = (report, ct) => SnapshotAsync(report, context.Entry, context.Message, ct)
                };
            }
            ConversationEntry entry = await _Module.Conversations.RequireEntryAsync(me, targetId, token).ConfigureAwait(false);
            return new ModerationTarget
            {
                TargetType = TargetTypeEnum.Conversation,
                TargetId = entry.Conversation.Id,
                AuthorUserId = entry.Others.FirstOrDefault()?.UserId ?? String.Empty,
                ScopeType = Scope,
                ScopeId = "*",
                SkipAutoHide = true,
                AfterReport = (report, ct) => SnapshotAsync(report, entry, null, ct)
            };
        }

        /// <summary>
        /// Write the report snapshot: the reported message (or, for a conversation report, the latest message) and up to
        /// messaging.reportContextMessages earlier messages the reporter could see, tombstones as placeholders, attachment
        /// references. Later edits and deletes never change it.
        /// </summary>
        /// <param name="report">Stored report.</param>
        /// <param name="entry">Conversation from the reporter's side.</param>
        /// <param name="reported">The reported message, or null for a conversation report.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task SnapshotAsync(Report report, ConversationEntry entry, Message? reported, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(report);
            ArgumentNullException.ThrowIfNull(entry);
            int context = _Services.Settings.Messaging.ReportContextMessages;
            long at = reported?.Seq ?? entry.Conversation.LastSeq;
            List<Message> messages = await _Services.Database.Messages.LatestUpToAsync(entry.Conversation.Id, at, entry.Mine.HiddenBeforeSeq, context + 1, token).ConfigureAwait(false);
            string? reportedId = reported?.Id ?? messages.FirstOrDefault()?.Id;
            DateTime now = _Module.Clock();
            MessageReportSnapshot snapshot = new MessageReportSnapshot
            {
                Id = IdGenerator.Generate(MessagingIdPrefixes.ReportSnapshot),
                ReportId = report.Id,
                ConversationId = entry.Conversation.Id,
                ReporterUserId = report.ReporterUserId,
                CapturedUtc = now
            };
            List<MessageReportSnapshotItem> items = messages.OrderBy(m => m.Seq).Select(m => new MessageReportSnapshotItem
            {
                Id = IdGenerator.Generate(MessagingIdPrefixes.ReportSnapshotItem),
                SnapshotId = snapshot.Id,
                MessageId = m.Id,
                Seq = m.Seq,
                AuthorUserId = m.AuthorUserId,
                Body = m.State == MessageStateEnum.Visible ? m.Body : String.Empty,
                OriginalBody = m.State == MessageStateEnum.Visible ? m.OriginalBody : null,
                State = m.State,
                IsReported = m.Id == reportedId,
                AttachmentSummary = m.State == MessageStateEnum.Visible && m.MediaIds.Count > 0 ? String.Join(", ", m.MediaIds) : null,
                EditedUtc = m.EditedUtc,
                CreatedUtc = m.CreatedUtc
            }).ToList();
            await _Services.Database.Messages.CreateSnapshotAsync(snapshot, items, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/moderation/messaging/reports/enumerate: the Messaging report queue (snapshot ids, never a link
        /// into a conversation).
        /// </summary>
        /// <param name="actor">Moderator.</param>
        /// <param name="query">Query (filters status, targetType).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 400 ValidationFailed.</exception>
        public async Task<EnumerationResult<MessagingReportItem>> QueueAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            RequireModerator(actor);
            EnumerationQuery q = query ?? new EnumerationQuery();
            Dictionary<string, string> errors = new Dictionary<string, string>();
            ReportStatusEnum status = q.FilterEnum<ReportStatusEnum>("status", errors) ?? ReportStatusEnum.Open;
            TargetTypeEnum? type = q.FilterEnum<TargetTypeEnum>("targetType", errors);
            if (type != null && type != TargetTypeEnum.Message && type != TargetTypeEnum.Conversation) errors["filters.targetType"] = "Use Message or Conversation.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, MessagingCollections.Reports, true, "messaging-reports");
            EnumerationResult<Report> page = await _Services.Database.Messages.EnumerateReportsAsync(request, status, type, token).ConfigureAwait(false);
            Dictionary<string, MessageReportSnapshot> snapshots = await _Services.Database.Messages.ReadSnapshotsByReportsAsync(page.Objects.Select(r => r.Id), token).ConfigureAwait(false);
            Dictionary<string, UserSummary> users = await _Services.Summaries.BuildAsync(page.Objects.SelectMany(r => new[] { r.ReporterUserId, r.TargetAuthorUserId }), token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.Report, page.Objects.Select(r => r.Id), true, token).ConfigureAwait(false);
            return page.With(page.Objects.Select(r => new MessagingReportItem
            {
                ReportId = r.Id,
                TargetType = r.TargetType,
                TargetId = r.TargetId,
                Reason = r.Reason,
                Details = r.Details,
                Status = r.Status,
                Reporter = r.ReporterUserId != null && users.TryGetValue(r.ReporterUserId, out UserSummary? reporter) ? reporter : UserSummary.Deleted(),
                TargetAuthor = r.TargetAuthorUserId != null && users.TryGetValue(r.TargetAuthorUserId, out UserSummary? author) ? author : UserSummary.Deleted(),
                SnapshotId = snapshots.TryGetValue(r.Id, out MessageReportSnapshot? s) ? s.Id : null,
                Labels = metadata.TryGetValue(r.Id, out ObjectMetadata? m) ? m.Labels : new List<string>(),
                Tags = m != null ? m.Tags : new Dictionary<string, string>(),
                CreatedUtc = r.CreatedUtc
            }).ToList());
        }

        /// <summary>
        /// GET /api/v1/admin/moderation/messaging/reports/{reportId}/snapshot: the frozen snapshot of a report, audited
        /// (message.snapshot.view) on every view.
        /// </summary>
        /// <param name="actor">Moderator.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="reportId">Report identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Snapshot.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task<MessageSnapshotView> SnapshotViewAsync(ContentActor actor, RequestInfo info, string reportId, CancellationToken token = default)
        {
            RequireModerator(actor);
            ArgumentNullException.ThrowIfNull(info);
            Report? report = String.IsNullOrWhiteSpace(reportId) ? null : await _Services.Database.Reports.ReadAsync(reportId.Trim(), token).ConfigureAwait(false);
            if (report == null || report.ScopeType != Scope) throw ApiErrorException.NotFound("Report");
            MessageReportSnapshot? snapshot = await _Services.Database.Messages.ReadSnapshotByReportAsync(report.Id, token).ConfigureAwait(false);
            if (snapshot == null) throw ApiErrorException.NotFound("Snapshot");
            int bound = _Services.Settings.Messaging.ReportContextMessages + 1;
            List<MessageReportSnapshotItem> items = await _Services.Database.Messages.SnapshotItemsAsync(snapshot.Id, Math.Max(bound, 51), token).ConfigureAwait(false);
            Dictionary<string, UserSummary> users = await _Services.Summaries.BuildAsync(items.Select(i => i.AuthorUserId).Concat(new[] { snapshot.ReporterUserId }), token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, SnapshotViewAction, TargetTypeEnum.MessageReportSnapshot.ToString(), snapshot.Id, "report " + report.Id, token).ConfigureAwait(false);
            return new MessageSnapshotView
            {
                Id = snapshot.Id,
                ReportId = report.Id,
                ConversationId = snapshot.ConversationId,
                ReportedMessageId = items.FirstOrDefault(i => i.IsReported)?.MessageId,
                Reporter = snapshot.ReporterUserId != null && users.TryGetValue(snapshot.ReporterUserId, out UserSummary? r) ? r : null,
                CapturedUtc = snapshot.CapturedUtc,
                Messages = items.Select(i => new MessageSnapshotItemView
                {
                    Id = i.Id,
                    MessageId = i.MessageId,
                    Seq = i.Seq,
                    Author = i.AuthorUserId != null && users.TryGetValue(i.AuthorUserId, out UserSummary? a) ? a : null,
                    Body = i.Body,
                    OriginalBody = i.OriginalBody,
                    State = i.State,
                    IsReported = i.IsReported,
                    AttachmentSummary = i.AttachmentSummary,
                    EditedUtc = i.EditedUtc,
                    CreatedUtc = i.CreatedUtc
                }).ToList()
            };
        }

        /// <summary>
        /// Remove (Visible to Removed) or Restore a reported message for both members, through the moderation extension.
        /// Remove resolves the message's open reports as ActionTaken. Audited.
        /// </summary>
        /// <param name="actor">Moderator.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="messageId">Message identifier.</param>
        /// <param name="remove">True to remove, false to restore.</param>
        /// <param name="request">Body (reason).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The snapshot item of the message as moderators see it.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound (no snapshot), 409 NotChanged.</exception>
        public async Task<object> ModerateAsync(ContentActor actor, RequestInfo info, string messageId, bool remove, MessagingModerationRequest? request, CancellationToken token = default)
        {
            RequireModerator(actor);
            ArgumentNullException.ThrowIfNull(info);
            ModerationTarget? target = await _Module.ModerationTarget.LoadAsync(TargetTypeEnum.Message, messageId, token).ConfigureAwait(false);
            if (target == null) throw ApiErrorException.NotFound("Message");
            Message? before = await _Services.Database.Messages.ReadAsync(target.TargetId, token).ConfigureAwait(false);
            MessageStateEnum expected = remove ? MessageStateEnum.Visible : MessageStateEnum.Removed;
            if (before == null || before.State != expected) throw new ApiErrorException(409, "NotChanged", remove ? "Only a visible message can be removed." : "Only a removed message can be restored.");
            string? reason = String.IsNullOrWhiteSpace(request?.Reason) ? null : request!.Reason!.Trim();
            await _Module.ModerationTarget.SetStateAsync(target, remove ? ModerationStateEnum.Removed : ModerationStateEnum.None, actor.UserId, reason, token).ConfigureAwait(false);
            if (remove) await _Services.Database.Reports.ResolveOpenAsync(TargetTypeEnum.Message, target.TargetId, ReportStatusEnum.ActionTaken, actor.UserId, reason, null, _Module.Clock(), null, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, remove ? "moderation.message.remove" : "moderation.message.restore", TargetTypeEnum.Message.ToString(), target.TargetId, reason, token).ConfigureAwait(false);
            return await _Module.ModerationTarget.ViewAsync(actor, target, token).ConfigureAwait(false);
        }

        /// <summary>
        /// RevokeMessaging (and its reversal): set a member's canSendDirectMessages. Audited (messaging.revoke,
        /// messaging.restore).
        /// </summary>
        /// <param name="actor">Moderator.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="userId">Member.</param>
        /// <param name="allowed">The new canSendDirectMessages value.</param>
        /// <param name="request">Body (reason).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>{ userId, canSendDirectMessages }.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task<MessagingFlagView> SetCanSendAsync(ContentActor actor, RequestInfo info, string userId, bool allowed, MessagingModerationRequest? request, CancellationToken token = default)
        {
            RequireModerator(actor);
            ArgumentNullException.ThrowIfNull(info);
            User? user = String.IsNullOrWhiteSpace(userId) ? null : await _Services.Database.Users.ReadAsync(userId.Trim(), token).ConfigureAwait(false);
            if (user == null || user.IsDeleted) throw ApiErrorException.NotFound("Member");
            if (!actor.IsSystemAdmin && (await _Services.Summaries.SystemAdminsAsync(new[] { user.Id }, token).ConfigureAwait(false)).Count > 0)
                throw ApiErrorException.Forbidden("Moderators cannot restrict a SystemAdmin.");
            if (user.CanSendDirectMessages != allowed)
            {
                user.CanSendDirectMessages = allowed;
                await _Services.Database.Users.UpdateAsync(user, token).ConfigureAwait(false);
                _Services.Tokens.InvalidateUser(user.Id);
            }
            string? reason = String.IsNullOrWhiteSpace(request?.Reason) ? null : request!.Reason!.Trim();
            await _Services.Audit.WriteAsync(info, allowed ? "messaging.restore" : "messaging.revoke", TargetTypeEnum.User.ToString(), user.Id, reason, token).ConfigureAwait(false);
            await _Module.PublishAsync(_Module.Event(RealtimeEventKinds.AccessChanged, null, new[] { user.Id }), token).ConfigureAwait(false);
            return new MessagingFlagView { UserId = user.Id, CanSendDirectMessages = allowed };
        }

        /// <summary>
        /// 403 Forbidden unless the caller is SystemAdmin or a Messaging moderator.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden.</exception>
        public void RequireModerator(ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (!actor.IsSystemAdmin && !actor.IsMessagingModerator) throw ApiErrorException.Forbidden("Direct message reports are handled by SystemAdmin and Messaging moderators.");
        }

        #endregion
    }
}
