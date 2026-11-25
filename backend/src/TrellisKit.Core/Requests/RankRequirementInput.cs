namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One requirement in PUT /api/v1/admin/ranks/requirements.
    /// </summary>
    public class RankRequirementInput
    {
        #region Public-Members

        /// <summary>
        /// Option the requirement leads to.
        /// </summary>
        public string? OptionKey { get; set; } = null;

        /// <summary>
        /// Level, or null for every level of the option without its own row.
        /// </summary>
        public int? Level { get; set; } = null;

        /// <summary>
        /// Days in grade (0 to 3650).
        /// </summary>
        public int MinimumDaysInGrade { get; set; } = 0;

        /// <summary>
        /// Check-ins in grade (0 to 10000).
        /// </summary>
        public int MinimumAttendance { get; set; } = 0;

        /// <summary>
        /// Notes (at most 1000 characters).
        /// </summary>
        public string? Notes { get; set; } = null;

        /// <summary>
        /// Curriculum, or null.
        /// </summary>
        public string? CurriculumId { get; set; } = null;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
