namespace TrellisKit.Server.Services.Messaging
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services.Realtime;

    /// <summary>
    /// The messaging side of the realtime protocol (docs/PHASE5_CONTRACT.md sections 4.4 to 4.7): renders bus events into
    /// per-recipient frames (receipt visibility, request rules, blocks, hidden ranges, access) and handles the inbound
    /// typing, ack, and read frames. The realtime gateway calls these methods; they are not wired to a socket here.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class MessagingRealtime
    {
        #region Public-Members

        /// <summary>
        /// How long a typing indicator lasts without a refresh. Default 6000 ms.
        /// </summary>
        public const int TypingExpiresInMs = 6000;

        /// <summary>
        /// Minimum interval between delivery position writes per conversation and member. Default 1 second, minimum 0,
        /// maximum 60 seconds.
        /// </summary>
        public TimeSpan AckWriteInterval
        {
            get { return _AckWriteInterval; }
            set { _AckWriteInterval = TimeSpan.FromMilliseconds(Math.Clamp(value.TotalMilliseconds, 0, 60000)); }
        }

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;
        private readonly MessagingModule _Module;
        private readonly ConcurrentDictionary<string, DateTime> _AckWrites = new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, long> _AckPending = new ConcurrentDictionary<string, long>(StringComparer.Ordinal);
        private TimeSpan _AckWriteInterval = TimeSpan.FromSeconds(1);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the component.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <param name="module">Messaging module.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public MessagingRealtime(ApplicationServices services, MessagingModule module)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            _Module = module ?? throw new ArgumentNullException(nameof(module));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether this component renders an event kind.
        /// </summary>
        /// <param name="kind">Event kind.</param>
        /// <returns>True for the messaging kinds.</returns>
        public static bool Renders(string? kind)
        {
            switch (kind)
            {
                case RealtimeEventKinds.MessageCreated:
                case RealtimeEventKinds.MessageUpdated:
                case RealtimeEventKinds.MessageDeleted:
                case RealtimeEventKinds.ConversationCreated:
                case RealtimeEventKinds.ConversationUpdated:
                case RealtimeEventKinds.ReceiptRead:
                case RealtimeEventKinds.ReceiptDelivered:
                case RealtimeEventKinds.Typing:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Render a messaging event for one recipient, or null when the recipient must not receive it (not a member,
        /// deleted for them, hidden range, blocked, lost access, receipts or typing not allowed, or a request rule).
        /// </summary>
        /// <param name="evt">Bus event of this property.</param>
        /// <param name="recipientUserId">Recipient.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Frame, or null.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<RealtimeFrame?> RenderAsync(RealtimeEvent evt, string recipientUserId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(evt);
            if (String.IsNullOrEmpty(recipientUserId)) throw new ArgumentNullException(nameof(recipientUserId));
            if (!Renders(evt.Kind) || String.IsNullOrEmpty(evt.ConversationId)) return null;
            if (!String.Equals(evt.PropertyId, _Services.PropertyId, StringComparison.Ordinal)) return null;
            if (!_Services.Property.Property.Features.Messaging) return null;
            ConversationEntry? entry = await _Services.Database.Conversations.ReadEntryAsync(evt.ConversationId, recipientUserId, token).ConfigureAwait(false);
            if (entry == null || entry.Mine.LeftUtc != null) return null;
            if (!(await _Module.Policy.WithAccessAsync(new[] { recipientUserId }, token).ConfigureAwait(false)).Contains(recipientUserId)) return null;
            HashSet<string> blockedByRecipient = await _Services.Blocks.BlockedByAsync(recipientUserId, token).ConfigureAwait(false);
            bool recipientBlocked = entry.Others.Any(o => blockedByRecipient.Contains(o.UserId));
            ContentActor actor = await _Services.Actors.ForUserAsync(recipientUserId, token).ConfigureAwait(false);
            switch (evt.Kind)
            {
                case RealtimeEventKinds.MessageCreated:
                case RealtimeEventKinds.MessageUpdated:
                    {
                        if (entry.Mine.DeletedUtc != null || recipientBlocked || evt.MessageId == null) return null;
                        Message? message = await _Services.Database.Messages.ReadAsync(evt.MessageId, token).ConfigureAwait(false);
                        if (message == null || message.ConversationId != entry.Conversation.Id || message.Seq <= entry.Mine.HiddenBeforeSeq) return null;
                        DirectMessageView view = await _Module.Messages.ViewAsync(actor, entry, message, token).ConfigureAwait(false);
                        return new RealtimeFrame { Type = evt.Kind, Data = new MessageFrameData { ConversationId = entry.Conversation.Id, Message = view } };
                    }
                case RealtimeEventKinds.MessageDeleted:
                    {
                        if (entry.Mine.DeletedUtc != null || recipientBlocked || evt.MessageId == null || (evt.Seq ?? 0) <= entry.Mine.HiddenBeforeSeq) return null;
                        return new RealtimeFrame { Type = evt.Kind, Data = new MessageDeletedFrameData { ConversationId = entry.Conversation.Id, MessageId = evt.MessageId, Seq = evt.Seq ?? 0 } };
                    }
                case RealtimeEventKinds.ConversationCreated:
                case RealtimeEventKinds.ConversationUpdated:
                    {
                        if (entry.Mine.DeletedUtc != null && evt.Kind == RealtimeEventKinds.ConversationCreated) return null;
                        if (evt.Kind == RealtimeEventKinds.ConversationCreated && entry.Conversation.LastSeq <= entry.Mine.HiddenBeforeSeq && entry.Conversation.CreatedByUserId != recipientUserId) return null;
                        DirectConversationView view = await _Module.Views.ConversationAsync(actor, entry, token).ConfigureAwait(false);
                        return new RealtimeFrame { Type = evt.Kind, Data = new ConversationFrameData { Conversation = view } };
                    }
                case RealtimeEventKinds.ReceiptRead:
                case RealtimeEventKinds.ReceiptDelivered:
                    {
                        if (evt.ActorUserId == null || evt.Seq == null) return null;
                        bool own = String.Equals(evt.ActorUserId, recipientUserId, StringComparison.Ordinal);
                        if (own)
                        {
                            // The member's own devices always get their own read position to sync unread state.
                            if (evt.Kind != RealtimeEventKinds.ReceiptRead) return null;
                        }
                        else if (!await ReceiptsAllowedAsync(entry, evt.ActorUserId, recipientBlocked, token).ConfigureAwait(false)) return null;
                        return new RealtimeFrame { Type = evt.Kind, Data = new ReceiptFrameData { ConversationId = entry.Conversation.Id, UserId = evt.ActorUserId, Seq = evt.Seq.Value } };
                    }
                case RealtimeEventKinds.Typing:
                    {
                        if (evt.ActorUserId == null || String.Equals(evt.ActorUserId, recipientUserId, StringComparison.Ordinal)) return null;
                        if (entry.Mine.DeletedUtc != null || !await TypingAllowedAsync(entry, evt.ActorUserId, recipientBlocked, token).ConfigureAwait(false)) return null;
                        return new RealtimeFrame
                        {
                            Type = RealtimeEventKinds.Typing,
                            Data = new TypingFrameData { ConversationId = entry.Conversation.Id, UserId = evt.ActorUserId, State = evt.State == "Stop" ? "Stop" : "Start", ExpiresInMs = TypingExpiresInMs }
                        };
                    }
                default:
                    return null;
            }
        }

        /// <summary>
        /// Inbound typing frame { conversationId, state: Start or Stop }: never stored; relayed (as a Typing bus event to the
        /// other member) only when both members allow typing indicators, the conversation is not a pending request, and
        /// there is no block.
        /// </summary>
        /// <param name="userId">Typing member (the connection's member).</param>
        /// <param name="data">Frame data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when relayed.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed (bad frame), 404 NotFound (not a member).</exception>
        public async Task<bool> HandleTypingAsync(string userId, JsonElement data, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            string conversationId = RequireString(data, "conversationId");
            string state = RequireString(data, "state");
            if (state != "Start" && state != "Stop") throw ApiErrorException.Field("state", "Use Start or Stop.");
            ConversationEntry entry = await _Module.Conversations.RequireEntryAsync(userId, conversationId, token).ConfigureAwait(false);
            HashSet<string> blockedByMe = await _Services.Blocks.BlockedByAsync(userId, token).ConfigureAwait(false);
            if (!await TypingAllowedAsync(entry, userId, entry.Others.Any(o => blockedByMe.Contains(o.UserId)), token).ConfigureAwait(false)) return false;
            RealtimeEvent evt = _Module.Event(RealtimeEventKinds.Typing, entry.Conversation.Id, entry.Others.Select(o => o.UserId));
            evt.ActorUserId = userId;
            evt.State = state;
            await _Module.PublishAsync(evt, token).ConfigureAwait(false);
            return true;
        }

        /// <summary>
        /// Inbound ack frame { conversationId, seq }: moves the delivery position (monotonic), writing at most once per
        /// <see cref="AckWriteInterval"/> per conversation and member (later acks inside the interval are flushed once it
        /// passes).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="data">Frame data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when written now; false when batched or unchanged.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotFound.</exception>
        public async Task<bool> HandleAckAsync(string userId, JsonElement data, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            string conversationId = RequireString(data, "conversationId");
            long seq = RequireSeq(data);
            ConversationEntry entry = await _Module.Conversations.RequireEntryAsync(userId, conversationId, token).ConfigureAwait(false);
            string key = entry.Conversation.Id + "|" + userId;
            DateTime now = DateTime.UtcNow;
            if (_AckWrites.TryGetValue(key, out DateTime last) && now - last < _AckWriteInterval)
            {
                long pending = _AckPending.AddOrUpdate(key, seq, (k, existing) => Math.Max(existing, seq));
                if (pending == seq)
                {
                    TimeSpan wait = _AckWriteInterval - (now - last);
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(wait < TimeSpan.Zero ? TimeSpan.Zero : wait, CancellationToken.None).ConfigureAwait(false);
                            if (_AckPending.TryRemove(key, out long value))
                            {
                                _AckWrites[key] = DateTime.UtcNow;
                                await _Module.Conversations.DeliveredCoreAsync(entry, value, CancellationToken.None).ConfigureAwait(false);
                            }
                        }
                        catch (Exception e) when (e is not OutOfMemoryException)
                        {
                            _Services.Logger.Warn("[MessagingRealtime] batched ack failed: " + e.GetType().Name);
                        }
                    }, CancellationToken.None);
                }
                return false;
            }
            _AckWrites[key] = now;
            return await _Module.Conversations.DeliveredCoreAsync(entry, seq, token).ConfigureAwait(false) != null;
        }

        /// <summary>
        /// Inbound read frame { conversationId, seq }: same as POST /api/v1/conversations/{id}/read.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="data">Frame data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The read position afterwards.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotFound.</exception>
        public async Task<long> HandleReadAsync(string userId, JsonElement data, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            string conversationId = RequireString(data, "conversationId");
            long seq = RequireSeq(data);
            ConversationEntry entry = await _Module.Conversations.RequireEntryAsync(userId, conversationId, token).ConfigureAwait(false);
            return await _Module.Conversations.ReadCoreAsync(entry, seq, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task<bool> ReceiptsAllowedAsync(ConversationEntry entry, string otherUserId, bool recipientBlocked, CancellationToken token)
        {
            if (recipientBlocked || MessagingViewBuilder.IsRequest(entry)) return false;
            if (await _Services.Blocks.IsEitherBlockedAsync(entry.Mine.UserId, otherUserId, token).ConfigureAwait(false)) return false;
            Dictionary<string, MemberMessagingSettings> settings = await _Services.Database.Conversations.ReadSettingsAsync(new[] { entry.Mine.UserId, otherUserId }, token).ConfigureAwait(false);
            bool mine = !settings.TryGetValue(entry.Mine.UserId, out MemberMessagingSettings? m) || m.ReadReceipts;
            bool theirs = !settings.TryGetValue(otherUserId, out MemberMessagingSettings? t) || t.ReadReceipts;
            return mine && theirs;
        }

        private async Task<bool> TypingAllowedAsync(ConversationEntry entry, string otherUserId, bool blocked, CancellationToken token)
        {
            if (blocked || MessagingViewBuilder.IsRequest(entry)) return false;
            string peer = String.Equals(otherUserId, entry.Mine.UserId, StringComparison.Ordinal) ? (entry.Others.FirstOrDefault()?.UserId ?? String.Empty) : otherUserId;
            if (peer.Length == 0) return false;
            if (await _Services.Blocks.IsEitherBlockedAsync(entry.Mine.UserId, peer, token).ConfigureAwait(false)) return false;
            Dictionary<string, MemberMessagingSettings> settings = await _Services.Database.Conversations.ReadSettingsAsync(new[] { entry.Mine.UserId, peer }, token).ConfigureAwait(false);
            bool mine = !settings.TryGetValue(entry.Mine.UserId, out MemberMessagingSettings? m) || m.TypingIndicators;
            bool theirs = !settings.TryGetValue(peer, out MemberMessagingSettings? t) || t.TypingIndicators;
            return mine && theirs;
        }

        private static string RequireString(JsonElement data, string name)
        {
            if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String || String.IsNullOrWhiteSpace(value.GetString()))
                throw ApiErrorException.Field(name, "Required.");
            return value.GetString()!.Trim();
        }

        private static long RequireSeq(JsonElement data)
        {
            if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("seq", out JsonElement value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out long seq) || seq < 0)
                throw ApiErrorException.Field("seq", "Required: a sequence number of 0 or more.");
            return seq;
        }

        #endregion
    }
}
