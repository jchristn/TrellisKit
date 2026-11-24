namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// POST /api/v1/reports body (contract section 9.1).
    /// </summary>
    public class ReportRequest
    {
        #region Public-Members

        /// <summary>
        /// FeedPost, Comment, or User.
        /// </summary>
        public string? TargetType { get; set; } = null;

        /// <summary>
        /// Target identifier.
        /// </summary>
        public string? TargetId { get; set; } = null;

        /// <summary>
        /// Spam, Harassment, HateSpeech, SexualContent, Violence, SelfHarm, Impersonation, IntellectualProperty, or Other.
        /// </summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// Optional details (at most 1000; at least 10 when the reason is Other).
        /// </summary>
        public string? Details { get; set; } = null;

        /// <summary>
        /// Optional labels for the new report (docs/ENUMERATION_AND_METADATA.md section 1). Non-staff callers cannot set staff: entries; member values pass through the word filter. Applied only when a new report is created.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Optional tags for the new report (docs/ENUMERATION_AND_METADATA.md section 1). Non-staff callers cannot set staff: entries; member values pass through the word filter. Applied only when a new report is created.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
