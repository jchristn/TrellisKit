namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// POST /api/v1/admin/moderation/actions result: { action, target }.
    /// </summary>
    public class ModerationActionResult
    {
        #region Public-Members

        /// <summary>
        /// The action.
        /// </summary>
        public ModerationActionView Action { get; set; } = new ModerationActionView();

        /// <summary>
        /// The target as moderators see it (FeedPost or Comment).
        /// </summary>
        public object? Target { get; set; } = null;

        #endregion
    }
}
