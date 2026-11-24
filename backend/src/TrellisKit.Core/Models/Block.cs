namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// One member blocking another (contract section 9.2). Blocks are private: the blocked member is never told.
    /// </summary>
    public class Block
    {
        #region Public-Members

        /// <summary>
        /// Member who blocked.
        /// </summary>
        public string BlockerUserId { get; set; } = String.Empty;

        /// <summary>
        /// Member who was blocked.
        /// </summary>
        public string BlockedUserId { get; set; } = String.Empty;

        /// <summary>
        /// When the block was created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
