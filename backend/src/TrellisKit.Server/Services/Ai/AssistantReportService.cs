namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;
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
    /// Reports and moderation snapshots of assistant messages (docs/AI_CONTRACT.md sections 12.1 to 12.3): members report
    /// a reply in their own conversation through POST /api/v1/reports (targetType AssistantMessage, scope Assistant with
    /// the assistant id, handled by SystemAdmin); the server files system reports when output moderation withholds a
    /// reply or a member message is flagged. Each reported or withheld message gets one snapshot of the conversation up
    /// to and including it, kept for moderation.retainRemovedDays even after the member deletes the conversation.
    /// Thread safety: safe to call concurrently (snapshot creation is idempotent per message).
    /// </summary>
    public class AssistantReportService
    {
        #region Public-Members

        /// <summary>
        /// The moderation scope type of assistant message reports.
        /// </summary>
        public const string Scope = "Assistant";

        /// <summary>
        /// Snapshot reason of a member report.
        /// </summary>
        public const string ReasonReport = "Report";

        /// <summary>
        /// Snapshot reason of a reply withheld by output moderation.
        /// </summary>
        public const string ReasonOutputBlocked = "OutputBlocked";

        /// <summary>
        /// Snapshot reason of a reply flagged by output moderation (kept, not withheld).
        /// </summary>
        public const string ReasonOutputFlagged = "OutputFlagged";

        /// <summary>
        /// Snapshot reason of a member message flagged by the word filter or the moderation model.
        /// </summary>
        public const string ReasonInputFlagged = "InputFlagged";

        /// <summary>
        /// Most messages copied into one snapshot (the largest limits.maxMessagesPerConversation).
        /// </summary>
        public const int MaxSnapshotMessages = 1000;

        #endregion

        #region Private-Members

        private const int _PageSize = 200;
        private readonly AiServices _Ai;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="ai">AI services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public AssistantReportService(AiServices ai)
        {
            _Ai = ai ?? throw new ArgumentNullException(nameof(ai));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolve the target of a member report: an assistant reply in a conversation the caller owns.
        /// </summary>
        /// <param name="actor">Reporter (a member session).</param>
        /// <param name="messageId">Message identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Target with AssistantId set and an empty author.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">404 NotAvailable (AI off), 404 NotFound (not the caller's reply).</exception>
        public async Task<ModerationTarget> RequireReportableAsync(ContentActor actor, string messageId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Ai.EnsureAvailable();
            string id = (messageId ?? String.Empty).Trim();
            AssistantMessage? message = id.StartsWith(AiConstants.MessageIdPrefix, StringComparison.Ordinal) ? await _Ai.App.Database.Ai.Conversations.ReadMessageAsync(id, token).ConfigureAwait(false) : null;
            if (message == null || message.Role != AssistantMessageRoleEnum.Assistant) throw ApiErrorException.NotFound("Message");
            AssistantConversation? conversation = await _Ai.App.Database.Ai.Conversations.ReadConversationAsync(message.ConversationId, token).ConfigureAwait(false);
            if (conversation == null || actor.UserId == null || !String.Equals(conversation.UserId, actor.UserId, StringComparison.Ordinal)) throw ApiErrorException.NotFound("Message");
            return new ModerationTarget { TargetType = TargetTypeEnum.AssistantMessage, TargetId = message.Id, AuthorUserId = String.Empty, AssistantId = message.AssistantId };
        }

        /// <summary>
        /// Load a moderation target for the queue: the live message, or the snapshot when the conversation is gone.
        /// </summary>
        /// <param name="messageId">Message identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Target.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<ModerationTarget> LoadTargetAsync(string messageId, CancellationToken token = default)
        {
            string id = (messageId ?? String.Empty).Trim();
            if (id.Length == 0) throw ApiErrorException.NotFound("Message");
            AssistantMessage? message = await _Ai.App.Database.Ai.Conversations.ReadMessageAsync(id, token).ConfigureAwait(false);
            if (message != null) return new ModerationTarget { TargetType = TargetTypeEnum.AssistantMessage, TargetId = message.Id, AuthorUserId = String.Empty, AssistantId = message.AssistantId };
            AiModerationSnapshot? snapshot = await _Ai.App.Database.Ai.Moderation.ReadByMessageAsync(id, token).ConfigureAwait(false);
            if (snapshot == null) throw ApiErrorException.NotFound("Message");
            return new ModerationTarget { TargetType = TargetTypeEnum.AssistantMessage, TargetId = snapshot.MessageId, AuthorUserId = String.Empty, AssistantId = snapshot.AssistantId };
        }

        /// <summary>
        /// Make the snapshot of a message (idempotent: an existing snapshot is kept).
        /// </summary>
        /// <param name="messageId">Message identifier.</param>
        /// <param name="reason">Report, OutputBlocked, OutputFlagged, or InputFlagged.</param>
        /// <param name="reportId">Report identifier, or null.</param>
        /// <param name="withheldContent">The text output moderation replaced, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a snapshot was stored now.</returns>
        public async Task<bool> SnapshotAsync(string messageId, string reason, string? reportId, string? withheldContent, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(messageId)) return false;
            if (await _Ai.App.Database.Ai.Moderation.ReadByMessageAsync(messageId, token).ConfigureAwait(false) != null) return false;
            AssistantMessage? target = await _Ai.App.Database.Ai.Conversations.ReadMessageAsync(messageId, token).ConfigureAwait(false);
            if (target == null) return false;
            AssistantConversation? conversation = await _Ai.App.Database.Ai.Conversations.ReadConversationAsync(target.ConversationId, token).ConfigureAwait(false);
            if (conversation == null) return false;
            List<AiModerationSnapshotMessage> messages = new List<AiModerationSnapshotMessage>();
            string snapshotId = IdGenerator.Generate(AiConstants.ModerationSnapshotIdPrefix);
            string? after = null;
            bool reached = false;
            while (!reached && messages.Count < MaxSnapshotMessages)
            {
                token.ThrowIfCancellationRequested();
                List<AssistantMessage> page = await _Ai.App.Database.Ai.Conversations.ListMessagesAfterAsync(conversation.Id, after, _PageSize, token).ConfigureAwait(false);
                foreach (AssistantMessage m in page)
                {
                    bool isTarget = String.Equals(m.Id, target.Id, StringComparison.Ordinal);
                    messages.Add(new AiModerationSnapshotMessage
                    {
                        SnapshotId = snapshotId,
                        Position = messages.Count,
                        MessageId = m.Id,
                        Role = m.Role,
                        Content = isTarget && withheldContent != null ? withheldContent : m.Content,
                        Status = m.Status,
                        CreatedUtc = m.CreatedUtc
                    });
                    if (isTarget || messages.Count >= MaxSnapshotMessages)
                    {
                        reached = true;
                        break;
                    }
                }
                if (page.Count < _PageSize) break;
                after = page[page.Count - 1].Id;
            }
            // Keep the newest messages when the conversation is longer than the bound, so the copy ends at the target.
            if (!messages.Any(m => String.Equals(m.MessageId, target.Id, StringComparison.Ordinal)))
            {
                messages.Add(new AiModerationSnapshotMessage { SnapshotId = snapshotId, Position = messages.Count, MessageId = target.Id, Role = target.Role, Content = withheldContent ?? target.Content, Status = target.Status, CreatedUtc = target.CreatedUtc });
                if (messages.Count > MaxSnapshotMessages) messages = messages.Skip(messages.Count - MaxSnapshotMessages).ToList();
                for (int i = 0; i < messages.Count; i++) messages[i].Position = i;
            }
            DateTime now = DateTime.UtcNow;
            AiModerationSnapshot snapshot = new AiModerationSnapshot
            {
                Id = snapshotId,
                ReportId = reportId,
                MessageId = target.Id,
                ConversationId = conversation.Id,
                AssistantId = target.AssistantId,
                OwnerUserId = conversation.UserId,
                Reason = reason,
                EndpointId = target.EndpointId,
                EndpointName = target.EndpointName,
                Model = target.Model,
                Citations = target.Citations.Take(100).ToList(),
                ToolCalls = target.ToolCalls.Take(AiConstants.MaxToolCallsPerReply).ToList(),
                CreatedUtc = now,
                ExpiresUtc = now.AddDays(_Ai.App.Settings.Moderation.RetainRemovedDays)
            };
            bool stored = await _Ai.App.Database.Ai.Moderation.CreateSnapshotAsync(snapshot, messages, token).ConfigureAwait(false);
            if (stored) TrellisKitTelemetry.Add(AiTelemetry.Moderation, 1, "stage", "snapshot", "verdict", AiTelemetry.Label(reason), "source", "report");
            return stored;
        }

        /// <summary>
        /// File a system report on an assistant message (scope Assistant, reason Filter, no reporter) unless an Open one
        /// exists, and snapshot the conversation. Never throws for storage problems (logged).
        /// </summary>
        /// <param name="message">The stored message.</param>
        /// <param name="authorUserId">The member who wrote it (member messages), or null for replies.</param>
        /// <param name="details">Details for moderators.</param>
        /// <param name="reason">Snapshot reason (OutputBlocked, OutputFlagged, or InputFlagged).</param>
        /// <param name="withheldContent">The text output moderation replaced, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a new report was filed.</returns>
        /// <exception cref="ArgumentNullException">The message is null.</exception>
        public async Task<bool> FileSystemReportAsync(AssistantMessage message, string? authorUserId, string details, string reason, string? withheldContent, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(message);
            try
            {
                DateTime now = DateTime.UtcNow;
                string reportId = IdGenerator.Generate(Constants.ReportIdPrefix);
                bool created = await _Ai.App.Database.WordFilter.CreateSystemReportAsync(reportId, TargetTypeEnum.AssistantMessage, message.Id, authorUserId, details, Scope, message.AssistantId, now,
                    now.AddHours(_Ai.App.Settings.Moderation.ReviewTargetHours), token).ConfigureAwait(false);
                if (created) TrellisKitTelemetry.Add(TrellisKitTelemetry.Reports, 1, "target_type", TargetTypeEnum.AssistantMessage.ToString(), "reason", ReportReasonEnum.Filter.ToString());
                await SnapshotAsync(message.Id, reason, created ? reportId : null, withheldContent, token).ConfigureAwait(false);
                return created;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _Ai.App.Logger.Warn("[AssistantReportService] system report for " + message.Id + " failed: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// The moderation queue target view of an assistant message: its snapshot, made now from the live conversation
        /// when none exists yet.
        /// </summary>
        /// <param name="messageId">Message identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<AssistantModerationSnapshotView> ViewAsync(string messageId, CancellationToken token = default)
        {
            AiModerationSnapshot? snapshot = await _Ai.App.Database.Ai.Moderation.ReadByMessageAsync(messageId, token).ConfigureAwait(false);
            if (snapshot == null)
            {
                await SnapshotAsync(messageId, ReasonReport, null, null, token).ConfigureAwait(false);
                snapshot = await _Ai.App.Database.Ai.Moderation.ReadByMessageAsync(messageId, token).ConfigureAwait(false);
            }
            if (snapshot == null) throw ApiErrorException.NotFound("Message");
            Assistant? assistant = await _Ai.App.Database.Ai.Assistants.ReadAsync(snapshot.AssistantId, token).ConfigureAwait(false);
            AssistantConversation? conversation = await _Ai.App.Database.Ai.Conversations.ReadConversationAsync(snapshot.ConversationId, token).ConfigureAwait(false);
            List<AiModerationSnapshotMessage> last = await _Ai.App.Database.Ai.Moderation.LastMessagesAsync(snapshot.Id, BoundedPreview<object>.MaxObjects, token).ConfigureAwait(false);
            AiModerationSnapshotMessage? reported = last.LastOrDefault(m => String.Equals(m.MessageId, snapshot.MessageId, StringComparison.Ordinal)) ?? last.LastOrDefault();
            return new AssistantModerationSnapshotView
            {
                Id = snapshot.Id,
                MessageId = snapshot.MessageId,
                ReportId = snapshot.ReportId,
                Reason = snapshot.Reason,
                ConversationId = snapshot.ConversationId,
                ConversationDeleted = conversation == null,
                Assistant = new AiAssistantRef { Id = snapshot.AssistantId, Name = assistant?.Name ?? String.Empty, Deleted = assistant == null || assistant.DeletedUtc != null },
                Owner = snapshot.OwnerUserId == null ? null : await _Ai.App.Summaries.BuildOneAsync(snapshot.OwnerUserId, token).ConfigureAwait(false),
                ReportedMessage = reported == null ? new AssistantSnapshotMessageView { Id = snapshot.MessageId, IsReported = true } : MessageView(reported, snapshot.MessageId),
                Model = new MessageModelView { EndpointId = snapshot.EndpointId, EndpointName = snapshot.EndpointName, Model = snapshot.Model },
                Citations = snapshot.Citations.Select(c => new CitationView { Index = c.Index, TargetType = c.TargetType, TargetId = c.TargetId, Title = c.Title, Path = c.Path }).ToList(),
                ToolCalls = ConversationService.ToolCallsView(new AssistantMessage { Id = snapshot.MessageId, ToolCalls = snapshot.ToolCalls }, true, _Ai.Tools),
                Messages = new BoundedPreview<AssistantSnapshotMessageView>
                {
                    Objects = last.Select(m => MessageView(m, snapshot.MessageId)).ToList(),
                    TotalRecords = snapshot.MessageCount,
                    Enumerate = SnapshotEnumeratePath(snapshot.MessageId)
                },
                CreatedUtc = snapshot.CreatedUtc,
                ExpiresUtc = snapshot.ExpiresUtc
            };
        }

        /// <summary>
        /// Enumerate the messages of a message's snapshot (SystemAdmin; the route checks the caller).
        /// </summary>
        /// <param name="messageId">Reported message identifier.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">404 NotFound, 400 ValidationFailed.</exception>
        public async Task<EnumerationResult<AssistantSnapshotMessageView>> EnumerateSnapshotAsync(string messageId, EnumerationQuery? query, CancellationToken token = default)
        {
            AiModerationSnapshot? snapshot = await _Ai.App.Database.Ai.Moderation.ReadByMessageAsync((messageId ?? String.Empty).Trim(), token).ConfigureAwait(false);
            if (snapshot == null) throw ApiErrorException.NotFound("Snapshot");
            EnumerationRequest request = _Ai.App.Enumeration.Prepare(query ?? new EnumerationQuery(), AiCollections.SnapshotMessages, true, "snapshot|" + snapshot.Id);
            EnumerationResult<AiModerationSnapshotMessage> page = await _Ai.App.Database.Ai.Moderation.EnumerateMessagesAsync(request, snapshot.Id, token).ConfigureAwait(false);
            return page.With(page.Objects.Select(m => MessageView(m, snapshot.MessageId)).ToList());
        }

        /// <summary>
        /// Delete expired snapshots (AiMaintenanceWorker).
        /// </summary>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Snapshots deleted.</returns>
        public Task<int> PruneAsync(DateTime nowUtc, CancellationToken token = default)
        {
            return _Ai.App.Database.Ai.Moderation.PruneAsync(nowUtc, 500, token);
        }

        /// <summary>
        /// The enumerate path of a message's snapshot messages.
        /// </summary>
        /// <param name="messageId">Reported message identifier.</param>
        /// <returns>Path.</returns>
        public static string SnapshotEnumeratePath(string messageId)
        {
            return Constants.ApiPrefix + "/admin/moderation/queue/AssistantMessage/" + Uri.EscapeDataString(messageId ?? String.Empty) + "/snapshot/enumerate";
        }

        #endregion

        #region Private-Methods

        private static AssistantSnapshotMessageView MessageView(AiModerationSnapshotMessage m, string reportedId)
        {
            return new AssistantSnapshotMessageView
            {
                Id = m.MessageId,
                Position = m.Position,
                Role = m.Role,
                Content = m.Content,
                Status = m.Status,
                IsReported = String.Equals(m.MessageId, reportedId, StringComparison.Ordinal),
                CreatedUtc = m.CreatedUtc
            };
        }

        #endregion
    }
}
