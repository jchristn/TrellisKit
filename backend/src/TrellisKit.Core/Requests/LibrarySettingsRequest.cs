namespace TrellisKit.Core.Requests
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Body of PUT /api/v1/admin/library/settings (omitted fields stay).
    /// </summary>
    public class LibrarySettingsRequest
    {
        #region Public-Members

        /// <summary>
        /// Level source.
        /// </summary>
        public LibraryLevelSourceEnum? LevelSource { get; set; } = null;

        /// <summary>
        /// Profile field key (ProfileField source).
        /// </summary>
        public string? LevelFieldKey { get; set; } = null;

        /// <summary>
        /// library.downloads.enabled.
        /// </summary>
        public bool? DownloadsEnabled { get; set; } = null;

        /// <summary>
        /// library.completionThresholdPercent (50 to 100).
        /// </summary>
        public int? CompletionThresholdPercent { get; set; } = null;

        /// <summary>
        /// library.publicSamplesEnabled.
        /// </summary>
        public bool? PublicSamplesEnabled { get; set; } = null;

        /// <summary>
        /// library.autoFollowOnProgress.
        /// </summary>
        public bool? AutoFollowOnProgress { get; set; } = null;

        /// <summary>
        /// library.commentsEnabledByDefault.
        /// </summary>
        public bool? CommentsEnabledByDefault { get; set; } = null;

        #endregion
    }
}
