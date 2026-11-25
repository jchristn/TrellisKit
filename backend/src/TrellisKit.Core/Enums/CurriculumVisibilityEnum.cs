namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Who sees a published curriculum.
    /// </summary>
    public enum CurriculumVisibilityEnum
    {
        /// <summary>
        /// Signed-in members see it in lists and by identifier.
        /// </summary>
        Members,

        /// <summary>
        /// Every caller of the curriculum routes sees it in lists and by identifier.
        /// </summary>
        Public,

        /// <summary>
        /// Not listed; members open it only by identifier.
        /// </summary>
        Hidden
    }
}
