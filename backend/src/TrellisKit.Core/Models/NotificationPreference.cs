namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A row of notificationpreferences: one member's channel choices for one category. Rows exist only for categories
    /// the member changed; defaults come from code.
    /// </summary>
    public class NotificationPreference
    {
        /// <summary>
        /// Member identifier.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Category.
        /// </summary>
        public NotificationCategoryEnum Category { get; set; } = NotificationCategoryEnum.Replies;

        /// <summary>
        /// In-app notifications on.
        /// </summary>
        public bool InApp { get; set; } = true;

        /// <summary>
        /// Push notifications on.
        /// </summary>
        public bool Push { get; set; } = true;

        /// <summary>
        /// Email on.
        /// </summary>
        public bool Email { get; set; } = false;

        /// <summary>
        /// Last change (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }
}
