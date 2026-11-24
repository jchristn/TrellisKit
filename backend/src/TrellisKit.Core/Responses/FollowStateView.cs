namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Follow state of a series.
    /// </summary>
    public class FollowStateView
    {
        #region Public-Members

        /// <summary>
        /// Whether the caller follows the series.
        /// </summary>
        public bool IsFollowed { get; set; } = false;

        #endregion
    }
}
