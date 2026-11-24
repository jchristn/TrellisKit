namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Progress filter of library enumerations.
    /// </summary>
    public enum LessonProgressStateEnum
    {
        /// <summary>
        /// The caller has no progress on the lesson (or no lesson of the series).
        /// </summary>
        NotStarted,

        /// <summary>
        /// The caller has progress but has not completed it.
        /// </summary>
        InProgress,

        /// <summary>
        /// The caller completed the lesson (or every published lesson of the series).
        /// </summary>
        Completed
    }
}
