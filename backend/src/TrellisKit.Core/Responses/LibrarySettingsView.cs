namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// GET /api/v1/admin/library/settings.
    /// </summary>
    public class LibrarySettingsView
    {
        #region Public-Members

        /// <summary>
        /// Level source.
        /// </summary>
        public LibraryLevelSourceEnum LevelSource { get; set; } = LibraryLevelSourceEnum.None;

        /// <summary>
        /// Profile field key (ProfileField source).
        /// </summary>
        public string? LevelFieldKey { get; set; } = null;

        /// <summary>
        /// library.downloads.enabled.
        /// </summary>
        public bool DownloadsEnabled { get; set; } = false;

        /// <summary>
        /// library.completionThresholdPercent.
        /// </summary>
        public int CompletionThresholdPercent { get; set; } = 90;

        /// <summary>
        /// library.publicSamplesEnabled.
        /// </summary>
        public bool PublicSamplesEnabled { get; set; } = true;

        /// <summary>
        /// library.autoFollowOnProgress.
        /// </summary>
        public bool AutoFollowOnProgress { get; set; } = true;

        /// <summary>
        /// library.commentsEnabledByDefault.
        /// </summary>
        public bool CommentsEnabledByDefault { get; set; } = true;

        /// <summary>
        /// Honest limits of downloads, shown where downloads are enabled.
        /// </summary>
        public string DownloadsNotice { get; set; } = String.Empty;

        /// <summary>
        /// Last change of the stored configuration.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
