namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// How a property document version was created.
    /// </summary>
    public enum DocumentVersionSourceEnum
    {
        /// <summary>
        /// Seeded from the property folder on the property's first load.
        /// </summary>
        Seed,

        /// <summary>
        /// Published by an administrator.
        /// </summary>
        Edit,

        /// <summary>
        /// The document's version or file changed on disk and replaced the stored version (SingleProperty, the folder wins on change).
        /// </summary>
        DirectorySync
    }
}
