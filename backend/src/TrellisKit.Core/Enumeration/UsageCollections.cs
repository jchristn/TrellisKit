namespace TrellisKit.Core.Enumeration
{
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Enumeration collections of usage metering (docs/USAGE_METERING_CONTRACT.md section 7). Aliases: usagedaily ud,
    /// properties p.
    /// </summary>
    public static class UsageCollections
    {
        #region Public-Members

        /// <summary>
        /// Computed comparison ordering names of the platform usage table, in display order.
        /// </summary>
        public static readonly string[] ComparisonOrderings = new[]
        {
            "UsersDescending", "ActiveUsersDescending", "PostsDescending", "CommentsDescending", "InteractionsDescending", "ImagesDescending",
            "VideosDescending", "StorageDescending", "ApiRequestsDescending", "PushDescending", "EmailDescending", "AiTokensDescending", "AiCostDescending"
        };

        /// <summary>
        /// POST /api/v1/admin/usage/enumerate: daily usage records. The created window applies to the period start.
        /// Orderings CreatedDescending (default), CreatedAscending, MeterAscending, ValueDescending. Search over meter.
        /// Filters meter (id or group), fromDay, toDay, closed.
        /// </summary>
        public static readonly EnumerationCollection Records = new EnumerationCollection("admin-usage-records", TargetTypeEnum.UsageRecord, "ud.id", "ud.periodstartutc", true)
            .Order("MeterAscending", "ud.meter", false, EnumerationKeyKindEnum.Text)
            .Order("ValueDescending", "ud.value", true, EnumerationKeyKindEnum.Number)
            .Search("ud.meter")
            .Filters("meter", "fromDay", "toDay", "closed");

        /// <summary>
        /// POST /api/v1/platform/usage/properties/enumerate: one usage row per registered property for a period. Search
        /// over id, slug, and name; filters period, from, to, status. The computed orderings sort every matching
        /// property (bounded by the registry scan limit) and page with standard keyset cursors.
        /// </summary>
        public static readonly EnumerationCollection PropertyUsage = Build();

        #endregion

        #region Private-Methods

        private static EnumerationCollection Build()
        {
            EnumerationCollection collection = new EnumerationCollection("platform-property-usage", TargetTypeEnum.Property, "p.id", "p.createdutc", true)
                .Order("NameAscending", "lower(p.name)", false, EnumerationKeyKindEnum.Text);
            foreach (string name in ComparisonOrderings) collection.Order(name, "p.createdutc", true, EnumerationKeyKindEnum.Number);
            return collection.Search("p.id", "p.slug", "p.name").Filters("period", "from", "to", "status");
        }

        #endregion
    }
}
