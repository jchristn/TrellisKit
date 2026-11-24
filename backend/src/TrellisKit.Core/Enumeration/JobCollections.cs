namespace TrellisKit.Core.Enumeration
{
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Enumeration collections of the job engine (admin).
    /// </summary>
    public static class JobCollections
    {
        #region Public-Members

        /// <summary>Job definitions, with key/name search and persistent metadata filters.</summary>
        public static readonly EnumerationCollection AdminDefinitions = new EnumerationCollection("admin-job-definitions", TargetTypeEnum.JobDefinition, "d.id", "d.createdutc", true)
            .Search("d.key", "d.name", "d.description")
            .Order("NameAscending", "d.name", false, EnumerationKeyKindEnum.Text);


        /// <summary>
        /// POST /api/v1/admin/jobs/runs/enumerate: newest first. Filters key, status, trigger.
        /// </summary>
        public static readonly EnumerationCollection AdminRuns = new EnumerationCollection("admin-job-runs", TargetTypeEnum.JobRun, "r.id", "r.createdutc", true)
            .Filters("key", "status", "trigger")
            .DefaultTo("CreatedDescending");

        /// <summary>
        /// POST /api/v1/admin/jobs/items/enumerate and .../runs/{id}/items/enumerate: newest first. Search over the
        /// target key and error. Filters runId, key, status (Dead for the dead-letter view).
        /// </summary>
        public static readonly EnumerationCollection AdminItems = new EnumerationCollection("admin-job-items", TargetTypeEnum.JobItem, "i.id", "i.createdutc", true)
            .Search("i.targetkey", "i.error")
            .Filters("runId", "key", "status")
            .DefaultTo("CreatedDescending");

        #endregion
    }
}
