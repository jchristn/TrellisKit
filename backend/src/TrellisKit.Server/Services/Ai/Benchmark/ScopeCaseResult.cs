namespace TrellisKit.Server.Services.Ai.Benchmark
{
    using System;

    /// <summary>
    /// What happened to one benchmark case in a live run (stored as the job item's result).
    /// </summary>
    public class ScopeCaseResult
    {
        #region Public-Members

        /// <summary>
        /// Case identifier.
        /// </summary>
        public string CaseId { get; set; } = String.Empty;

        /// <summary>
        /// Category.
        /// </summary>
        public string Category { get; set; } = String.Empty;

        /// <summary>
        /// tune or holdout.
        /// </summary>
        public string Split { get; set; } = ScopeBenchmark.Tune;

        /// <summary>
        /// Expected end-to-end outcome.
        /// </summary>
        public string Expected { get; set; } = String.Empty;

        /// <summary>
        /// True when the case met its expected outcome.
        /// </summary>
        public bool Passed { get; set; } = false;

        /// <summary>
        /// Why the case failed, or null.
        /// </summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// True for a dry run (deterministic layers only).
        /// </summary>
        public bool DryRun { get; set; } = false;

        /// <summary>
        /// Detector verdict: Block or Allow.
        /// </summary>
        public string Detector { get; set; } = ScopeBenchmark.Allow;

        /// <summary>
        /// Whether the detector did what the case expects (Any always counts).
        /// </summary>
        public bool DetectorCorrect { get; set; } = true;

        /// <summary>
        /// Classifier verdict (Allow, Flag, Block), or null when it did not run.
        /// </summary>
        public string? Classifier { get; set; } = null;

        /// <summary>
        /// Classifier category, or null.
        /// </summary>
        public string? ClassifierCategory { get; set; } = null;

        /// <summary>
        /// Whether the classifier did what the case expects, or null when it did not run.
        /// </summary>
        public bool? ClassifierCorrect { get; set; } = null;

        /// <summary>
        /// Classifier path: role (the Moderation role) or assistant (an assistant's moderation endpoint), or null.
        /// </summary>
        public string? ClassifierPath { get; set; } = null;

        /// <summary>
        /// Whether the post-call check rejected the first reply and asked again.
        /// </summary>
        public bool Retried { get; set; } = false;

        /// <summary>
        /// Post-call rejection reason of the final attempt (code, rules), or null.
        /// </summary>
        public string? PostCheck { get; set; } = null;

        /// <summary>
        /// Output moderation verdict, or null when it did not run.
        /// </summary>
        public string? OutputModeration { get; set; } = null;

        /// <summary>
        /// Layer that refused (detector, classifier, postCheck, outputModeration), or null when a reply was delivered.
        /// </summary>
        public string? DeclinedBy { get; set; } = null;

        /// <summary>
        /// Whether the delivered reply reads as a decline (heuristic).
        /// </summary>
        public bool LooksDeclined { get; set; } = false;

        /// <summary>
        /// Forbidden pattern the delivered reply matched, or null.
        /// </summary>
        public string? ForbidHit { get; set; } = null;

        /// <summary>
        /// The first 400 characters of what the member would see.
        /// </summary>
        public string Excerpt { get; set; } = String.Empty;

        #endregion
    }
}
