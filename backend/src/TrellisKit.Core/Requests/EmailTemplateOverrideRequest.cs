namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Body of PUT /api/v1/admin/email/templates/{key} and POST .../preview: null parts use the engine default.
    /// </summary>
    public class EmailTemplateOverrideRequest
    {
        #region Public-Members

        /// <summary>
        /// Subject (one line, at most 200 characters).
        /// </summary>
        public string? Subject { get; set; } = null;

        /// <summary>
        /// Preheader (one line, at most 200 characters).
        /// </summary>
        public string? Preheader { get; set; } = null;

        /// <summary>
        /// Heading (one line, at most 200 characters).
        /// </summary>
        public string? Heading { get; set; } = null;

        /// <summary>
        /// Body: paragraphs separated by blank lines (at most 5000 characters).
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// Preview only: sample token values (the template's samples fill the rest).
        /// </summary>
        public Dictionary<string, string>? Tokens { get; set; } = null;

        #endregion
    }
}
