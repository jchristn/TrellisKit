namespace TrellisKit.Core.Responses
{
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One category row of the notification settings document.
    /// </summary>
    public class CategoryPreferenceView : CatalogEntryMetadata
    {
        /// <summary>
        /// Category.
        /// </summary>
        public NotificationCategoryEnum Category { get; set; } = NotificationCategoryEnum.Replies;

        /// <summary>
        /// In-app on.
        /// </summary>
        public bool InApp { get; set; } = true;

        /// <summary>
        /// Push on.
        /// </summary>
        public bool Push { get; set; } = true;

        /// <summary>
        /// Email on.
        /// </summary>
        public bool Email { get; set; } = false;

        /// <summary>
        /// Which cells members may change.
        /// </summary>
        public ChannelFlags Editable { get; set; } = new ChannelFlags();
    }
}
