namespace TrellisKit.Core.Enumeration
{
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Bounded curriculum enumerations (docs/TRELLISKIT.md, Ranks, curricula, and attendance).
    /// </summary>
    public static class CurriculumCollections
    {
        #region Public-Members

        /// <summary>
        /// Published curricula a member may list (Members and Public visibility).
        /// </summary>
        public static readonly EnumerationCollection Curricula = Build("curricula", false)
            .Filters("fieldKey")
            .DefaultTo("PositionAscending");

        /// <summary>
        /// Every curriculum in any status (curriculum staff).
        /// </summary>
        public static readonly EnumerationCollection AdminCurricula = Build("admin-curricula", true)
            .Filters("fieldKey", "status", "visibility")
            .DefaultTo("PositionAscending");

        #endregion

        #region Private-Methods

        private static EnumerationCollection Build(string name, bool admin)
        {
            return new EnumerationCollection(name, TargetTypeEnum.Curriculum, "c.id", "c.createdutc", admin)
                .Order("PositionAscending", "c.position", false, EnumerationKeyKindEnum.Number)
                .Order("NameAscending", "lower(c.name)", false, EnumerationKeyKindEnum.Text)
                .Order("UpdatedDescending", "c.updatedutc", true, EnumerationKeyKindEnum.Time)
                .Search("c.name", "c.description");
        }

        #endregion
    }
}
