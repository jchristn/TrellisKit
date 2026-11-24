namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// A member with unread direct messages due for the unread messages email.
    /// </summary>
    public class MessageEmailCandidate
    {
        #region Public-Members

        /// <summary>
        /// Member.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Conversations with unread messages older than the email delay.
        /// </summary>
        public long Conversations { get; set; } = 0;

        /// <summary>
        /// Unread messages older than the email delay.
        /// </summary>
        public long Messages { get; set; } = 0;

        /// <summary>
        /// Last unread email (UTC), or null.
        /// </summary>
        public DateTime? LastSentUtc { get; set; } = null;

        #endregion
    }
}
