namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Reject a submission.
    /// </summary>
    public class SubmissionRejectRequest
    {
        #region Public-Members

        /// <summary>
        /// Reason (required, 3 to 1000).
        /// </summary>
        public string? ReviewNote { get; set; } = null;

        /// <summary>
        /// Send SubmissionDecision and the decision email.
        /// </summary>
        public bool NotifySubmitter { get; set; } = true;

        #endregion
    }
}
