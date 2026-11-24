namespace TrellisKit.Core.Enums
{
    /// <summary>
    /// Client-reported live update event types (docs/LIVE_UPDATES_CONTRACT.md section 9.2).
    /// </summary>
    public enum UpdateEventTypeEnum
    {
        /// <summary>
        /// An update check failed.
        /// </summary>
        CheckFailed,

        /// <summary>
        /// An update finished downloading.
        /// </summary>
        Downloaded,

        /// <summary>
        /// An update download failed.
        /// </summary>
        DownloadFailed,

        /// <summary>
        /// An update launched.
        /// </summary>
        Applied,

        /// <summary>
        /// An update crashed on launch and the library rolled back.
        /// </summary>
        LaunchFailed,

        /// <summary>
        /// The app rolled back to its embedded bundle.
        /// </summary>
        RolledBackToEmbedded,

        /// <summary>
        /// The critical update prompt was shown.
        /// </summary>
        CriticalPromptShown,

        /// <summary>
        /// The member restarted from the critical update prompt.
        /// </summary>
        CriticalRestarted
    }
}
