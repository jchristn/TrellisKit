namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// Filters of the attendance enumerations.
    /// </summary>
    public class AttendanceFilter
    {
        #region Public-Members

        /// <summary>
        /// Member.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Target type.
        /// </summary>
        public TrellisKit.Core.Enums.AttendanceTargetTypeEnum? TargetType { get; set; } = null;

        /// <summary>
        /// Target identifier.
        /// </summary>
        public string? TargetId { get; set; } = null;

        /// <summary>
        /// Occurrence date (yyyy-MM-dd).
        /// </summary>
        public string? OccurrenceDate { get; set; } = null;

        #endregion
    }
}
