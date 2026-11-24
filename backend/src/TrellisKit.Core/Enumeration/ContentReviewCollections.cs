namespace TrellisKit.Core.Enumeration
{
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Enumeration collections of the content changelog (docs/INTEGRATION.md content review contract). Alias: contentchanges c.
    /// </summary>
    public static class ContentReviewCollections
    {
        #region Public-Members

        /// <summary>
        /// POST /api/v1/admin/content-review/enumerate: the content review queue. Orderings CreatedDescending (default),
        /// CreatedAscending, SeverityDescending (Critical first, then newest), ReviewedDescending. Search over the
        /// snapshot, the reviewer note, and the target id. Filters status, category, severity, minimumSeverity,
        /// targetType, targetId, client, action, actorUserId, authorUserId.
        /// </summary>
        public static readonly EnumerationCollection Queue = new EnumerationCollection("admin-content-review", TargetTypeEnum.ContentChange, "c.id", "c.createdutc", true)
            .Order("SeverityDescending", "(CASE c.severity WHEN 'Critical' THEN 4 WHEN 'High' THEN 3 WHEN 'Medium' THEN 2 WHEN 'Low' THEN 1 ELSE 0 END)", true, EnumerationKeyKindEnum.Number)
            .Order("ReviewedDescending", "COALESCE(c.reviewedutc, c.createdutc)", true, EnumerationKeyKindEnum.Time)
            .Search("c.snapshot", "c.reviewernote", "c.targetid")
            .Filters("status", "category", "severity", "minimumSeverity", "targetType", "targetId", "client", "action", "actorUserId", "authorUserId")
            .DefaultTo("CreatedDescending");

        #endregion
    }
}
