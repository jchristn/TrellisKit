namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// Completion rate of one series (aggregates only).
    /// </summary>
    public class SeriesCompletionRow
    {
        #region Public-Members

        /// <summary>
        /// Series.
        /// </summary>
        public string SeriesId { get; set; } = String.Empty;

        /// <summary>
        /// Members with progress on a lesson of the series.
        /// </summary>
        public long Learners { get; set; } = 0;

        /// <summary>
        /// Completed lesson progress rows.
        /// </summary>
        public long Completions { get; set; } = 0;

        /// <summary>
        /// Lesson progress rows.
        /// </summary>
        public long ProgressRows { get; set; } = 0;

        #endregion
    }
}
