namespace TrellisKit.Server.Services.Realtime
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The capabilities a realtime connection can hold (docs/PHASE5_CONTRACT.md sections 4.2 and 4.5). notifications and
    /// tickets are granted to every signed-in session; messaging only where the messaging feature and the access model
    /// allow it (section 0.3 and resolved decision 26).
    /// Thread safety: constants only.
    /// </summary>
    public static class RealtimeCapabilities
    {
        #region Public-Members

        /// <summary>
        /// Direct message events (messages, receipts, typing, conversations).
        /// </summary>
        public const string Messaging = "messaging";

        /// <summary>
        /// Notification events (notification.created, unread.counts).
        /// </summary>
        public const string Notifications = "notifications";

        /// <summary>
        /// Support ticket hints (ticket.updated).
        /// </summary>
        public const string Tickets = "tickets";

        /// <summary>
        /// Every capability, in welcome order.
        /// </summary>
        public static readonly IReadOnlyList<string> All = new List<string> { Messaging, Notifications, Tickets };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a value names a known capability.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>True when known.</returns>
        public static bool IsKnown(string? value)
        {
            return value != null && (String.Equals(value, Messaging, StringComparison.Ordinal) || String.Equals(value, Notifications, StringComparison.Ordinal) || String.Equals(value, Tickets, StringComparison.Ordinal));
        }

        #endregion
    }
}
