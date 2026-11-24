namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Purpose of an uploaded media item (docs/PHASE5_CONTRACT.md sections 3.7 and 6.1). Private purposes are never
    /// served through the member-wide media grant cookie.
    /// </summary>
    public enum MediaPurposeEnum
    {
        /// <summary>
        /// Posts, comments, threads, and other community content (the default; served through the grant cookie).
        /// </summary>
        Content,

        /// <summary>
        /// A support ticket attachment; private to the requester and support staff.
        /// </summary>
        Ticket,

        /// <summary>
        /// A direct message attachment; private to the conversation members.
        /// </summary>
        Message
    }
}
