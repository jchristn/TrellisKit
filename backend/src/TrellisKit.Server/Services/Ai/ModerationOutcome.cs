namespace TrellisKit.Server.Services.Ai
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Verdict of the moderation classifier.
    /// </summary>
    public class ModerationOutcome
    {
        #region Public-Members

        /// <summary>
        /// Verdict.
        /// </summary>
        public AssistantModerationVerdictEnum Verdict { get; set; } = AssistantModerationVerdictEnum.Allow;

        /// <summary>
        /// Category.
        /// </summary>
        public string? Category { get; set; } = null;

        /// <summary>
        /// True when no classifier ran because none is configured (the verdict is then Allow and is not stored as a
        /// moderation verdict).
        /// </summary>
        public bool Skipped { get; set; } = false;

        /// <summary>
        /// Telemetry stage.
        /// </summary>
        public AssistantMessageStage? Stage { get; set; } = null;

        #endregion
    }
}
