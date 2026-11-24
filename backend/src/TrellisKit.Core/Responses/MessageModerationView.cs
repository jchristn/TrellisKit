namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A reported direct message as moderators see it: the frozen snapshot copy and the live state (never the conversation).
    /// </summary>
    public class MessageModerationView
    {
        #region Public-Members

        /// <summary>
        /// Message or Conversation.
        /// </summary>
        public TargetTypeEnum TargetType { get; set; } = TargetTypeEnum.Message;

        /// <summary>
        /// Target identifier.
        /// </summary>
        public string TargetId { get; set; } = String.Empty;

        /// <summary>
        /// Author, or null.
        /// </summary>
        public UserSummary? Author { get; set; } = null;

        /// <summary>
        /// Text in the latest snapshot.
        /// </summary>
        public string SnapshotBody { get; set; } = String.Empty;

        /// <summary>
        /// State in the latest snapshot.
        /// </summary>
        public MessageStateEnum SnapshotState { get; set; } = MessageStateEnum.Visible;

        /// <summary>
        /// Current state of the live message, or null when purged.
        /// </summary>
        public MessageStateEnum? CurrentState { get; set; } = null;

        /// <summary>
        /// Sequence number, or null.
        /// </summary>
        public long? CapturedSeq { get; set; } = null;

        #endregion
    }
}
