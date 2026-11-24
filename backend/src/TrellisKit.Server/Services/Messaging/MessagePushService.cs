namespace TrellisKit.Server.Services.Messaging
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Integrations.Models;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// The push decision for a new direct message, per recipient (docs/PHASE5_CONTRACT.md section 4.9 and resolved
    /// decision 12): no push while the recipient has a foreground connection (the socket delivers), and otherwise a push
    /// when the Messages push preference is on, push is not paused, the time is inside the recipient's notification hours
    /// (unless the member exempted Messages), the conversation is not muted, and it is not a pending request. Pushes
    /// coalesce to one per conversation per messaging.pushCoalesceSeconds while unread and follow the recipient's push
    /// preview. No in-app notification row is created for DirectMessage. Push payloads are never stored.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class MessagePushService
    {
        #region Public-Members

        /// <summary>
        /// Longest message excerpt in a Full preview push. Default 100.
        /// </summary>
        public const int FullPreviewLength = 100;

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
        public MessagePushService(ApplicationServices services, MessagingModule module)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            _Module = module ?? throw new ArgumentNullException(nameof(module));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Decide and send the pushes of a new message to the other members of its conversation.
        /// </summary>
        /// <param name="entry">Conversation from the sender's side (before the send).</param>
        /// <param name="message">The new message.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Pushes sent (one per recipient device).</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<int> AfterSendAsync(ConversationEntry entry, Message message, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(entry);
            ArgumentNullException.ThrowIfNull(message);
            int sent = 0;
            foreach (ConversationMember recipient in entry.Others)
            {
                token.ThrowIfCancellationRequested();
                sent += await PushOneAsync(entry, recipient.UserId, message, token).ConfigureAwait(false);
            }
            return sent;
        }

        /// <summary>
        /// The push title and body of a message for a preview mode: SenderOnly (default) names the sender, Full adds a
        /// 100 character excerpt, Hidden says only "New message".
        /// </summary>
        /// <param name="preview">Preview mode.</param>
        /// <param name="senderName">Sender display name.</param>
        /// <param name="message">Message.</param>
        /// <returns>Title and body (the body may be null).</returns>
        public static PushMessage Render(PushPreviewEnum preview, string senderName, Message message)
        {
            ArgumentNullException.ThrowIfNull(message);
            string name = String.IsNullOrWhiteSpace(senderName) ? "Someone" : senderName.Trim();
            switch (preview)
            {
                case PushPreviewEnum.Full:
                    string excerpt = MessagingViewBuilder.Excerpt(message.Body, FullPreviewLength);
                    return new PushMessage { Title = name, Body = excerpt.Length > 0 ? excerpt : (message.AttachmentCount > 0 ? "Sent an attachment" : "Sent you a message") };
                case PushPreviewEnum.Hidden:
                    return new PushMessage { Title = "New message", Body = null };
                default:
                    return new PushMessage { Title = name, Body = name + " sent you a message" };
            }
        }

        #endregion

        #region Private-Methods

        private async Task<int> PushOneAsync(ConversationEntry senderEntry, string userId, Message message, CancellationToken token)
        {
            DateTime now = _Module.Clock();
            RealtimePresence presence = await _Module.PresenceProbe(userId, message.ConversationId, token).ConfigureAwait(false) ?? RealtimePresence.None;
            if (presence.HasForeground)
            {
                TrellisKitTelemetry.Record(MessagingTelemetry.DeliveryLatency, Math.Max(0, (now - message.CreatedUtc).TotalSeconds), "path", "socket");
                return 0;
            }
            if (!_Services.Settings.Push.Enabled || !_Services.Integrations.Push.IsConfigured) return 0;
            ConversationEntry? mine = await _Services.Database.Conversations.ReadEntryAsync(message.ConversationId, userId, token).ConfigureAwait(false);
            if (mine == null || mine.Mine.LeftUtc != null) return 0;
            if (mine.Mine.RequestState == ConversationRequestStateEnum.Pending || mine.Mine.RequestState == ConversationRequestStateEnum.Declined) return 0;
            if (MessagingViewBuilder.IsMuted(mine.Mine, now)) return 0;
            if (message.AuthorUserId != null && await _Services.Blocks.HasBlockedAsync(userId, message.AuthorUserId, token).ConfigureAwait(false)) return 0;
            if (!(await _Module.Policy.WithAccessAsync(new[] { userId }, token).ConfigureAwait(false)).Contains(userId)) return 0;
            List<NotificationPreference> preferences = await _Services.Database.NotificationPreferences.EnumerateAsync(new[] { userId }, token).ConfigureAwait(false);
            if (!NotificationPreferenceService.Effective(userId, preferences, NotificationCategoryEnum.Messages).Push) return 0;
            NotificationHours hours = (await _Services.Database.NotificationPreferences.EnumerateHoursAsync(new[] { userId }, token).ConfigureAwait(false)).FirstOrDefault()
                ?? NotificationPreferenceService.DefaultHours(_Services.Settings.Notifications, userId);
            if (hours.PushPaused) return 0;
            MemberMessagingSettings settings = await _Module.Policy.SettingsOfAsync(userId, token).ConfigureAwait(false);
            if (!settings.IgnoreNotificationHours && !NotificationHoursRule.IsWithin(hours, now)) return 0;
            List<PushToken> devices = await _Services.Database.PushTokens.EnumerateActiveByUsersAsync(new[] { userId }, now, token).ConfigureAwait(false);
            if (devices.Count == 0) return 0;
            if (!await _Services.Database.Conversations.TryClaimPushAsync(message.ConversationId, userId, message.Seq, now, _Services.Settings.Messaging.PushCoalesceSeconds, token).ConfigureAwait(false)) return 0;
            UserSummary sender = await _Services.Summaries.BuildOneAsync(message.AuthorUserId, token).ConfigureAwait(false);
            PushMessage template = Render(settings.PushPreview, sender.DisplayName, message);
            long notifications = await _Services.Database.Notifications.CountUnreadAsync(userId, token).ConfigureAwait(false);
            long conversations = (await _Services.Database.Conversations.UnreadSummaryAsync(userId, token).ConfigureAwait(false)).Conversations;
            int badge = (int)Math.Min(Int32.MaxValue, notifications + conversations);
            string seq = message.Seq.ToString(CultureInfo.InvariantCulture);
            List<PushMessage> pushes = devices.Select(d => new PushMessage
            {
                To = d.Token,
                Title = template.Title,
                Body = template.Body,
                Data = new Dictionary<string, string>
                {
                    { "type", NotificationTypeEnum.DirectMessage.ToString() },
                    { "conversationId", message.ConversationId },
                    { "seq", seq },
                    { "path", "/messages/" + message.ConversationId },
                    { "collapseId", message.ConversationId },
                    { "threadId", message.ConversationId }
                },
                Sound = "default",
                Badge = badge,
                ChannelId = "default",
                Priority = "high"
            }).ToList();
            try
            {
                await _Services.Integrations.Push.SendAsync(pushes, token).ConfigureAwait(false);
            }
            catch (HttpRequestException e)
            {
                _Services.Logger.Warn("[MessagePushService] push send failed: " + e.Message);
                TrellisKitTelemetry.Add(TrellisKitTelemetry.PushSends, pushes.Count, "outcome", "error");
                return 0;
            }
            TrellisKitTelemetry.Add(TrellisKitTelemetry.PushSends, pushes.Count, "outcome", "sent");
            TrellisKitTelemetry.Record(MessagingTelemetry.DeliveryLatency, Math.Max(0, (_Module.Clock() - message.CreatedUtc).TotalSeconds), "path", "push");
            return pushes.Count;
        }

        #endregion
    }
}
