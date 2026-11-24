namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A member due for a notification category email digest (docs/PHASE3_CONTRACT.md section 9.3): email turned on for
    /// the category, a verified address, and no digest check within the interval.
    /// </summary>
    public class EmailDigestCandidate
    {
        #region Public-Members

        /// <summary>
        /// Member identifier.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Verified email address.
        /// </summary>
        public string Email { get; set; } = String.Empty;

        /// <summary>
        /// Display name.
        /// </summary>
        public string DisplayName { get; set; } = String.Empty;

        /// <summary>
        /// Notification category of the digest.
        /// </summary>
        public NotificationCategoryEnum Category { get; set; } = NotificationCategoryEnum.Following;

        /// <summary>
        /// Last digest check, or null when the member never had one.
        /// </summary>
        public DateTime? LastCheckedUtc { get; set; } = null;

        /// <summary>
        /// Last digest sent, or null.
        /// </summary>
        public DateTime? LastSentUtc { get; set; } = null;

        #endregion
    }
}
