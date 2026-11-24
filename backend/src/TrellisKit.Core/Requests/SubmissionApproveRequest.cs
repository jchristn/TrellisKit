namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Approve a submission.
    /// </summary>
    public class SubmissionApproveRequest
    {
        #region Public-Members

        /// <summary>
        /// Note to the submitter (0 to 1000).
        /// </summary>
        public string? ReviewNote { get; set; } = null;

        /// <summary>
        /// Send SubmissionDecision and the decision email.
        /// </summary>
        public bool NotifySubmitter { get; set; } = true;

        /// <summary>
        /// Pin to place (overrides the proposed one).
        /// </summary>
        public GeoRequest? Geo { get; set; } = null;

        #endregion
    }
}
