namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// The administrator's current text of one prompt catalog key.
    /// </summary>
    public class AiPromptTemplate
    {
        #region Public-Members

        /// <summary>
        /// Prompt catalog key (for example platform.scopeGuardrail).
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Prompt text.
        /// </summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>
        /// Version number, starting at 1 and increasing with every save or reset.
        /// </summary>
        public int Version { get; set; } = 1;

        /// <summary>
        /// Administrator who saved it, or null.
        /// </summary>
        public string? UpdatedByUserId { get; set; } = null;

        /// <summary>
        /// UTC time it was saved.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
