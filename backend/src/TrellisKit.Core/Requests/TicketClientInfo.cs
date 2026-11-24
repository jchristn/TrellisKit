namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Client diagnostics headers of POST /api/v1/tickets (docs/PHASE5_CONTRACT.md section 6.1), stored only when the requester includes diagnostics. Values are trimmed to 200 characters.
    /// </summary>
    public class TicketClientInfo
    {
        #region Public-Members

        /// <summary>
        /// X-Client.
        /// </summary>
        public string? Client { get; set; } = null;

        /// <summary>
        /// X-Client-Version.
        /// </summary>
        public string? ClientVersion { get; set; } = null;

        /// <summary>
        /// X-Client-Update-Id.
        /// </summary>
        public string? UpdateId { get; set; } = null;

        /// <summary>
        /// X-Client-Platform.
        /// </summary>
        public string? Platform { get; set; } = null;

        /// <summary>
        /// X-Client-Os-Version.
        /// </summary>
        public string? OsVersion { get; set; } = null;

        /// <summary>
        /// X-Client-Device-Model.
        /// </summary>
        public string? DeviceModel { get; set; } = null;

        /// <summary>
        /// X-Client-Locale, or the first Accept-Language entry.
        /// </summary>
        public string? Locale { get; set; } = null;

        /// <summary>
        /// X-Client-Time-Zone.
        /// </summary>
        public string? TimeZone { get; set; } = null;

        #endregion
    }
}
