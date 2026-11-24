namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Account export item of a block the member created.
    /// </summary>
    public class BlockExportItem
    {
        #region Public-Members

        /// <summary>
        /// Blocked member.
        /// </summary>
        public UserSummary User { get; set; } = new UserSummary();

        /// <summary>
        /// Blocked (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
