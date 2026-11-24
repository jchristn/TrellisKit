namespace TrellisKit.Core.Requests
{
    using System.Collections.Generic;

    /// <summary>
    /// Body of the notification read and unread routes.
    /// </summary>
    public class NotificationIdsRequest
    {
        /// <summary>
        /// Notification identifiers (1 to 100), or null.
        /// </summary>
        public List<string>? Ids { get; set; } = null;
    }
}
