namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// How serious a possible Terms of Service violation is, as judged by the reviewer.
    /// </summary>
    public enum ContentReviewSeverityEnum
    {
        /// <summary>
        /// No violation.
        /// </summary>
        None,

        /// <summary>
        /// Minor or borderline.
        /// </summary>
        Low,

        /// <summary>
        /// A clear violation that is not urgent.
        /// </summary>
        Medium,

        /// <summary>
        /// A serious violation to act on soon.
        /// </summary>
        High,

        /// <summary>
        /// Act now (threats, exploitation, imminent harm).
        /// </summary>
        Critical
    }
}
