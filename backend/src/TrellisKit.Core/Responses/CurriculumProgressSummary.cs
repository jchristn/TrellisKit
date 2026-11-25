namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Completion counts of a curriculum (optionally of one rank group) for one member.
    /// </summary>
    public class CurriculumProgressSummary
    {
        #region Public-Members

        /// <summary>
        /// Curriculum.
        /// </summary>
        public string? CurriculumId { get; set; } = null;

        /// <summary>
        /// Curriculum name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Rank group the counts are limited to, or null.
        /// </summary>
        public string? OptionKey { get; set; } = null;

        /// <summary>
        /// Items.
        /// </summary>
        public int Total { get; set; } = 0;

        /// <summary>
        /// Required items.
        /// </summary>
        public int Required { get; set; } = 0;

        /// <summary>
        /// Satisfied items.
        /// </summary>
        public int Completed { get; set; } = 0;

        /// <summary>
        /// Satisfied required items.
        /// </summary>
        public int CompletedRequired { get; set; } = 0;

        /// <summary>
        /// Completed share of the items (0 to 100).
        /// </summary>
        public int Percent { get; set; } = 0;

        #endregion
    }
}
