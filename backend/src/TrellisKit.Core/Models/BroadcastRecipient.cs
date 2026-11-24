namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// One recipient of a broadcast batch with what the send worker needs per channel.
    /// </summary>
    public class BroadcastRecipient
    {
        #region Public-Members

        /// <summary>
        /// Member.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Display name.
        /// </summary>
        public string DisplayName { get; set; } = String.Empty;

        /// <summary>
        /// Email address, or null.
        /// </summary>
        public string? Email { get; set; } = null;

        /// <summary>
        /// Whether the address is verified.
        /// </summary>
        public bool EmailVerified { get; set; } = false;

        /// <summary>
        /// Whether the address is on the suppression list.
        /// </summary>
        public bool EmailSuppressed { get; set; } = false;

        /// <summary>
        /// The Announcements push preference.
        /// </summary>
        public bool PushPreference { get; set; } = true;

        /// <summary>
        /// The Announcements email preference.
        /// </summary>
        public bool EmailPreference { get; set; } = true;

        /// <summary>
        /// Whether the member has an active push token.
        /// </summary>
        public bool HasPushToken { get; set; } = false;

        #endregion
    }
}
