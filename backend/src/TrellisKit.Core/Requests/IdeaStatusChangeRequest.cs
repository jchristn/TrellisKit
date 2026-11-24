namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// PUT /api/v1/admin/ideas/{id}/status.
    /// </summary>
    public class IdeaStatusChangeRequest
    {
        #region Public-Members

        /// <summary>
        /// New status key (required).
        /// </summary>
        public string? Status { get; set; } = null;

        /// <summary>
        /// Note shown with the status (0 to 1000 characters), or null.
        /// </summary>
        public string? StatusNote { get; set; } = null;

        /// <summary>
        /// Notify voters and followers (default true).
        /// </summary>
        public bool? NotifyVoters { get; set; } = null;

        #endregion
    }
}
