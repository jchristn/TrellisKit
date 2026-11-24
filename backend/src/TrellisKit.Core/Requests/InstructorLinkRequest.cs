namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Link an instructor entry to a member account (staff).
    /// </summary>
    public class InstructorLinkRequest
    {
        #region Public-Members

        /// <summary>
        /// Member identifier.
        /// </summary>
        public string? UserId { get; set; } = null;

        #endregion
    }
}
