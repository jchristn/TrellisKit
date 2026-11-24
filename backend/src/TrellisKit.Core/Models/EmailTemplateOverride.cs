namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// A property's override of an email template's text (tokens in {name} form). Null parts use the engine default.
    /// </summary>
    public class EmailTemplateOverride
    {
        #region Public-Members

        /// <summary>
        /// Template key.
        /// </summary>
        public string Template { get; set; } = String.Empty;

        /// <summary>
        /// Subject, at most 200 characters.
        /// </summary>
        public string? Subject { get; set; } = null;

        /// <summary>
        /// Preheader, at most 200 characters.
        /// </summary>
        public string? Preheader { get; set; } = null;

        /// <summary>
        /// Heading, at most 200 characters.
        /// </summary>
        public string? Heading { get; set; } = null;

        /// <summary>
        /// Body paragraphs separated by blank lines, at most 5000 characters.
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// Who changed it.
        /// </summary>
        public string? UpdatedByUserId { get; set; } = null;

        /// <summary>
        /// When it changed (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
