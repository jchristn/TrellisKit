namespace TrellisKit.Server.Services.Ai.Benchmark
{
    using System;

    /// <summary>
    /// How one category did in a live scope benchmark run.
    /// </summary>
    public class ScopeLiveCategoryScore
    {
        #region Public-Members

        /// <summary>
        /// Category.
        /// </summary>
        public string Category { get; set; } = String.Empty;

        /// <summary>
        /// Cases in the category (errored cases included; they count as failures).
        /// </summary>
        public int Cases { get; set; } = 0;

        /// <summary>
        /// Cases that met their expected outcome.
        /// </summary>
        public int Passed { get; set; } = 0;

        /// <summary>
        /// Cases without a result (errors).
        /// </summary>
        public int Errors { get; set; } = 0;

        /// <summary>
        /// Passed over cases (0 to 1).
        /// </summary>
        public double PassRate { get; set; } = 0;

        /// <summary>
        /// Holdout cases that passed, over holdout cases, as text (for example 12/14).
        /// </summary>
        public string Holdout { get; set; } = "0/0";

        /// <summary>
        /// Minimum pass rate of the category, or null when it has none of its own.
        /// </summary>
        public double? Minimum { get; set; } = null;

        /// <summary>
        /// Cases the classifier saw.
        /// </summary>
        public int ClassifierCases { get; set; } = 0;

        /// <summary>
        /// Cases where the classifier did what the case expects.
        /// </summary>
        public int ClassifierCorrect { get; set; } = 0;

        /// <summary>
        /// Cases whose first reply the post-call check rejected.
        /// </summary>
        public int Retried { get; set; } = 0;

        #endregion
    }
}
