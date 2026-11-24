namespace TrellisKit.Core.Requests
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Filters of the email message log.
    /// </summary>
    public class EmailMessageFilter
    {
        #region Public-Members

        /// <summary>
        /// Status.
        /// </summary>
        public EmailStatusEnum? Status { get; set; } = null;

        /// <summary>
        /// Template key.
        /// </summary>
        public string? Template { get; set; } = null;

        /// <summary>
        /// Category.
        /// </summary>
        public EmailCategoryEnum? Category { get; set; } = null;

        /// <summary>
        /// Recipient address (exact, case-insensitive).
        /// </summary>
        public string? Recipient { get; set; } = null;

        /// <summary>
        /// Member.
        /// </summary>
        public string? UserId { get; set; } = null;

        #endregion
    }
}
