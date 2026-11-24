namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;

    /// <summary>
    /// PUT /api/v1/admin/directory/settings. Omitted fields stay.
    /// </summary>
    public class DirectorySettingsRequest
    {
        #region Public-Members

        /// <summary>
        /// Venue label (1 to 40).
        /// </summary>
        public string? VenueLabel { get; set; } = null;

        /// <summary>
        /// Venue label plural (1 to 40).
        /// </summary>
        public string? VenueLabelPlural { get; set; } = null;

        /// <summary>
        /// Instructor label (1 to 40).
        /// </summary>
        public string? InstructorLabel { get; set; } = null;

        /// <summary>
        /// Instructor label plural (1 to 40).
        /// </summary>
        public string? InstructorLabelPlural { get; set; } = null;

        /// <summary>
        /// Rank profile field key (empty clears it).
        /// </summary>
        public string? InstructorRankFieldKey { get; set; } = null;

        /// <summary>
        /// Member submissions on or off.
        /// </summary>
        public bool? Submissions { get; set; } = null;

        /// <summary>
        /// Instructor claims on or off.
        /// </summary>
        public bool? Claims { get; set; } = null;

        /// <summary>
        /// directory.* property settings to set (secrets are encrypted).
        /// </summary>
        public Dictionary<string, JsonElement> Values { get; set; } = new Dictionary<string, JsonElement>();

        /// <summary>
        /// directory.* property settings to clear.
        /// </summary>
        public List<string> Clear { get; set; } = new List<string>();

        #endregion
    }
}
