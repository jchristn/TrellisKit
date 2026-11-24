namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Completion rate of a series in the library metrics.
    /// </summary>
    public class SeriesCompletionView
    {
        #region Public-Members

        /// <summary>
        /// Series.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Members with progress.
        /// </summary>
        public long Learners { get; set; } = 0;

        /// <summary>
        /// Completed progress rows divided by progress rows (0 to 1).
        /// </summary>
        public double CompletionRate { get; set; } = 0;

        #endregion
    }
}
