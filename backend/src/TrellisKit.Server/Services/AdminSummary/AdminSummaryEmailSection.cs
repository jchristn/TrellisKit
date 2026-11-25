namespace TrellisKit.Server.Services.AdminSummary
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Server.Services.Email.Blocks;

    /// <summary>
    /// One section of the summary as stored in the queued message's report token: a title and label/value lines.
    /// </summary>
    public sealed class AdminSummaryEmailSection
    {
        #region Public-Members

        /// <summary>
        /// Section title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Lines (label and formatted value).
        /// </summary>
        public List<EmailField> Lines { get; set; } = new List<EmailField>();

        #endregion
    }
}
