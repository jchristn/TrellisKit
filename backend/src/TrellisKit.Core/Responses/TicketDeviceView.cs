namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Device block captured at ticket creation from the X-Client headers (when the requester included diagnostics).
    /// </summary>
    public class TicketDeviceView
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
        /// X-Client-Update-Id.
        /// </summary>
        public string? UpdateId { get; set; } = null;

        /// <summary>
        /// Locale (X-Client-Locale or Accept-Language).
        /// </summary>
        public string? Locale { get; set; } = null;

        /// <summary>
        /// IANA time zone (X-Client-Time-Zone).
        /// </summary>
        public string? TimeZone { get; set; } = null;

        #endregion
    }
}
