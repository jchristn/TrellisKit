namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// What the caller may do on a thread (docs/PHASE3_CONTRACT.md section 1.4).
    /// </summary>
    public class ThreadViewer
    {
        #region Public-Members

        /// <summary>
        /// Edit the thread.
        /// </summary>
        public bool CanEdit { get; set; } = false;

        /// <summary>
        /// Delete the thread.
        /// </summary>
        public bool CanDelete { get; set; } = false;

        /// <summary>
        /// Comment and reply.
        /// </summary>
        public bool CanComment { get; set; } = false;

        /// <summary>
        /// React (false on locked threads, resolved decision 14).
        /// </summary>
        public bool CanReact { get; set; } = false;

        /// <summary>
        /// Report the thread.
        /// </summary>
        public bool CanReport { get; set; } = false;

        /// <summary>
        /// Moderate (hide, remove, restore).
        /// </summary>
        public bool CanModerate { get; set; } = false;

        /// <summary>
        /// Pin or unpin.
        /// </summary>
        public bool CanPin { get; set; } = false;

        /// <summary>
        /// Lock or unlock.
        /// </summary>
        public bool CanLock { get; set; } = false;

        /// <summary>
        /// Move to another forum.
        /// </summary>
        public bool CanMove { get; set; } = false;

        /// <summary>
        /// Accept or clear the answer.
        /// </summary>
        public bool CanAcceptAnswer { get; set; } = false;

        /// <summary>
        /// Vote in the poll.
        /// </summary>
        public bool CanVote { get; set; } = false;

        /// <summary>
        /// Close the poll early.
        /// </summary>
        public bool CanClosePoll { get; set; } = false;

        /// <summary>
        /// Change the review status.
        /// </summary>
        public bool CanSetFeedbackStatus { get; set; } = false;

        /// <summary>
        /// Promote to the feed.
        /// </summary>
        public bool CanPromote { get; set; } = false;

        /// <summary>
        /// Follow or unfollow.
        /// </summary>
        public bool CanFollow { get; set; } = false;

        /// <summary>
        /// Attach media.
        /// </summary>
        public bool CanAttachMedia { get; set; } = false;

        /// <summary>
        /// Attach video.
        /// </summary>
        public bool CanAttachVideo { get; set; } = false;

        #endregion
    }
}
