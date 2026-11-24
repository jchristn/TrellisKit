namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body of POST /api/v1/admin/members/{id}/mute.
    /// </summary>
    public class MuteRequest
    {
        #region Public-Members

        /// <summary>
        /// When the mute ends (UTC), or null for indefinite.
        /// </summary>
        public DateTime? UntilUtc { get; set; } = null;

        /// <summary>
        /// Reason (required).
        /// </summary>
        public string? Reason { get; set; } = null;

        #endregion
    }
}
