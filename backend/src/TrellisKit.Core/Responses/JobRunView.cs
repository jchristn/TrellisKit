namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A job run.
    /// </summary>
    public class JobRunView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Handler key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Schedule, Manual, or DryRun.
        /// </summary>
        public string Trigger { get; set; } = "Schedule";

        /// <summary>
        /// Queued, Running, Succeeded, PartiallySucceeded, Failed, Cancelled, or Skipped.
        /// </summary>
        public string Status { get; set; } = "Queued";

        /// <summary>
        /// True for a dry run.
        /// </summary>
        public bool IsDryRun { get; set; } = false;

        /// <summary>
        /// Why the run was skipped, or null.
        /// </summary>
        public string? SkippedReason { get; set; } = null;

        /// <summary>
        /// Error, or null.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Administrator who started it, or null.
        /// </summary>
        public string? RequestedByUserId { get; set; } = null;

        /// <summary>
        /// UTC time queued.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC time started, or null.
        /// </summary>
        public DateTime? StartedUtc { get; set; } = null;

        /// <summary>
        /// UTC time finished, or null.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        /// <summary>
        /// Items planned.
        /// </summary>
        public int ItemsTotal { get; set; } = 0;

        /// <summary>
        /// Items pending or running.
        /// </summary>
        public int ItemsOpen { get; set; } = 0;

        /// <summary>
        /// Items succeeded.
        /// </summary>
        public int ItemsSucceeded { get; set; } = 0;

        /// <summary>
        /// Items failed.
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
        /// Cost in US dollars.
        /// </summary>
        public double CostUsd { get; set; } = 0;

        /// <summary>
        /// Handler-specific parameters (JSON) given when the run was queued, or null.
        /// </summary>
        public string? Parameters { get; set; } = null;

        /// <summary>
        /// Handler-written summary (JSON) recorded when the run finished, or null.
        /// </summary>
        public string? Summary { get; set; } = null;

        /// <inheritdoc />
        public List<string> Labels { get; set; } = new List<string>();

        /// <inheritdoc />
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
