namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Widget settings.
    /// </summary>
    public class AssistantEmbedView
    {
        #region Public-Members

        /// <summary>
        /// Enabled.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Extra allowed origins.
        /// </summary>
        public List<string> AllowedOrigins { get; set; } = new List<string>();

        /// <summary>
        /// Launcher label.
        /// </summary>
        public string? LauncherLabel { get; set; } = null;

        /// <summary>
        /// Position.
        /// </summary>
        public string Position { get; set; } = "BottomRight";

        #endregion
    }
}
