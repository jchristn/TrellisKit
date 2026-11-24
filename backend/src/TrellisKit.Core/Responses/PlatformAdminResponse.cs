namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A platform administrator.
    /// </summary>
    public class PlatformAdminResponse
    {
        #region Public-Members

        /// <summary>
        /// Identifier (pad_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Email.
        /// </summary>
        public string Email { get; set; } = String.Empty;

        /// <summary>
        /// Display name.
        /// </summary>
        public string DisplayName { get; set; } = String.Empty;

        /// <summary>
        /// Whether the invitation has not been accepted yet.
        /// </summary>
        public bool InvitationPending { get; set; } = false;

        /// <summary>
        /// Last sign-in.
        /// </summary>
        public DateTime? LastLoginUtc { get; set; } = null;

        /// <summary>
        /// Created time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
