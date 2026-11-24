namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// What the caller may do in a conversation (docs/PHASE5_CONTRACT.md section 1.4).
    /// </summary>
    public class ConversationViewer
    {
        #region Public-Members

        /// <summary>
        /// May send.
        /// </summary>
        public bool CanSend { get; set; } = false;

        /// <summary>
        /// May attach images.
        /// </summary>
        public bool CanAttachMedia { get; set; } = false;

        /// <summary>
        /// May attach a video.
        /// </summary>
        public bool CanAttachVideo { get; set; } = false;

        /// <summary>
        /// May report.
        /// </summary>
        public bool CanReport { get; set; } = false;

        /// <summary>
        /// May block the other member.
        /// </summary>
        public bool CanBlock { get; set; } = false;

        /// <summary>
        /// May accept a request.
        /// </summary>
        public bool CanAccept { get; set; } = false;

        /// <summary>
        /// May decline a request.
        /// </summary>
        public bool CanDecline { get; set; } = false;

        /// <summary>
        /// Why sending is not possible, or null.
        /// </summary>
        public MessagingCannotSendReasonEnum? CannotSendReason { get; set; } = null;

        #endregion
    }
}
