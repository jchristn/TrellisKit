namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The effective library configuration clients render from.
    /// </summary>
    public class PublicLibraryConfig
    {
        #region Public-Members

        /// <summary>
        /// None, ProfileField, or Custom.
        /// </summary>
        public string LevelSource { get; set; } = "None";

        /// <summary>
        /// Profile field key when LevelSource is ProfileField.
        /// </summary>
        public string? LevelFieldKey { get; set; } = null;

        /// <summary>
        /// Effective levels in order.
        /// </summary>
        public List<PublicOption> Levels { get; set; } = new List<PublicOption>();

        /// <summary>
        /// Offline downloads on mobile are enabled for the property.
        /// </summary>
        public bool DownloadsEnabled { get; set; } = false;

        /// <summary>
        /// How often players report progress while playing.
        /// </summary>
        public int ProgressIntervalSeconds { get; set; } = 15;

        /// <summary>
        /// Percent at which a lesson counts as completed.
        /// </summary>
        public int CompletionThresholdPercent { get; set; } = 90;

        /// <summary>
        /// A resume point within this many seconds of the end restarts from 0.
        /// </summary>
        public int ResumeTailSeconds { get; set; } = 10;

        /// <summary>
        /// The anonymous public sample lessons are served (library.publicSamplesEnabled, effective). Default true.
        /// Clients skip the public routes while this is false.
        /// </summary>
        public bool PublicSamplesEnabled { get; set; } = true;

        #endregion
    }
}
