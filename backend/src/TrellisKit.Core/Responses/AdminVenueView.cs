namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A venue in the admin console (docs/PHASE4_CONTRACT.md section 3.9): exact address plus staff counters.
    /// </summary>
    public class AdminVenueView : VenueView
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
        /// Geo source (None means it needs a pin).
        /// </summary>
        public GeoSourceEnum GeoSource { get; set; } = GeoSourceEnum.None;

        /// <summary>
        /// When the position was set.
        /// </summary>
        public DateTime? GeocodedUtc { get; set; } = null;

        /// <summary>
        /// No position yet.
        /// </summary>
        public bool NeedsPin { get; set; } = false;

        /// <summary>
        /// Moderation state.
        /// </summary>
        public ModerationStateEnum ModerationState { get; set; } = ModerationStateEnum.None;

        /// <summary>
        /// Close reason.
        /// </summary>
        public string? ClosedReason { get; set; } = null;

        /// <summary>
        /// When it was closed.
        /// </summary>
        public DateTime? ClosedUtc { get; set; } = null;

        #endregion
    }
}
