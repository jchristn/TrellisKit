namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body of PUT /api/v1/admin/members/{id}/flags. Only present values change.
    /// </summary>
    public class MemberFlagsRequest
    {
        #region Public-Members

        /// <summary>
        /// May create feed posts.
        /// </summary>
        public bool? CanPostToFeed { get; set; } = null;

        /// <summary>
        /// May promote threads to the feed.
        /// </summary>
        public bool? CanPromoteToFeed { get; set; } = null;

        /// <summary>
        /// May attach video.
        /// </summary>
        public bool? CanUploadVideo { get; set; } = null;

        /// <summary>
        /// New canPost value, or null to leave it.
        /// </summary>
        public bool? CanPost { get; set; } = null;

        /// <summary>
        /// New canPostMedia value, or null to leave it.
        /// </summary>
        public bool? CanPostMedia { get; set; } = null;

        /// <summary>
        /// May send direct messages.
        /// </summary>
        public bool? CanSendDirectMessages { get; set; } = null;

        #endregion
    }
}
