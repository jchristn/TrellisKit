namespace TrellisKit.Core.Enumeration
{
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Bounded attendance enumerations (docs/TRELLISKIT.md, Ranks, curricula, and attendance).
    /// </summary>
    public static class AttendanceCollections
    {
        #region Public-Members

        /// <summary>
        /// Every check-in (rank awarders and SystemAdmin), newest check-in first.
        /// </summary>
        public static readonly EnumerationCollection Roster = new EnumerationCollection("attendance", TargetTypeEnum.Attendance, "a.id", "a.createdutc", true)
            .Order("CheckedInDescending", "a.checkedinutc", true, EnumerationKeyKindEnum.Time)
            .Order("CheckedInAscending", "a.checkedinutc", false, EnumerationKeyKindEnum.Time)
            .Filters("targetType", "targetId", "occurrenceDate", "userId")
            .DefaultTo("CheckedInDescending");

        /// <summary>
        /// The caller's own check-ins, newest first.
        /// </summary>
        public static readonly EnumerationCollection Mine = new EnumerationCollection("my-attendance", TargetTypeEnum.Attendance, "a.id", "a.createdutc", false)
            .Order("CheckedInDescending", "a.checkedinutc", true, EnumerationKeyKindEnum.Time)
            .Filters("targetType", "targetId", "occurrenceDate")
            .DefaultTo("CheckedInDescending");

        #endregion
    }
}
