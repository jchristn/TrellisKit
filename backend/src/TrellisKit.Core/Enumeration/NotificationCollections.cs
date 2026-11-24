namespace TrellisKit.Core.Enumeration
{
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Enumeration collections of the notifications slice.
    /// </summary>
    public static class NotificationCollections
    {
        /// <summary>
        /// A member's notifications (alias n over notifications). Orderings UpdatedDescending (default),
        /// CreatedDescending, CreatedAscending. Search over title and body. Filters unreadOnly (boolean), type, category.
        /// </summary>
        public static readonly EnumerationCollection Notifications = new EnumerationCollection("notifications", TargetTypeEnum.Notification, "n.id", "n.createdutc", false)
            .Order("UpdatedDescending", "n.updatedutc", true, EnumerationKeyKindEnum.Time)
            .DefaultTo("UpdatedDescending")
            .Search("n.title", "n.body")
            .Filters("unreadOnly", "type", "category");

        /// <summary>
        /// A member's push tokens (alias t over pushtokens; at most 10 exist). Orderings LastSeenDescending (default),
        /// CreatedDescending, CreatedAscending. Search over device name.
        /// </summary>
        public static readonly EnumerationCollection PushTokens = new EnumerationCollection("me-push-tokens", TargetTypeEnum.PushToken, "t.id", "t.createdutc", false)
            .Order("LastSeenDescending", "t.lastseenutc", true, EnumerationKeyKindEnum.Time)
            .DefaultTo("LastSeenDescending")
            .Search("t.devicename");
    }
}
