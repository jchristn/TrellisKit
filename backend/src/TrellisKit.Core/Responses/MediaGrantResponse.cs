namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// Media grant (POST /api/v1/media/grant).
    /// </summary>
    public class MediaGrantResponse
    {
        #region Public-Members

        /// <summary>
        /// CloudFront or Local.
        /// </summary>
        public MediaDeliveryModeEnum Mode { get; set; } = MediaDeliveryModeEnum.Local;

        /// <summary>
        /// Grant expiry.
        /// </summary>
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Cookies (at most three).
        /// </summary>
        public List<MediaCookie> Cookies { get; set; } = new List<MediaCookie>();

        #endregion
    }
}
