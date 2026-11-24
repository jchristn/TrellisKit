namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Outcome of one category email to one member.
    /// </summary>
    public enum EmailSendStatusEnum
    {
        /// <summary>
        /// Delivered to the email sender.
        /// </summary>
        Sent,

        /// <summary>
        /// Not sent (preference, interval, or eligibility).
        /// </summary>
        Skipped,

        /// <summary>
        /// The send failed.
        /// </summary>
        Error
    }
}
