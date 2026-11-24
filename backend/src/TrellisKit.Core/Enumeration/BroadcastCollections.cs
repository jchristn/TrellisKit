namespace TrellisKit.Core.Enumeration
{
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Enumeration collections of broadcasts and announcements (docs/PHASE5_CONTRACT.md section 8). Table aliases:
    /// broadcasts b, broadcastsegments s, broadcastdeliveries d.
    /// </summary>
    public static class BroadcastCollections
    {
        #region Public-Members

        /// <summary>
        /// POST /api/v1/admin/broadcasts/enumerate: newest first by default; search over the title and the announcement
        /// title; filters status and channel (inApp, push, email). Test copies are never listed.
        /// </summary>
        public static readonly EnumerationCollection Broadcasts = new EnumerationCollection("admin-broadcasts", TargetTypeEnum.Broadcast, "b.id", "b.createdutc", true)
            .Order("UpdatedDescending", "b.updatedutc", true, EnumerationKeyKindEnum.Time)
            .Order("SendAtAscending", "COALESCE(b.sendatutc, b.createdutc)", false, EnumerationKeyKindEnum.Time)
            .Search("b.title", "b.inapptitle")
            .Filters("status", "channel");

        /// <summary>
        /// POST /api/v1/admin/broadcasts/segments/enumerate: newest first by default; search over name and description.
        /// </summary>
        public static readonly EnumerationCollection Segments = new EnumerationCollection("admin-broadcast-segments", TargetTypeEnum.BroadcastSegment, "s.id", "s.createdutc", true)
            .Order("NameAscending", "lower(s.name)", false, EnumerationKeyKindEnum.Text)
            .Search("s.name", "s.description");

        /// <summary>
        /// POST /api/v1/admin/broadcasts/{id}/deliveries/enumerate: newest first by default; filters channel (inApp, push,
        /// email) and status (a delivery status; with channel it applies to that channel, else to any channel).
        /// </summary>
        public static readonly EnumerationCollection Deliveries = new EnumerationCollection("admin-broadcast-deliveries", TargetTypeEnum.BroadcastDelivery, "d.id", "d.createdutc", true)
            .Filters("channel", "status");

        /// <summary>
        /// POST /api/v1/announcements/enumerate: the caller's received broadcasts, newest first; search over the
        /// announcement title.
        /// </summary>
        public static readonly EnumerationCollection Announcements = new EnumerationCollection("announcements", TargetTypeEnum.Broadcast, "b.id", "d.createdutc", false)
            .Search("COALESCE(b.inapptitle, b.title)");

        #endregion
    }
}
