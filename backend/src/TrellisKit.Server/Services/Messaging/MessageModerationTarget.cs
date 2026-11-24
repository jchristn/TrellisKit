namespace TrellisKit.Server.Services.Messaging
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services.Realtime;

    /// <summary>
    /// The moderation extension for direct messages (docs/PHASE5_CONTRACT.md sections 3.9 and 11): targets load from
    /// report snapshots (never from the conversation), Remove sets the live message Removed for both members and Restore
    /// makes it Visible again, each with a message.updated event. Auto-hide, revisions, and purges do not apply. The
    /// coordinator registers this instance with the moderation service (MessagingModule.ModerationTarget).
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class MessageModerationTarget : IModerationTargetExtension
    {
        #region Public-Members

        /// <inheritdoc />
        public string? ScopeType
        {
            get { return MessageReportService.Scope; }
        }

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;
        private readonly MessagingModule _Module;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the extension.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <param name="module">Messaging module.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public MessageModerationTarget(ApplicationServices services, MessagingModule module)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            _Module = module ?? throw new ArgumentNullException(nameof(module));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public IReadOnlyList<string> ScopesFor(ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            return actor.IsMessagingModerator ? new[] { MessageReportService.Scope } : Array.Empty<string>();
        }

        /// <inheritdoc />
        public bool? CanAct(ContentActor actor, ModerationTarget target, bool authorIsSystemAdmin)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(target);
            if (actor.IsSystemAdmin) return true;
            return actor.IsMessagingModerator && !authorIsSystemAdmin;
        }

        /// <inheritdoc />
        public bool Handles(TargetTypeEnum targetType)
        {
            return targetType == TargetTypeEnum.Message || targetType == TargetTypeEnum.Conversation;
        }

        /// <inheritdoc />
        public async Task<ModerationTarget?> LoadAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(targetId)) return null;
            string id = targetId.Trim();
            if (targetType == TargetTypeEnum.Message)
            {
                MessageReportSnapshotItem? item = await _Services.Database.Messages.LatestSnapshotItemAsync(id, token).ConfigureAwait(false);
                if (item == null) return null;
                return new ModerationTarget { TargetType = TargetTypeEnum.Message, TargetId = item.MessageId, AuthorUserId = item.AuthorUserId ?? String.Empty, ScopeType = MessageReportService.Scope, ScopeId = "*", SkipAutoHide = true };
            }
            if (targetType == TargetTypeEnum.Conversation)
            {
                Conversation? conversation = await _Services.Database.Conversations.ReadAsync(id, token).ConfigureAwait(false);
                if (conversation == null) return null;
                return new ModerationTarget { TargetType = TargetTypeEnum.Conversation, TargetId = conversation.Id, AuthorUserId = String.Empty, ScopeType = MessageReportService.Scope, ScopeId = "*", SkipAutoHide = true };
            }
            return null;
        }

        /// <inheritdoc />
        public async Task SetStateAsync(ModerationTarget target, ModerationStateEnum state, string? moderatorUserId, string? reason, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(target);
            if (target.TargetType != TargetTypeEnum.Message) return;
            bool changed = await _Services.Database.Messages.SetRemovedAsync(target.TargetId, state != ModerationStateEnum.None, moderatorUserId, _Module.Clock(), token).ConfigureAwait(false);
            if (!changed) return;
            Message? message = await _Services.Database.Messages.ReadAsync(target.TargetId, token).ConfigureAwait(false);
            if (message == null) return;
            List<ConversationMember> members = await _Services.Database.Conversations.ReadMembersAsync(message.ConversationId, token).ConfigureAwait(false);
            RealtimeEvent evt = _Module.Event(RealtimeEventKinds.MessageUpdated, message.ConversationId, members.ConvertAll(m => m.UserId));
            evt.MessageId = message.Id;
            evt.Seq = message.Seq;
            await _Module.PublishAsync(evt, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<object> ViewAsync(ContentActor actor, ModerationTarget target, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(target);
            MessageReportSnapshotItem? item = target.TargetType == TargetTypeEnum.Message ? await _Services.Database.Messages.LatestSnapshotItemAsync(target.TargetId, token).ConfigureAwait(false) : null;
            Message? live = target.TargetType == TargetTypeEnum.Message ? await _Services.Database.Messages.ReadAsync(target.TargetId, token).ConfigureAwait(false) : null;
            UserSummary? author = item?.AuthorUserId == null ? null : await _Services.Summaries.BuildOneAsync(item.AuthorUserId, token).ConfigureAwait(false);
            return new MessageModerationView
            {
                TargetType = target.TargetType,
                TargetId = target.TargetId,
                Author = author,
                SnapshotBody = item?.Body ?? String.Empty,
                SnapshotState = item?.State ?? MessageStateEnum.Visible,
                CurrentState = live?.State,
                CapturedSeq = item?.Seq
            };
        }

        /// <inheritdoc />
        public Task<BoundedPreview<object>> RevisionsAsync(ModerationTarget target, CancellationToken token = default)
        {
            return Task.FromResult(new BoundedPreview<object>());
        }

        /// <inheritdoc />
        public Task RecomputeReportsAsync(ModerationTarget target, CancellationToken token = default)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<bool> PurgeAsync(string targetId, DateTime nowUtc, CancellationToken token = default)
        {
            return Task.FromResult(false);
        }

        /// <inheritdoc />
        public Task<string?> ForumOfCommentAsync(ModerationTarget target, CancellationToken token = default)
        {
            return Task.FromResult<string?>(null);
        }

        /// <inheritdoc />
        public Task<string?> ForumNameAsync(string forumId, CancellationToken token = default)
        {
            return Task.FromResult<string?>(null);
        }

        /// <inheritdoc />
        public Task<List<string>> ModeratorIdsAsync(string forumId, CancellationToken token = default)
        {
            return Task.FromResult(new List<string>());
        }

        #endregion
    }
}
