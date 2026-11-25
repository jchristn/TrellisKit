namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// What a curriculum item points at.
    /// </summary>
    public enum CurriculumItemTypeEnum
    {
        /// <summary>
        /// A library lesson, satisfied when the member completes it.
        /// </summary>
        Lesson,

        /// <summary>
        /// A library series, satisfied when every published lesson in it is complete.
        /// </summary>
        Series,

        /// <summary>
        /// A catalog entry identifier (stored as text), satisfied by self-completion or verification.
        /// </summary>
        CatalogEntry,

        /// <summary>
        /// Free text, satisfied by self-completion or verification.
        /// </summary>
        Custom
    }
}
