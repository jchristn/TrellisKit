namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// A mention in a payload (contract section 2): handle exactly as written.
    /// </summary>
    public class MentionView
    {
        #region Public-Members

        /// <summary>
        /// Mentioned member.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// Handle as written in the body (case preserved).
        /// </summary>
        public string Handle { get; set; } = String.Empty;

        /// <summary>
        /// Current display name.
        /// </summary>
        public string DisplayName { get; set; } = String.Empty;

        #endregion
    }
}
