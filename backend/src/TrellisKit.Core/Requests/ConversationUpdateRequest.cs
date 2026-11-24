namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Body of PUT /api/v1/assistant-conversations/{id}.
    /// </summary>
    public class ConversationUpdateRequest
    {
        #region Public-Members

        /// <summary>
        /// Title (1 to 120).
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Archived.
        /// </summary>
        public bool? IsArchived { get; set; } = null;

        /// <summary>
        /// Replacement labels.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Replacement tags.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
