namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One run of a job definition (docs/INTEGRATION.md).
    /// </summary>
    public class JobRun
    {
        /// <summary>Internal fencing token for the current planning claim.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string? PlanningToken { get; set; } = null;

        /// <summary>Internal planning lease expiry; unplanned runs can be reclaimed after it.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public DateTime? PlanningUntilUtc { get; set; } = null;

        #region Public-Members

        /// <summary>
        /// Identifier (jbr_...).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Definition identifier.
        /// </summary>
        public string DefinitionId { get; set; } = String.Empty;

        /// <summary>
        /// Handler key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// What started the run.
        /// </summary>
        public JobTriggerEnum Trigger { get; set; } = JobTriggerEnum.Schedule;

        /// <summary>
        /// Status.
        /// </summary>
        public JobRunStatusEnum Status { get; set; } = JobRunStatusEnum.Queued;

        /// <summary>
        /// True for a dry run (handlers must not write lasting changes).
        /// </summary>
        public bool IsDryRun { get; set; } = false;

        /// <summary>
        /// Why the run was skipped, or null.
        /// </summary>
        public string? SkippedReason { get; set; } = null;

        /// <summary>
        /// Planning or run error, or null.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Administrator who started a manual run, or null.
        /// </summary>
        public string? RequestedByUserId { get; set; } = null;

        /// <summary>
        /// Worker that planned the run, or null.
        /// </summary>
        public string? WorkerId { get; set; } = null;

        /// <summary>
        /// UTC time queued.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC time planning started, or null.
        /// </summary>
        public DateTime? StartedUtc { get; set; } = null;

        /// <summary>
        /// UTC time the run finished, or null.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        /// <summary>
        /// Items planned.
        /// </summary>
        public int ItemsTotal { get; set; } = 0;

        /// <summary>
        /// Items succeeded.
        /// </summary>
        public int ItemsSucceeded { get; set; } = 0;

        /// <summary>
        /// Items failed without retry.
        /// </summary>
        public int ItemsFailed { get; set; } = 0;

        /// <summary>
        /// Items dead-lettered.
        /// </summary>
        public int ItemsDead { get; set; } = 0;

        /// <summary>
        /// Items skipped.
        /// </summary>
        public int ItemsSkipped { get; set; } = 0;

        /// <summary>
        /// Cost of the run in micro-dollars.
        /// </summary>
        public long CostMicros { get; set; } = 0;

        /// <summary>
        /// Handler-specific parameters (JSON) given when the run was queued, or null.
        /// </summary>
        public string? Parameters { get; set; } = null;

        /// <summary>
        /// Handler-written summary (JSON) recorded when the run finished, or null.
        /// </summary>
        public string? Summary { get; set; } = null;

        #endregion
    }
}
