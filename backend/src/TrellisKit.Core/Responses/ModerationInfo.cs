namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Moderation state shown to the author and moderators: { state, reason, actedUtc }.
    /// </summary>
    public class ModerationInfo
    {
        #region Public-Members

        /// <summary>
        /// State.
        /// </summary>
        public ModerationStateEnum State { get; set; } = ModerationStateEnum.None;

        /// <summary>
        /// Reason given by the moderator.
        /// </summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// When the state last changed.
        /// </summary>
        public DateTime? ActedUtc { get; set; } = null;

        #endregion
    }
}
