namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The library configuration of a property (libraryconfig), seeded from property.json section library.
    /// </summary>
    public class LibraryConfig
    {
        #region Public-Members

        /// <summary>
        /// Where levels come from.
        /// </summary>
        public LibraryLevelSourceEnum LevelSource { get; set; } = LibraryLevelSourceEnum.None;

        /// <summary>
        /// Key of the profile field whose options are the levels (ProfileField source), or null.
        /// </summary>
        public string? LevelFieldKey { get; set; } = null;

        /// <summary>
        /// Hash of the property.json section the row was seeded from, or null after an admin edit.
        /// </summary>
        public string? SourceHash { get; set; } = null;

        /// <summary>
        /// Property override of library.downloads.enabled, or null to use the setting.
        /// </summary>
        public bool? DownloadsEnabled { get; set; } = null;

        /// <summary>
        /// Property override of library.completionThresholdPercent (50 to 100), or null to use the setting.
        /// </summary>
        public int? CompletionThresholdPercent { get; set; } = null;

        /// <summary>
        /// Property override of library.publicSamplesEnabled, or null to use the setting.
        /// </summary>
        public bool? PublicSamplesEnabled { get; set; } = null;

        /// <summary>
        /// Property override of library.autoFollowOnProgress, or null to use the setting.
        /// </summary>
        public bool? AutoFollowOnProgress { get; set; } = null;

        /// <summary>
        /// Property override of library.commentsEnabledByDefault, or null to use the setting.
        /// </summary>
        public bool? CommentsEnabledByDefault { get; set; } = null;

        /// <summary>
        /// Last change.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
