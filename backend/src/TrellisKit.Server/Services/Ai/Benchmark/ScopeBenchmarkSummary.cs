namespace TrellisKit.Server.Services.Ai.Benchmark
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The summary of an ai.scopeBenchmark run (stored as the run's summary JSON): provenance, pass rates per category,
    /// the prompt versions measured, and whether the run clears the publish gate.
    /// </summary>
    public class ScopeBenchmarkSummary
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
        /// True for a dry run (deterministic layers only; never clears the publish gate).
        /// </summary>
        public bool DryRun { get; set; } = false;

        /// <summary>
        /// Parameters of the run. Never null.
        /// </summary>
        public ScopeBenchmarkParameters Parameters { get; set; } = new ScopeBenchmarkParameters();

        /// <summary>
        /// Prompt versions measured. Never null.
        /// </summary>
        public List<ScopePromptVersion> Prompts { get; set; } = new List<ScopePromptVersion>();

        /// <summary>
        /// Cases in the dataset.
        /// </summary>
        public int Total { get; set; } = 0;

        /// <summary>
        /// Cases that passed.
        /// </summary>
        public int Passed { get; set; } = 0;

        /// <summary>
        /// Cases without a result (errors, skipped, or dead items).
        /// </summary>
        public int Errors { get; set; } = 0;

        /// <summary>
        /// Passed over total (0 to 1); errors count as failures.
        /// </summary>
        public double PassRate { get; set; } = 0;

        /// <summary>
        /// True when every case has a result. An incomplete run is not comparable and never clears the gate.
        /// </summary>
        public bool Complete { get; set; } = false;

        /// <summary>
        /// True when the run is complete, live, and meets every live threshold.
        /// </summary>
        public bool GatePassed { get; set; } = false;

        /// <summary>
        /// Per category. Never null.
        /// </summary>
        public List<ScopeLiveCategoryScore> Categories { get; set; } = new List<ScopeLiveCategoryScore>();

        /// <summary>
        /// Thresholds that failed, in words. Never null.
        /// </summary>
        public List<string> Failures { get; set; } = new List<string>();

        /// <summary>
        /// Up to 50 failed cases with their reasons. Never null.
        /// </summary>
        public List<string> Misses { get; set; } = new List<string>();

        #endregion
    }
}
