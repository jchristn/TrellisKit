namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.Models;

    /// <summary>
    /// The next rank of a member and how far they are from its requirements.
    /// </summary>
    public class RankNextView
    {
        #region Public-Members

        /// <summary>
        /// Next option key.
        /// </summary>
        public string OptionKey { get; set; } = String.Empty;

        /// <summary>
        /// Next option label.
        /// </summary>
        public string OptionLabel { get; set; } = String.Empty;

        /// <summary>
        /// Next level.
        /// </summary>
        public int Level { get; set; } = 0;

        /// <summary>
        /// Display text.
        /// </summary>
        public string RankLabel { get; set; } = String.Empty;

        /// <summary>
        /// The requirement that applies, or null when none is configured.
        /// </summary>
        public RankRequirement? Requirement { get; set; } = null;

        /// <summary>
        /// Days since the current verified award, or null without one.
        /// </summary>
        public int? DaysInGrade { get; set; } = null;

        /// <summary>
        /// Check-ins since the current verified award (all check-ins without one).
        /// </summary>
        public long Attendance { get; set; } = 0;

        /// <summary>
        /// Whether the time in grade is met.
        /// </summary>
        public bool DaysInGradeMet { get; set; } = true;

        /// <summary>
        /// Whether the attendance is met.
        /// </summary>
        public bool AttendanceMet { get; set; } = true;

        /// <summary>
        /// Required curriculum progress for the next option, or null.
        /// </summary>
        public CurriculumProgressSummary? Curriculum { get; set; } = null;

        /// <summary>
        /// Whether the curriculum is met.
        /// </summary>
        public bool CurriculumMet { get; set; } = true;

        /// <summary>
        /// Whether every configured requirement is met (false when no requirement is configured).
        /// </summary>
        public bool Eligible { get; set; } = false;

        #endregion
    }
}
