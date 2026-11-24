namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A saved version of a prompt.
    /// </summary>
    public class AiPromptVersionView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Catalog key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Version number.
        /// </summary>
        public int Version { get; set; } = 1;

        /// <summary>
        /// Text of the version.
        /// </summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>
        /// True when the version reset the prompt to its default.
        /// </summary>
        public bool IsReset { get; set; } = false;

        /// <summary>
        /// True while the version is the pending draft (unpublished and not a reset). An older unpublished version that
        /// a newer one replaced has IsDraft false and PublishedUtc null.
        /// </summary>
        public bool IsDraft { get; set; } = false;

        /// <summary>
        /// Administrator, or null.
        /// </summary>
        public string? CreatedByUserId { get; set; } = null;

        /// <summary>
        /// UTC time saved.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC time the version went live, or null for a draft.
        /// </summary>
        public DateTime? PublishedUtc { get; set; } = null;

        /// <inheritdoc />
        public List<string> Labels { get; set; } = new List<string>();

        /// <inheritdoc />
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
