namespace TrellisKit.Core.Database
{
    using System;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Enumeration collections of the platform (deployment-level) lists. Table aliases: properties p,
    /// platformadmins pa. Their labels and tags live under the platform realm.
    /// </summary>
    public static class PlatformCollections
    {
        #region Public-Members

        /// <summary>
        /// POST /api/v1/platform/properties/enumerate: search over id, slug, and name; filters status, host.
        /// </summary>
        public static readonly EnumerationCollection Properties = new EnumerationCollection("platform-properties", TargetTypeEnum.Property, "p.id", "p.createdutc", true)
            .Order("NameAscending", "lower(p.name)", false, EnumerationKeyKindEnum.Text)
            .Order("UpdatedDescending", "p.updatedutc", true, EnumerationKeyKindEnum.Time)
            .Search("p.id", "p.slug", "p.name")
            .Filters("status", "host");

        /// <summary>
        /// POST /api/v1/platform/admins/enumerate: search over email and display name.
        /// </summary>
        public static readonly EnumerationCollection PlatformAdmins = new EnumerationCollection("platform-admins", TargetTypeEnum.PlatformAdmin, "pa.id", "pa.createdutc", true)
            .Order("EmailAscending", "lower(pa.email)", false, EnumerationKeyKindEnum.Text)
            .Search("pa.email", "pa.displayname");

        /// <summary>
        /// POST /api/v1/platform/audit/enumerate: search over action, actor name, and summary; filters propertyId,
        /// actorUserId, action, targetType, targetId.
        /// </summary>
        public static readonly EnumerationCollection Audit = new EnumerationCollection("platform-audit", TargetTypeEnum.AuditRecord, "ar.id", "ar.createdutc", true)
            .Search("ar.action", "ar.actorname", "ar.summary")
            .Filters("propertyId", "actorUserId", "action", "targetType", "targetId");

        /// <summary>
        /// Computed property metrics ordering: members, most first.
        /// </summary>
        public const string MetricsMembersDescending = "MembersDescending";

        /// <summary>
        /// Computed property metrics ordering: 5xx responses in 24 hours, most first.
        /// </summary>
        public const string MetricsErrorsDescending = "ErrorsDescending";

        /// <summary>
        /// Computed property metrics ordering: stored media bytes, most first.
        /// </summary>
        public const string MetricsStorageDescending = "StorageDescending";

        /// <summary>
        /// POST /api/v1/platform/request-history/enumerate: one property's request history (filters.propertyId, default
        /// the platform realm; unresolved and platform included), with the request history filters.
        /// </summary>
        public static readonly EnumerationCollection RequestHistory = new EnumerationCollection("platform-request-history", TargetTypeEnum.RequestHistory, "rh.id", "rh.createdutc", true)
            .Search("rh.path", "rh.principalname")
            .Filters("propertyId", "method", "statusCode", "pathContains", "userId");

        /// <summary>
        /// POST /api/v1/platform/metrics/properties/enumerate: one metrics row per registered property (at most
        /// deployment.maxProperties). Search over id, slug, and name; filter status. The computed orderings
        /// MembersDescending, ErrorsDescending, and StorageDescending sort the computed metrics of every matching
        /// property (bounded by DeploymentSettings.MaxPropertiesLimit) and use standard keyset cursors (or skip for page jumps).
        /// </summary>
        public static readonly EnumerationCollection PropertyMetrics = new EnumerationCollection("platform-property-metrics", TargetTypeEnum.Property, "p.id", "p.createdutc", true)
            .Order("NameAscending", "lower(p.name)", false, EnumerationKeyKindEnum.Text)
            .Order(MetricsMembersDescending, "p.createdutc", true, EnumerationKeyKindEnum.Number)
            .Order(MetricsErrorsDescending, "p.createdutc", true, EnumerationKeyKindEnum.Number)
            .Order(MetricsStorageDescending, "p.createdutc", true, EnumerationKeyKindEnum.Number)
            .Search("p.id", "p.slug", "p.name")
            .Filters("status");

        #endregion
    }
}
