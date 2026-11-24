namespace TrellisKit.Server.Services.Ai.Benchmark
{
    using System;

    /// <summary>
    /// How one deterministic layer did on one category of the scope benchmark.
    /// </summary>
    public class ScopeLayerScore
    {
        #region Public-Members

        /// <summary>
        /// Category.
        /// </summary>
        public string Category { get; set; } = String.Empty;

        /// <summary>
        /// Layer: detector (input) or outputCheck (post-call).
        /// </summary>
        public string Layer { get; set; } = String.Empty;

        /// <summary>
        /// What the category expects of the layer (Block, Allow, Any, Reject, Pass, or Mixed).
        /// </summary>
        public string Expected { get; set; } = String.Empty;

        /// <summary>
        /// Cases in the category.
        /// </summary>
        public int Cases { get; set; } = 0;

        /// <summary>
        /// Cases the layer refused or rejected.
        /// </summary>
        public int Flagged { get; set; } = 0;

        /// <summary>
        /// Cases where the layer did what the case expects (Any always counts).
        /// </summary>
        public int Correct { get; set; } = 0;

        /// <summary>
        /// Tune split cases.
        /// </summary>
        public int TuneCases { get; set; } = 0;

        /// <summary>
        /// Correct tune split cases.
        /// </summary>
        public int TuneCorrect { get; set; } = 0;

        /// <summary>
        /// Holdout split cases.
        /// </summary>
        public int HoldoutCases { get; set; } = 0;

        /// <summary>
        /// Correct holdout split cases.
        /// </summary>
        public int HoldoutCorrect { get; set; } = 0;

        /// <summary>
        /// Share of cases flagged (0 to 1).
        /// </summary>
        public double FlaggedRate
        {
            get { return Cases == 0 ? 0 : Math.Round((double)Flagged / Cases, 4); }
        }

        #endregion
    }
}
