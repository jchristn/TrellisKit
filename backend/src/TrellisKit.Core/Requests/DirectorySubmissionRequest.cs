namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A member directory submission (docs/PHASE4_CONTRACT.md section 3.7).
    /// </summary>
    public class DirectorySubmissionRequest
    {
        #region Public-Members

        /// <summary>
        /// Type.
        /// </summary>
        public DirectorySubmissionTypeEnum? Type { get; set; } = null;

        /// <summary>
        /// Target (edits, claims, closures).
        /// </summary>
        public string? TargetId { get; set; } = null;

        /// <summary>
        /// Proposed fields of the target type (only changed fields for edits).
        /// </summary>
        public Dictionary<string, JsonElement>? Fields { get; set; } = null;

        /// <summary>
        /// Note (0 to 1000; claims 10 to 1000).
        /// </summary>
        public string? Note { get; set; } = null;

        /// <summary>
        /// Queue even when possible duplicates were found.
        /// </summary>
        public bool ConfirmNotDuplicate { get; set; } = false;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
