namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// How a known contact relation was created (docs/PHASE5_CONTRACT.md section 16).
    /// </summary>
    public enum MessagingContactSourceEnum
    {
        /// <summary>
        /// The member sent a message to the contact.
        /// </summary>
        Messaged,

        /// <summary>
        /// The member accepted a request from the contact.
        /// </summary>
        Accepted
    }
}
