namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Permission flags and mute state.
    /// </summary>
    public class MemberFlags
    {
        #region Public-Members

        /// <summary>
        /// May create feed posts.
        /// </summary>
        public bool CanPostToFeed { get; set; } = false;

        /// <summary>
        /// May promote threads.
        /// </summary>
        public bool CanPromoteToFeed { get; set; } = false;

        /// <summary>
        /// May attach video.
        /// </summary>
        public bool CanUploadVideo { get; set; } = true;

        /// <summary>
        /// Whether the member may post at all (feed posts with canPostToFeed, comments, replies, and edits). Default true.
        /// </summary>
        public bool CanPost { get; set; } = true;

        /// <summary>
        /// Whether the member may upload and attach media. Default true.
        /// </summary>
        public bool CanPostMedia { get; set; } = true;

        /// <summary>
        /// May send direct messages.
        /// </summary>
        public bool CanSendDirectMessages { get; set; } = true;

        /// <summary>
        /// Currently muted.
        /// </summary>
        public bool IsMuted { get; set; } = false;

        /// <summary>
        /// Mute end, or null.
        /// </summary>
        public DateTime? MutedUntilUtc { get; set; } = null;

        #endregion
    }
}
