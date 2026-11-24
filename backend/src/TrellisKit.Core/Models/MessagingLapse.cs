namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// When a member lost messaging access in a gated model (as-built note B).
    /// </summary>
    public class MessagingLapse
    {
        #region Public-Members

        /// <summary>
        /// Member.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Lapsed (UTC).
        /// </summary>
        public DateTime LapsedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the member's conversations were removed for them, or null.
        /// </summary>
        public DateTime? PurgedUtc { get; set; } = null;

        #endregion
    }
}
