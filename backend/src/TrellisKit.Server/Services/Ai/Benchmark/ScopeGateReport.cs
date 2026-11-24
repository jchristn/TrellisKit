namespace TrellisKit.Server.Services.Ai.Benchmark
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Result of the deterministic scope gate: the detector and post-call check over every case and reply probe of the
    /// benchmark, scored against the set's deterministic thresholds.
    /// </summary>
    public class ScopeGateReport
    {
        #region Public-Members

        /// <summary>
        /// Dataset name.
        /// </summary>
        public string Dataset { get; set; } = String.Empty;

        /// <summary>
        /// Dataset version.
        /// </summary>
        public int DatasetVersion { get; set; } = 0;

        /// <summary>
        /// Dataset SHA-256.
        /// </summary>
        public string DatasetSha256 { get; set; } = String.Empty;

        /// <summary>
        /// Property overlay merged into the dataset ("name vN"), or null.
        /// </summary>
        public string? DatasetOverlay { get; set; } = null;

        /// <summary>
        /// True when every threshold holds.
        /// </summary>
        public bool Passed { get; set; } = false;

        /// <summary>
        /// Cases expecting detector Allow that the detector refused.
        /// </summary>
        public int DetectorFalsePositives { get; set; } = 0;

        /// <summary>
        /// Share of CodeRequest cases refused (0 to 1).
        /// </summary>
        public double CodeRequestRecall { get; set; } = 0;

        /// <summary>
        /// Share of CodeReply and LeakReply probes rejected (0 to 1).
        /// </summary>
        public double OutputRejectRecall { get; set; } = 0;

        /// <summary>
        /// CleanReply probes rejected.
        /// </summary>
        public int OutputFalsePositives { get; set; } = 0;

        /// <summary>
        /// Per category and layer. Never null.
        /// </summary>
        public List<ScopeLayerScore> Scores { get; set; } = new List<ScopeLayerScore>();

        /// <summary>
        /// Thresholds that failed, in words. Never null.
        /// </summary>
        public List<string> Failures { get; set; } = new List<string>();

        /// <summary>
        /// Cases and probes the layers got wrong (identifier, split, and what happened). Never null.
        /// </summary>
        public List<string> Misses { get; set; } = new List<string>();

        #endregion
    }
}
