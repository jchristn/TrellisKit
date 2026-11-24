namespace TrellisKit.Core.Responses
{
    /// <summary>
    /// Report counts of a target: { open, total }.
    /// </summary>
    public class ReportSummaryView
    {
        #region Public-Members

        /// <summary>
        /// Open reports.
        /// </summary>
        public long Open { get; set; } = 0;

        /// <summary>
        /// All reports.
        /// </summary>
        public long Total { get; set; } = 0;

        #endregion
    }
}
