namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One item of a job run.
    /// </summary>
    public class JobItemView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
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
        /// Target key.
        /// </summary>
        public string TargetKey { get; set; } = String.Empty;

        /// <summary>
        /// Input JSON, or null.
        /// </summary>
        public string? Payload { get; set; } = null;

        /// <summary>
        /// Pending, Running, Succeeded, Failed, Dead, or Skipped.
        /// </summary>
        public string Status { get; set; } = "Pending";

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
        /// Whether the item needs a CLI agent harness.
        /// </summary>
        public bool RequiresHarness { get; set; } = false;

        /// <summary>
        /// Output JSON, or null.
        /// </summary>
        public string? Result { get; set; } = null;

        /// <summary>
        /// Last error, or null.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Cost in US dollars.
        /// </summary>
        public double CostUsd { get; set; } = 0;

        /// <summary>
        /// UTC creation time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC time the last attempt started, or null.
        /// </summary>
        public DateTime? StartedUtc { get; set; } = null;

        /// <summary>
        /// UTC time finished, or null.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        /// <inheritdoc />
        public List<string> Labels { get; set; } = new List<string>();

        /// <inheritdoc />
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
