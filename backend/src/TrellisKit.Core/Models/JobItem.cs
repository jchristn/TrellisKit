namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One unit of work of a job run (for example one map cell or one website), retried with backoff and
    /// dead-lettered after its last attempt (docs/INTEGRATION.md).
    /// </summary>
    public class JobItem
    {
        #region Public-Members

        /// <summary>
        /// Identifier (jbi_...).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Run identifier.
        /// </summary>
        public string RunId { get; set; } = String.Empty;

        /// <summary>
        /// Handler key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// The item's target, unique within the run (for example a cell id or a URL).
        /// </summary>
        public string TargetKey { get; set; } = String.Empty;

        /// <summary>
        /// Handler input (JSON), or null.
        /// </summary>
        public string? Payload { get; set; } = null;

        /// <summary>
        /// Status.
        /// </summary>
        public JobItemStatusEnum Status { get; set; } = JobItemStatusEnum.Pending;

        /// <summary>
        /// Attempts made.
        /// </summary>
        public int Attempts { get; set; } = 0;

        /// <summary>
        /// Attempts allowed.
        /// </summary>
        public int MaxAttempts { get; set; } = 3;

        /// <summary>
        /// Earliest UTC time of the next attempt.
        /// </summary>
        public DateTime NextAttemptUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Claim expiry while running, or null.
        /// </summary>
        public DateTime? LockedUntilUtc { get; set; } = null;

        /// <summary>
        /// Worker holding the claim, or null.
        /// </summary>
        public string? WorkerId { get; set; } = null;

        /// <summary>
        /// Whether the item needs a CLI agent harness.
        /// </summary>
        public bool RequiresHarness { get; set; } = false;

        /// <summary>
        /// Handler output (JSON), or null.
        /// </summary>
        public string? Result { get; set; } = null;

        /// <summary>
        /// Last error, or null.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Cost in micro-dollars across attempts.
        /// </summary>
        public long CostMicros { get; set; } = 0;

        /// <summary>
        /// UTC creation time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC time of the last attempt start, or null.
        /// </summary>
        public DateTime? StartedUtc { get; set; } = null;

        /// <summary>
        /// UTC time finished, or null.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        #endregion
    }
}
