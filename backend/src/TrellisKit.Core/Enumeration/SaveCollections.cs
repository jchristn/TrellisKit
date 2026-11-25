namespace TrellisKit.Core.Enumeration
{
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Bounded enumerations of a member's saves and save collections (REST_API.md, Saves).
    /// </summary>
    public static class SaveCollections
    {
        /// <summary>
        /// The caller's saves (alias f). Orderings CreatedDescending (default), CreatedAscending, UpdatedDescending.
        /// Filters targetTypes, collectionId. Search covers the note.
        /// </summary>
        public static readonly EnumerationCollection Saves = new EnumerationCollection("me-saves", TargetTypeEnum.Favorite, "f.id", "f.createdutc", false)
            .Order("UpdatedDescending", "f.lastupdateutc", true, EnumerationKeyKindEnum.Time)
            .Search("f.note")
            .Filters("targetTypes", "collectionId")
            .DefaultTo("CreatedDescending");

        /// <summary>
        /// The caller's save collections (alias c). Orderings PositionAscending (default), NameAscending,
        /// CreatedDescending, CreatedAscending. Search covers the name.
        /// </summary>
        public static readonly EnumerationCollection Collections = new EnumerationCollection("me-save-collections", TargetTypeEnum.FavoriteCollection, "c.id", "c.createdutc", false)
            .Order("PositionAscending", "c.position", false, EnumerationKeyKindEnum.Number)
            .Order("NameAscending", "lower(c.name)", false, EnumerationKeyKindEnum.Text)
            .Search("c.name")
            .DefaultTo("PositionAscending");
    }
}
