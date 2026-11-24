namespace TrellisKit.Core.Requests
{
    using System.Collections.Generic;

    /// <summary>
    /// PUT /api/v1/me/notification-preferences: any subset of the settings document.
    /// </summary>
    public class NotificationPreferencesRequest
    {
        /// <summary>
        /// Category changes, or null.
        /// </summary>
        public List<CategoryPreferenceRequest>? Categories { get; set; } = null;

        /// <summary>
        /// Notification hours changes, or null.
        /// </summary>
        public NotificationHoursRequest? NotificationHours { get; set; } = null;

        /// <summary>
        /// Pause every push, or null for unchanged.
        /// </summary>
        public bool? PushPaused { get; set; } = null;

        /// <summary>
        /// Follow threads on first comment (true or false), or null to keep.
        /// </summary>
        public bool? AutoFollowOnComment { get; set; } = null;
    }
}
