namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// A member's opt-in public profile page (docs/PHASE4_CONTRACT.md section 18.2): off until the member turns it on.
    /// </summary>
    public class ProfilePage
    {
        #region Public-Members

        /// <summary>
        /// Member identifier.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Whether the page is published. Default false.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Current slug (lower case), or null until one is chosen.
        /// </summary>
        public string? Slug { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
