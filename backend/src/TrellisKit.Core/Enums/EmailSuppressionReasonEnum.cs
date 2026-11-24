namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Why an address is suppressed.
    /// </summary>
    public enum EmailSuppressionReasonEnum
    {
        /// <summary>
        /// Permanent delivery failure.
        /// </summary>
        Bounce,

        /// <summary>
        /// The recipient reported the message as spam.
        /// </summary>
        Complaint,

        /// <summary>
        /// The recipient unsubscribed at the provider.
        /// </summary>
        Unsubscribed,

        /// <summary>
        /// Added by staff.
        /// </summary>
        Manual
    }
}
