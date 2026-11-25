namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Progress state of a self-completed or verified curriculum item.
    /// </summary>
    public enum CurriculumProgressStatusEnum
    {
        /// <summary>
        /// The member marked the item complete.
        /// </summary>
        SelfComplete,

        /// <summary>
        /// A rank awarder or SystemAdmin verified the item.
        /// </summary>
        Verified
    }
}
