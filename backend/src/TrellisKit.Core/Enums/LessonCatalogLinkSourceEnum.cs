namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// How a lesson-to-catalog link was created (lessoncatalogentries.source).
    /// </summary>
    public enum LessonCatalogLinkSourceEnum
    {
        /// <summary>
        /// Chosen by a library editor or system administrator.
        /// </summary>
        Editor,

        /// <summary>
        /// Suggested (for example by an import or a generation job) and approved by an editor.
        /// </summary>
        Suggested
    }
}
