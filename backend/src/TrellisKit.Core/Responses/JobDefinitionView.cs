namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A job definition with its latest run.
    /// </summary>
    public class JobDefinitionView : IMetadataPayload
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
        /// Name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Description.
        /// </summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>
        /// Whether the schedule fires.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Schedule type.
        /// </summary>
        public string IntervalType { get; set; } = "Manual";

        /// <summary>
        /// Interval value.
        /// </summary>
        public int IntervalValue { get; set; } = 1;

        /// <summary>
        /// Minutes after UTC midnight.
        /// </summary>
        public int TimeOfDayMinutesUtc { get; set; } = 0;

        /// <summary>
        /// Day names for DaysOfWeek.
        /// </summary>
        public List<string> DaysOfWeek { get; set; } = new List<string>();

        /// <summary>
        /// Items processed at once.
        /// </summary>
        public int MaxConcurrency { get; set; } = 2;

        /// <summary>
        /// Attempts per item.
        /// </summary>
        public int MaxAttempts { get; set; } = 3;

        /// <summary>
        /// Cost cap per run in US dollars, or null.
        /// </summary>
        public double? BudgetUsdPerRun { get; set; } = null;

        /// <summary>
        /// Whether items need a CLI agent harness.
        /// </summary>
        public bool RequiresHarness { get; set; } = false;

        /// <summary>
        /// Whether the handler honors dry runs.
        /// </summary>
        public bool SupportsDryRun { get; set; } = true;

        /// <summary>
        /// Whether a handler is registered for the key (false for a definition left by a removed handler).
        /// </summary>
        public bool HandlerAvailable { get; set; } = true;

        /// <summary>
        /// Next scheduled UTC time, or null.
        /// </summary>
        public DateTime? NextRunUtc { get; set; } = null;

        /// <summary>
        /// Last scheduled UTC time, or null.
        /// </summary>
        public DateTime? LastRunUtc { get; set; } = null;

        /// <summary>
        /// The newest run, or null.
        /// </summary>
        public JobRunView? LatestRun { get; set; } = null;

        /// <summary>
        /// UTC time of the last change.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Definition creation time.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UnixEpoch;

        /// <inheritdoc />
        public List<string> Labels { get; set; } = new List<string>();

        /// <inheritdoc />
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
