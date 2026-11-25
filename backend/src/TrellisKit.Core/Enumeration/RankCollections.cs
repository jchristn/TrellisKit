namespace TrellisKit.Core.Enumeration
{
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Bounded verified rank enumerations (docs/TRELLISKIT.md, Ranks, curricula, and attendance).
    /// </summary>
    public static class RankCollections
    {
        #region Public-Members

        /// <summary>
        /// Rank awards and revokes (rank awarders and SystemAdmin), newest first.
        /// </summary>
        public static readonly EnumerationCollection Promotions = new EnumerationCollection("rank-awards", TargetTypeEnum.RankPromotion, "p.id", "p.createdutc", true)
            .Search("p.note", "p.reason", "p.optionkey")
            .Filters("userId", "fieldKey", "optionKey", "actorId", "action", "status", "fromUtc", "toUtc")
            .DefaultTo("CreatedDescending");

        /// <summary>
        /// Rank requirements of one Rank field.
        /// </summary>
        public static readonly EnumerationCollection Requirements = new EnumerationCollection("rank-requirements", TargetTypeEnum.RankRequirement, "q.id", "q.createdutc", true)
            .Order("PositionAscending", "q.position", false, EnumerationKeyKindEnum.Number)
            .Filters("fieldKey", "optionKey")
            .DefaultTo("PositionAscending");

        /// <summary>
        /// Members who hold a value in a Rank field, with their next rank and requirement progress (labels and tags are
        /// the member's).
        /// </summary>
        public static readonly EnumerationCollection Eligibility = new EnumerationCollection("rank-eligibility", TargetTypeEnum.User, "u.id", "u.createdutc", true)
            .Order("NameAscending", "lower(u.displayname)", false, EnumerationKeyKindEnum.Text)
            .Search("u.displayname", "u.handle")
            .Filters("fieldKey", "optionKey", "eligibleOnly")
            .DefaultTo("NameAscending");

        #endregion
    }
}
