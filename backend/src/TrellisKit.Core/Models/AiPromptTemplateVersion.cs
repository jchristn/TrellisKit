namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// One saved version of a prompt template (history).
    /// </summary>
    public class AiPromptTemplateVersion
    {
        #region Public-Members

        /// <summary>
        /// Identifier (apv_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Prompt catalog key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Version number.
        /// </summary>
        public int Version { get; set; } = 1;

        /// <summary>
        /// Prompt text of this version (the default text when IsReset).
        /// </summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>
        /// True when this version reset the prompt to its default.
        /// </summary>
        public bool IsReset { get; set; } = false;

        /// <summary>
        /// Administrator who saved it, or null.
        /// </summary>
        public string? CreatedByUserId { get; set; } = null;

        /// <summary>
        /// UTC time it was saved.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC time the version went live, or null while it is a draft (or a superseded draft that never went live).
        /// </summary>
        public DateTime? PublishedUtc { get; set; } = null;

        #endregion
    }
}
