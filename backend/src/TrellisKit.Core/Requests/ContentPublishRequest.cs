namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Body of POST /api/v1/admin/app/publish and POST /api/v1/admin/app/versions/{version}/restore.
    /// </summary>
    public class ContentPublishRequest
    {
        /// <summary>
        /// Message (1 to 500 characters).
        /// </summary>
        public string Message { get; set; } = String.Empty;

        /// <summary>
        /// Content version the drafts are based on (publish).
        /// </summary>
        public long? ExpectedVersion { get; set; } = null;
    }
}
