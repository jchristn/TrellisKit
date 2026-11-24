namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Where library levels come from (docs/PHASE4_CONTRACT.md section 0.2).
    /// </summary>
    public enum LibraryLevelSourceEnum
    {
        /// <summary>
        /// No levels.
        /// </summary>
        None,

        /// <summary>
        /// The options of a Rank or SingleSelect profile field, in their order.
        /// </summary>
        ProfileField,

        /// <summary>
        /// The admin-defined level list (librarylevels).
        /// </summary>
        Custom
    }
}
