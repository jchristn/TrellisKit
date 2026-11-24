namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Status of a directory submission (docs/PHASE4_CONTRACT.md section 2.4).
    /// </summary>
    public enum DirectorySubmissionStatusEnum
    {
        /// <summary>
        /// Not queued: possible duplicates were found and the member has not confirmed (response only).
        /// </summary>
        Draft,

        /// <summary>
        /// Waiting for review.
        /// </summary>
        Pending,

        /// <summary>
        /// Approved and applied.
        /// </summary>
        Approved,

        /// <summary>
        /// Rejected by a reviewer.
        /// </summary>
        Rejected,

        /// <summary>
        /// Withdrawn by the submitter (or closed with the account).
        /// </summary>
        Withdrawn,

        /// <summary>
        /// Closed by a reviewer because another approved change made it stale.
        /// </summary>
        Superseded
    }
}
