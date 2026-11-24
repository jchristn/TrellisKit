namespace TrellisKit.Core.Responses
{
    using System.Collections.Generic;

    /// <summary>
    /// The notification settings document (Phase 2 contract 8.5).
    /// </summary>
    public class NotificationPreferencesDocument
    {
        /// <summary>
        /// Every push is paused.
        /// </summary>
        public bool PushPaused { get; set; } = false;

        /// <summary>
        /// Whether the member follows threads they comment on (null when forums are not available).
        /// </summary>
        public bool? AutoFollowOnComment { get; set; } = null;

        /// <summary>
        /// One row per category in display order (fixed size, six rows). Never null.
        /// </summary>
        public List<CategoryPreferenceView> Categories
        {
            get { return _Categories; }
            set { _Categories = value ?? new List<CategoryPreferenceView>(); }
        }

        /// <summary>
        /// Notification hours.
        /// </summary>
        public NotificationHoursView NotificationHours { get; set; } = new NotificationHoursView();

        private List<CategoryPreferenceView> _Categories = new List<CategoryPreferenceView>();
    }
}
