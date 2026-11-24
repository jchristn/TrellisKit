namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// A rendered email preview.
    /// </summary>
    public class EmailPreviewResponse
    {
        #region Public-Members

        /// <summary>
        /// Subject.
        /// </summary>
        public string Subject { get; set; } = String.Empty;

        /// <summary>
        /// Preheader.
        /// </summary>
        public string Preheader { get; set; } = String.Empty;

        /// <summary>
        /// Complete HTML document.
        /// </summary>
        public string Html { get; set; } = String.Empty;

        /// <summary>
        /// Plain-text version.
        /// </summary>
        public string Text { get; set; } = String.Empty;

        #endregion
    }
}
