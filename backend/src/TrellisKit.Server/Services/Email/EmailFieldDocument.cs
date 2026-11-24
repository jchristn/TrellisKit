namespace TrellisKit.Server.Services.Email
{
    using System;

    /// <summary>
    /// One key/value row of a fields block in a property email template file.
    /// </summary>
    public class EmailFieldDocument
    {
        #region Public-Members

        /// <summary>
        /// Label ({token} placeholders).
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Value ({token} placeholders; an empty result hides the row).
        /// </summary>
        public string Value { get; set; } = String.Empty;

        #endregion
    }
}
