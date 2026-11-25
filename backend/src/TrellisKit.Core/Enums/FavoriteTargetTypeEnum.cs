namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// What a member can save (REST_API.md, Saves). Each type follows its own feature switch and access rules; a type
    /// whose content does not exist in the engine build (or is turned off) resolves as unavailable.
    /// </summary>
    public enum FavoriteTargetTypeEnum
    {
        /// <summary>
        /// A library lesson.
        /// </summary>
        Lesson,

        /// <summary>
        /// A library series.
        /// </summary>
        Series,

        /// <summary>
        /// A catalog entry (resolved by the catalog's content target provider when the catalog is present and on).
        /// </summary>
        CatalogEntry,

        /// <summary>
        /// A directory venue.
        /// </summary>
        Venue,

        /// <summary>
        /// A directory instructor.
        /// </summary>
        Instructor,

        /// <summary>
        /// An event.
        /// </summary>
        Event,

        /// <summary>
        /// A feed post.
        /// </summary>
        Post,

        /// <summary>
        /// A forum thread.
        /// </summary>
        Thread
    }
}
