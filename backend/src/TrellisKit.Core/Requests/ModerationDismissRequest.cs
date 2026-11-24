namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// POST /api/v1/admin/moderation/queue/{targetType}/{targetId}/dismiss body.
    /// </summary>
    public class ModerationDismissRequest
    {
        #region Public-Members

        /// <summary>
        /// Optional note (at most 2000).
        /// </summary>
        public string? Note { get; set; } = null;

        /// <summary>
        /// Optional labels for the new Dismiss moderation action (docs/ENUMERATION_AND_METADATA.md section 1). Applied only when a Dismiss action is recorded (at least one report was dismissed).
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Optional tags for the new Dismiss moderation action (docs/ENUMERATION_AND_METADATA.md section 1). Applied only when a Dismiss action is recorded (at least one report was dismissed).
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
