namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Lifecycle status of a curriculum.
    /// </summary>
    public enum CurriculumStatusEnum
    {
        /// <summary>
        /// Being edited; visible to curriculum staff only.
        /// </summary>
        Draft,

        /// <summary>
        /// Visible to members by its visibility.
        /// </summary>
        Published,

        /// <summary>
        /// Retired; visible to curriculum staff only; member progress is kept.
        /// </summary>
        Archived
    }
}
