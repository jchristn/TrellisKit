namespace TrellisKit.Core
{
    using System;

    /// <summary>
    /// Identifier prefixes of verified ranks, curricula, and attendance (docs/TRELLISKIT.md, Ranks, curricula, and
    /// attendance).
    /// </summary>
    public static class RankIds
    {
        #region Public-Members

        /// <summary>
        /// Member rank (memberranks).
        /// </summary>
        public const string MemberRank = "mrk_";

        /// <summary>
        /// Award or revoke (rankpromotions).
        /// </summary>
        public const string Promotion = "rkp_";

        /// <summary>
        /// Rank requirement (rankrequirements).
        /// </summary>
        public const string Requirement = "rkq_";

        /// <summary>
        /// Lineage entry (lineageentries).
        /// </summary>
        public const string Lineage = "lin_";

        /// <summary>
        /// Curriculum (curricula).
        /// </summary>
        public const string Curriculum = "cur_";

        /// <summary>
        /// Curriculum item (curriculumitems).
        /// </summary>
        public const string CurriculumItem = "cui_";

        /// <summary>
        /// Curriculum progress (curriculumprogress).
        /// </summary>
        public const string CurriculumProgress = "cup_";

        /// <summary>
        /// Attendance check-in (attendance).
        /// </summary>
        public const string Attendance = "att_";

        #endregion
    }
}
