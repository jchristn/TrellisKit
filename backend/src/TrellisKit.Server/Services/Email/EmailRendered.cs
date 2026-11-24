namespace TrellisKit.Server.Services.Email
{
    using System;

    /// <summary>
    /// A rendered administrative email.
    /// </summary>
    public class EmailRendered
    {
        #region Public-Members

        /// <summary>
        /// Subject.
        /// </summary>
        public string Subject { get; set; } = String.Empty;

        /// <summary>
        /// Preheader (inbox preview text).
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
