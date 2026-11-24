namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Admin media library item (contract section 10.3).
    /// </summary>
    public class AdminMediaItem
    {
        #region Public-Members

        /// <summary>
        /// Media payload (with labels and tags, including staff entries).
        /// </summary>
        public MediaView Media { get; set; } = new MediaView();

        /// <summary>
        /// Owner.
        /// </summary>
        public UserSummary Owner { get; set; } = new UserSummary();

        /// <summary>
        /// Content the media is attached to, or null.
        /// </summary>
        public MediaTargetView? AttachedTo { get; set; } = null;

        /// <summary>
        /// Bytes of the original plus image variants.
        /// </summary>
        public long StoredBytes { get; set; } = 0;

        /// <summary>
        /// Original file name, or null.
        /// </summary>
        public string? FileName { get; set; } = null;

        /// <summary>
        /// Creation time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
