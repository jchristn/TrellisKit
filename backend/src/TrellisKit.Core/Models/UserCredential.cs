namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// A member's password credential (one per member).
    /// </summary>
    public class UserCredential
    {
        #region Public-Members

        /// <summary>
        /// Identifier (prefix crd_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Member identifier.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Encoded password hash from IPasswordHasher.
        /// </summary>
        public string PasswordHash { get; set; } = String.Empty;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last changed (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
