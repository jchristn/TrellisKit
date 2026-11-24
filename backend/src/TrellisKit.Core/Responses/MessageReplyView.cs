namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The quoted message of a reply; follows the quoted message's current state.
    /// </summary>
    public class MessageReplyView
    {
        #region Public-Members

        /// <summary>
        /// Quoted message identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Sequence number.
        /// </summary>
        public long Seq { get; set; } = 0;

        /// <summary>
        /// Excerpt (empty when deleted or removed).
        /// </summary>
        public string Excerpt { get; set; } = String.Empty;

        /// <summary>
        /// Author display name ("Deleted member" after account deletion).
        /// </summary>
        public string AuthorDisplayName { get; set; } = String.Empty;

        /// <summary>
        /// Whether the quoted message is deleted or removed.
        /// </summary>
        public bool IsDeleted { get; set; } = false;

        /// <summary>
        /// Quoted message state.
        /// </summary>
        public MessageStateEnum State { get; set; } = MessageStateEnum.Visible;

        #endregion
    }
}
