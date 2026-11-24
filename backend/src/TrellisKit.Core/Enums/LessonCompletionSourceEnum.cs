namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// How a lesson was completed.
    /// </summary>
    public enum LessonCompletionSourceEnum
    {
        /// <summary>
        /// Progress reached library.completionThresholdPercent.
        /// </summary>
        Threshold,

        /// <summary>
        /// The member marked it complete.
        /// </summary>
        Manual
    }
}
