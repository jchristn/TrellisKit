namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// The answer of an approval: the submission and the entry it created or changed.
    /// </summary>
    public class SubmissionApprovalView
    {
        #region Public-Members

        /// <summary>
        /// Submission.
        /// </summary>
        public DirectorySubmissionView Submission { get; set; } = new DirectorySubmissionView();

        /// <summary>
        /// The Venue or Instructor (admin view).
        /// </summary>
        public object? Target { get; set; } = null;

        #endregion
    }
}
