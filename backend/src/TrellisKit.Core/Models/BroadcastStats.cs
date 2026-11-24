namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// Delivery counters of a broadcast (docs/PHASE5_CONTRACT.md section 2.5), recomputed from the delivery records, the
    /// push states and receipts of the in-app notifications, and the email message statuses (which the Mailgun webhooks
    /// update). No open or click tracking (resolved decision 22).
    /// </summary>
    public class BroadcastStats
    {
        #region Public-Members

        /// <summary>
        /// Recipients with a delivery record.
        /// </summary>
        public int Targeted { get; set; } = 0;

        /// <summary>
        /// In-app notifications created.
        /// </summary>
        public int InAppCreated { get; set; } = 0;

        /// <summary>
        /// Pushes accepted by the push service.
        /// </summary>
        public int PushSent { get; set; } = 0;

        /// <summary>
        /// Pushes that failed (send errors or error receipts on every device).
        /// </summary>
        public int PushFailed { get; set; } = 0;

        /// <summary>
        /// Emails handed to the administrative email queue.
        /// </summary>
        public int EmailQueued { get; set; } = 0;

        /// <summary>
        /// Emails accepted by the provider (including delivered ones).
        /// </summary>
        public int EmailSent { get; set; } = 0;

        /// <summary>
        /// Emails the provider reported delivered.
        /// </summary>
        public int EmailDelivered { get; set; } = 0;

        /// <summary>
        /// Emails that failed permanently.
        /// </summary>
        public int EmailFailed { get; set; } = 0;

        /// <summary>
        /// Emails not sent because the address is suppressed.
        /// </summary>
        public int EmailSuppressed { get; set; } = 0;

        /// <summary>
        /// Recipients with at least one channel skipped by their Announcements preferences.
        /// </summary>
        public int SkippedPreferences { get; set; } = 0;

        #endregion
    }
}
