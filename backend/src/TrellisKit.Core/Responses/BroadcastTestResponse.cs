namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// POST /api/v1/admin/broadcasts/{id}/test response (202).
    /// </summary>
    public class BroadcastTestResponse
    {
        #region Public-Members

        /// <summary>
        /// Always true.
        /// </summary>
        public bool Accepted { get; set; } = true;

        /// <summary>
        /// Channels tested (inApp, push, email; at most 3).
        /// </summary>
        public List<string> Channels { get; set; } = new List<string>();

        /// <summary>
        /// The test announcement the caller can open (/announcements/{id}).
        /// </summary>
        public string AnnouncementId { get; set; } = String.Empty;

        #endregion
    }
}
