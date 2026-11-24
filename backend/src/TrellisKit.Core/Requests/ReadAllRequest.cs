namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body of POST /api/v1/notifications/read-all.
    /// </summary>
    public class ReadAllRequest
    {
        /// <summary>
        /// Mark notifications last updated at or before this time; null means now.
        /// </summary>
        public DateTime? BeforeUtc { get; set; } = null;
    }
}
