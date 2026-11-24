namespace TrellisKit.Server.Services.Realtime
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The realtime bus event kinds (docs/PHASE5_CONTRACT.md sections 4.2, 4.5, and 4.11). Most kinds match the socket
    /// frame type the dispatcher renders; session.revoked and access.changed act on connections.
    /// Thread safety: constants only.
    /// </summary>
    public static class RealtimeEventKinds
    {
        #region Public-Members

        /// <summary>
        /// A message was created (c, s, m, a, u).
        /// </summary>
        public const string MessageCreated = "message.created";

        /// <summary>
        /// A message was edited, reacted to, previewed, or removed by a moderator (c, s, m, u).
        /// </summary>
        public const string MessageUpdated = "message.updated";

        /// <summary>
        /// A message was deleted for everyone (c, s, m, u).
        /// </summary>
        public const string MessageDeleted = "message.deleted";

        /// <summary>
        /// A conversation was created for its members (c, u).
        /// </summary>
        public const string ConversationCreated = "conversation.created";

        /// <summary>
        /// A conversation changed for some of its members (c, u).
        /// </summary>
        public const string ConversationUpdated = "conversation.updated";

        /// <summary>
        /// A member's delivery position moved (c, s, a, u).
        /// </summary>
        public const string ReceiptDelivered = "receipt.delivered";

        /// <summary>
        /// A member's read position moved (c, s, a, u).
        /// </summary>
        public const string ReceiptRead = "receipt.read";

        /// <summary>
        /// A member is typing (c, a, d = Start or Stop, u).
        /// </summary>
        public const string Typing = "typing";

        /// <summary>
        /// A notification was created for a member (t = notification id, u).
        /// </summary>
        public const string NotificationCreated = "notification.created";

        /// <summary>
        /// A member's unread counts changed on another device (u).
        /// </summary>
        public const string UnreadCounts = "unread.counts";

        /// <summary>
        /// Staff replied to or changed the status of a member's ticket (t = ticket id, d = status, u).
        /// </summary>
        public const string TicketUpdated = "ticket.updated";

        /// <summary>
        /// A session was revoked (x = session id, or u alone for every session of the member).
        /// </summary>
        public const string SessionRevoked = "session.revoked";

        /// <summary>
        /// A member's access changed (membership, flags); connections re-evaluate capabilities (u).
        /// </summary>
        public const string AccessChanged = "access.changed";

        /// <summary>
        /// A kill switch changed for a feature carried on the socket (d = feature key; u empty means every member of the property).
        /// </summary>
        public const string FeatureDisabled = "feature.disabled";

        /// <summary>
        /// Every kind.
        /// </summary>
        public static readonly IReadOnlyList<string> All = new List<string>
        {
            MessageCreated, MessageUpdated, MessageDeleted, ConversationCreated, ConversationUpdated, ReceiptDelivered, ReceiptRead, Typing,
            NotificationCreated, UnreadCounts, TicketUpdated, SessionRevoked, AccessChanged, FeatureDisabled
        };

        #endregion
    }
}
