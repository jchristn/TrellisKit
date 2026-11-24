namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A member's personal calendar subscription (docs/PHASE4_CONTRACT.md section 4.5).
    /// </summary>
    public class CalendarFeed
    {
        #region Public-Members

        /// <summary>
        /// Identifier (cfd_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Member.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// SHA-256 of the secret (hex).
        /// </summary>
        public string SecretHash { get; set; } = String.Empty;

        /// <summary>
        /// Whether the feed serves.
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Last fetch, or null.
        /// </summary>
        public DateTime? LastFetchedUtc { get; set; } = null;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last rotation, or null.
        /// </summary>
        public DateTime? RotatedUtc { get; set; } = null;

        #endregion
    }
}
