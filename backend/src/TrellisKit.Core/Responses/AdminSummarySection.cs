namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A titled group of administrator summary lines.
    /// </summary>
    public class AdminSummarySection
    {
        #region Public-Members

        /// <summary>
        /// Section title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Lines in display order.
        /// </summary>
        public List<AdminSummaryRow> Rows { get; set; } = new List<AdminSummaryRow>();

        #endregion
    }
}
