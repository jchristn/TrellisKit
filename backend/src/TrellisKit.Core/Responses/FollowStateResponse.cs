namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A thread follow state.
    /// </summary>
    public class FollowStateResponse
    {
        #region Public-Members

        /// <summary>
        /// Whether the caller follows the thread.
        /// </summary>
        public bool IsFollowed { get; set; } = false;

        #endregion
    }
}
