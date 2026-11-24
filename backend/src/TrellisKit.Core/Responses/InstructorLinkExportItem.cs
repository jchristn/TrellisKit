namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// A member's instructor link in the account export.
    /// </summary>
    public class InstructorLinkExportItem
    {
        #region Public-Members

        /// <summary>
        /// Instructor.
        /// </summary>
        public string InstructorId { get; set; } = String.Empty;

        /// <summary>
        /// Display name.
        /// </summary>
        public string DisplayName { get; set; } = String.Empty;

        /// <summary>
        /// Linked.
        /// </summary>
        public DateTime? LinkedUtc { get; set; } = null;

        #endregion
    }
}
