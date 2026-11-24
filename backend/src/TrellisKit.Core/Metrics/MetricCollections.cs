namespace TrellisKit.Core.Metrics
{
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    /// <summary>Collection contracts shared by metric routes and their database readers.</summary>
    public static class MetricCollections
    {
        /// <summary>Property-local definitions.</summary>
        public static readonly EnumerationCollection Definitions = new EnumerationCollection("metric-definitions", null, "key", "createdutc", true) { HasInlineMetadata = true }.Order("KeyAscending", "key", false, EnumerationKeyKindEnum.Text).Filters("kind", "source", "feature");
        /// <summary>Property-local dashboard declarations.</summary>
        public static readonly EnumerationCollection Pages = new EnumerationCollection("metric-pages", null, "id", "createdutc", true) { HasInlineMetadata = true };
        /// <summary>Actual current/comparison observations.</summary>
        public static readonly EnumerationCollection Observations = new EnumerationCollection("metric-observations", null, "id", "startutc", true) { HasInlineMetadata = true }.Filters("period", "seriesKey");
        /// <summary>Persistent worker passes.</summary>
        public static readonly EnumerationCollection Workers = new EnumerationCollection("metric-worker-passes", null, "w.id", "w.startedutc", true) { HasInlineMetadata = true }.Search("w.worker", "w.outcome", "w.error").Filters("worker", "outcome", "from", "to");
        /// <summary>Persistent operational annotations.</summary>
        public static readonly EnumerationCollection Annotations = new EnumerationCollection("metric-annotations", null, "a.id", "a.occurredutc", true) { HasInlineMetadata = true }.Search("a.kind", "a.label").Filters("kind", "from", "to");
    }
}
