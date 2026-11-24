namespace TrellisKit.Server.Services.Messaging
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services.Realtime;

    /// <summary>
    /// The Phase 5 direct messaging services of one property (docs/PHASE5_CONTRACT.md sections 3, 4.6, 4.7, 4.9, 9 to 15,
    /// and as-built note B) and their wiring into the shared services (reactions, reports, blocks, account lifecycle,
    /// exports). Seams for the coordinator: <see cref="PresenceProbe"/> (realtime connection registry),
    /// <see cref="ModerationTarget"/> (register with the moderation service), <see cref="Realtime"/> (frame rendering
    /// and inbound frames for the gateway), and <see cref="MessageAttachmentViews.BuildAsync"/> (private media URLs).
    /// Thread safety: safe to call concurrently after <see cref="Attach"/>.
    /// </summary>
    public class MessagingModule
    {
        #region Public-Members

        /// <summary>
        /// Access, eligibility, and viewer flags.
        /// </summary>
        public MessagingPolicyService Policy { get; }

        /// <summary>
        /// Payload builders.
        /// </summary>
        public MessagingViewBuilder Views { get; }

        /// <summary>
        /// Attachment payloads (the one place that builds direct message media URLs).
        /// </summary>
        public MessageAttachmentViews Attachments { get; }

        /// <summary>
        /// Conversations, requests, positions, sync, unread counts, and settings.
        /// </summary>
        public ConversationService Conversations { get; }

        /// <summary>
        /// Messages, history, search, and reactions.
        /// </summary>
        public MessageService Messages { get; }

        /// <summary>
        /// The push decision.
        /// </summary>
        public MessagePushService Push { get; }

        /// <summary>
        /// Reports, snapshots, and the Messaging moderation actions.
        /// </summary>
        public MessageReportService Reports { get; }

        /// <summary>
        /// The moderation extension for Message and Conversation targets.
        /// </summary>
        public MessageModerationTarget ModerationTarget { get; }

        /// <summary>
        /// Realtime frame rendering and inbound frame handlers.
        /// </summary>
        public MessagingRealtime Realtime { get; }

        /// <summary>
        /// Worker passes.
        /// </summary>
        public MessagingMaintenance Maintenance { get; }

        /// <summary>
        /// Clock (UTC) used for message times, windows, and worker passes. Default DateTime.UtcNow; tests set a fixed clock.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public Func<DateTime> Clock
        {
            get { return _Clock; }
            set { _Clock = value ?? throw new ArgumentNullException(nameof(Clock)); }
        }

        /// <summary>
        /// Whether a member has a live connection (foreground, conversation open), for the push decision. Arguments: user
        /// id, conversation id, token. Default: no connections. The coordinator wires it to the realtime connection registry.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public Func<string, string, CancellationToken, Task<RealtimePresence>> PresenceProbe
        {
            get { return _PresenceProbe; }
            set { _PresenceProbe = value ?? throw new ArgumentNullException(nameof(PresenceProbe)); }
        }

        /// <summary>
        /// Days after every member deleted a conversation before it is purged. Default 30, minimum 1, maximum 3650.
        /// </summary>
        public int PurgeAfterDays
        {
            get { return _PurgeAfterDays; }
            set { _PurgeAfterDays = Math.Clamp(value, 1, 3650); }
        }

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;
        private Func<DateTime> _Clock = () => DateTime.UtcNow;
        private Func<string, string, CancellationToken, Task<RealtimePresence>> _PresenceProbe = (userId, conversationId, token) => Task.FromResult(RealtimePresence.None);
        private int _PurgeAfterDays = 30;
        private int _Attached = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the module (call <see cref="Attach"/> once the shared services exist).
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public MessagingModule(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            Policy = new MessagingPolicyService(services);
            Views = new MessagingViewBuilder(services, this);
            Attachments = new MessageAttachmentViews(services);
            Conversations = new ConversationService(services, this);
            Messages = new MessageService(services, this);
            Push = new MessagePushService(services, this);
            Reports = new MessageReportService(services, this);
            ModerationTarget = new MessageModerationTarget(services, this);
            Realtime = new MessagingRealtime(services, this);
            Maintenance = new MessagingMaintenance(services, this);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Wire the module into the shared services (idempotent): message reactions, message and conversation reports,
        /// the block change hook (sync and lists), account deletion, and account export sections. The moderation
        /// extension is exposed on <see cref="ModerationTarget"/> for the coordinator to register.
        /// </summary>
        public void Attach()
        {
            if (Interlocked.Exchange(ref _Attached, 1) != 0) return;
            _Services.Reactions.ExtraTargets.Add(Messages.ReactionTargetAsync);
            _Services.Reports.ExtraTargets.Add(Reports.ReportTargetAsync);
            _Services.Reports.ExtraTargetTypes.Add(TargetTypeEnum.Message);
            _Services.Reports.ExtraTargetTypes.Add(TargetTypeEnum.Conversation);
            Func<string, string, CancellationToken, Task>? previousBlock = _Services.Blocks.Changed;
            _Services.Blocks.Changed = async (blockerUserId, blockedUserId, token) =>
            {
                await OnBlockChangedAsync(blockerUserId, blockedUserId, token).ConfigureAwait(false);
                if (previousBlock != null) await previousBlock(blockerUserId, blockedUserId, token).ConfigureAwait(false);
            };
            MessagingExportSections.Register(_Services);
            // Direct message media is private: per-object URLs work only for current members of the conversation.
            _Services.Media.RegisterPrivateScope(MediaPurposeEnum.Message, new PrivateMediaScope
            {
                CanAccess = CanAccessConversationMediaAsync,
                AllowVideo = () => _Services.Settings.Messaging.AllowMedia,
                RequirePostingPermissions = true
            });
            // Moderation of reported messages goes through the snapshot extension (sections 3.9 and 11).
            if (!_Services.Moderation.Extensions.Contains(ModerationTarget)) _Services.Moderation.Extensions.Add(ModerationTarget);
            AttachRealtime(_Services.Realtime);
            Func<string, CancellationToken, Task>? previous = _Services.Accounts.AccountClosed;
            _Services.Accounts.AccountClosed = async (userId, token) =>
            {
                await EraseForAccountAsync(userId, token).ConfigureAwait(false);
                if (previous != null) await previous(userId, token).ConfigureAwait(false);
            };
        }

        /// <summary>
        /// Account deletion (section 12): the member's messages become tombstones ("Deleted member", bodies and media
        /// purged) in every conversation, and their memberships, settings, contacts, and messaging state are removed.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Messages turned into tombstones.</returns>
        /// <exception cref="ArgumentNullException">The id is null or empty.</exception>
        public async Task<int> EraseForAccountAsync(string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            string? after = null;
            while (true)
            {
                List<string> withMedia = await _Services.Database.Messages.WithAttachmentsAsync(null, userId, after, 200, token).ConfigureAwait(false);
                if (withMedia.Count == 0) break;
                foreach (string messageId in withMedia) await _Services.Media.PurgeTargetAsync(TargetTypeEnum.Message, messageId, token).ConfigureAwait(false);
                after = withMedia[withMedia.Count - 1];
            }
            DateTime now = Clock();
            return await _Services.Database.Conversations.EraseForAccountAsync(userId, now, now.AddDays(PurgeAfterDays), token).ConfigureAwait(false);
        }

        /// <summary>
        /// A realtime event of this property (references only, never text).
        /// </summary>
        /// <param name="kind">Event kind (RealtimeEventKinds).</param>
        /// <param name="conversationId">Conversation, or null.</param>
        /// <param name="userIds">Recipients.</param>
        /// <returns>Event.</returns>
        public RealtimeEvent Event(string kind, string? conversationId, IEnumerable<string> userIds)
        {
            ArgumentNullException.ThrowIfNull(userIds);
            return new RealtimeEvent
            {
                PropertyId = _Services.PropertyId,
                Kind = kind,
                ConversationId = conversationId,
                UserIds = userIds.Where(u => !String.IsNullOrEmpty(u)).Distinct(StringComparer.Ordinal).ToList(),
                PublishedUtc = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Publish an event after a write committed. Never throws (the socket is best effort; REST is the source of truth).
        /// </summary>
        /// <param name="evt">Event.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task PublishAsync(RealtimeEvent evt, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(evt);
            try
            {
                await _Services.RealtimeBus.PublishAsync(evt, token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _Services.Logger.Warn("[MessagingModule] realtime publish of " + evt.Kind + " failed: " + e.GetType().Name);
            }
        }

        /// <summary>
        /// Plug the messaging module into the realtime seams (docs/PHASE5_CONTRACT.md section 4): renderers for every
        /// messaging event kind (capability messaging; typing and receipts droppable under backpressure), inbound handlers
        /// for typing, ack, and read frames, the unread.counts contributor, and the push decision's presence probe backed
        /// by the connection registry (a member connected to another task suppresses pushes from this one).
        /// </summary>
        /// <param name="realtime">Realtime module of this property.</param>
        /// <exception cref="ArgumentNullException">The module is null.</exception>
        public void AttachRealtime(RealtimeModule realtime)
        {
            ArgumentNullException.ThrowIfNull(realtime);
            string[] kinds = new[]
            {
                RealtimeEventKinds.MessageCreated, RealtimeEventKinds.MessageUpdated, RealtimeEventKinds.MessageDeleted, RealtimeEventKinds.ConversationCreated,
                RealtimeEventKinds.ConversationUpdated, RealtimeEventKinds.ReceiptRead, RealtimeEventKinds.ReceiptDelivered, RealtimeEventKinds.Typing
            };
            foreach (string kind in kinds)
            {
                bool droppable = kind == RealtimeEventKinds.Typing || kind == RealtimeEventKinds.ReceiptRead || kind == RealtimeEventKinds.ReceiptDelivered;
                realtime.Renderers.Register(kind, kind, RealtimeCapabilities.Messaging, droppable, async (context, token) =>
                {
                    RealtimeFrame? frame = await Realtime.RenderAsync(context.Event, context.RecipientUserId, token).ConfigureAwait(false);
                    return frame?.Data;
                });
            }
            realtime.Inbound.Register("typing", RealtimeCapabilities.Messaging, async (context, token) =>
            {
                // Unknown or foreign conversations are ignored silently, so the socket never reveals which ids exist.
                await Realtime.HandleTypingAsync(context.UserId, context.Data, token).ConfigureAwait(false);
            });
            realtime.Inbound.Register("ack", RealtimeCapabilities.Messaging, async (context, token) =>
            {
                // Unknown or foreign conversations are ignored silently, so the socket never reveals which ids exist.
                await Realtime.HandleAckAsync(context.UserId, context.Data, token).ConfigureAwait(false);
            });
            realtime.Inbound.Register("read", RealtimeCapabilities.Messaging, async (context, token) =>
            {
                await Realtime.HandleReadAsync(context.UserId, context.Data, token).ConfigureAwait(false);
            });
            realtime.UnreadCountContributors.Add(async (userId, counts, token) =>
            {
                MessagingUnreadCounts unread = await _Services.Database.Conversations.UnreadSummaryAsync(userId, token).ConfigureAwait(false);
                counts["conversations"] = unread.Conversations;
                counts["messages"] = unread.Messages;
                counts["requests"] = unread.Requests;
            });
            PresenceProbe = async (userId, conversationId, token) =>
            {
                RealtimeForeground foreground = await realtime.Registry.ForegroundAsync(userId, conversationId, token).ConfigureAwait(false);
                return new RealtimePresence { HasForeground = foreground.HasForeground, HasConversationActive = foreground.ConversationActive };
            };
        }

        #endregion

        #region Private-Methods

        private async Task<bool> CanAccessConversationMediaAsync(string viewerUserId, string conversationId, CancellationToken token)
        {
            if (String.IsNullOrEmpty(viewerUserId) || String.IsNullOrEmpty(conversationId)) return false;
            ConversationEntry? entry = await _Services.Database.Conversations.ReadEntryAsync(conversationId, viewerUserId, token).ConfigureAwait(false);
            if (entry == null || entry.Mine.LeftUtc != null) return false;
            // A pending request hides media from the recipient until they accept (section 3.2).
            return !(entry.Mine.RequestState == ConversationRequestStateEnum.Pending || entry.Mine.RequestState == ConversationRequestStateEnum.Declined);
        }

        private async Task OnBlockChangedAsync(string blockerUserId, string blockedUserId, CancellationToken token)
        {
            // Blocking hides the conversation from the blocker's lists and unblocking restores it: touch the blocker's rows
            // so the sync reports the change. The blocked member's rows are left alone (never told).
            int touched = await _Services.Database.Conversations.TouchSharedAsync(blockerUserId, blockedUserId, Clock(), token).ConfigureAwait(false);
            if (touched == 0) return;
            Conversation? conversation = await _Services.Database.Conversations.ReadByDirectKeyAsync(ConversationService.DirectKey(blockerUserId, blockedUserId), token).ConfigureAwait(false);
            if (conversation != null) await PublishAsync(Event(RealtimeEventKinds.ConversationUpdated, conversation.Id, new[] { blockerUserId }), token).ConfigureAwait(false);
        }

        #endregion
    }
}
