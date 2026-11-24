namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// One bounded page of a CSV export: the CSV text (header row plus at most enumeration.maxResultsAdmin rows) and the
    /// continuation token for the next page, sent as the X-Continuation-Token response header (absent on the last page).
    /// </summary>
    public class CsvPage
    {
        #region Public-Members

        /// <summary>
        /// CSV text with a header row.
        /// </summary>
        public string Csv { get; set; } = String.Empty;

        /// <summary>
        /// Token for the next page, or null on the last page.
        /// </summary>
        public string? ContinuationToken { get; set; } = null;

        /// <summary>
        /// Exact number of matching records across every page.
        /// </summary>
        public long? TotalRecords { get; set; } = null;

        #endregion
    }
}
