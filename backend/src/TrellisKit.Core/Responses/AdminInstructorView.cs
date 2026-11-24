namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// An instructor in the admin console (docs/PHASE4_CONTRACT.md section 3.9).
    /// </summary>
    public class AdminInstructorView : InstructorView
    {
        #region Public-Members

        /// <summary>
        /// Open reports.
        /// </summary>
        public int OpenReportCount { get; set; } = 0;

        /// <summary>
        /// Pending submissions on it.
        /// </summary>
        public int PendingSubmissionCount { get; set; } = 0;

        /// <summary>
        /// Moderation state.
        /// </summary>
        public ModerationStateEnum ModerationState { get; set; } = ModerationStateEnum.None;

        /// <summary>
        /// When the account was linked.
        /// </summary>
        public DateTime? LinkedUtc { get; set; } = null;

        #endregion
    }
}
