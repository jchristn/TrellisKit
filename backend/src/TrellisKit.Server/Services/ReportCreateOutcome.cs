namespace TrellisKit.Server.Services
{
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Result of filing a report: the report and whether this call created it (201) or found the caller's existing
    /// Open report on the same target (200).
    /// </summary>
    public class ReportCreateOutcome
    {
        #region Public-Members

        /// <summary>
        /// Whether a new report was created.
        /// </summary>
        public bool Created { get; set; } = false;

        /// <summary>
        /// The report.
        /// </summary>
        public ReportCreatedView Report { get; set; } = new ReportCreatedView();

        /// <summary>
        /// Whether this report made the content Hidden (auto-hide).
        /// </summary>
        public bool AutoHidden { get; set; } = false;

        #endregion
    }
}
