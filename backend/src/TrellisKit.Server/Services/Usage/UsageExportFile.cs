namespace TrellisKit.Server.Services.Usage
{
    using System;

    /// <summary>
    /// A usage export file.
    /// </summary>
    public sealed class UsageExportFile
    {
        #region Public-Members

        /// <summary>
        /// Download file name.
        /// </summary>
        public string FileName { get; set; } = String.Empty;

        /// <summary>
        /// Content type.
        /// </summary>
        public string ContentType { get; set; } = "application/json";

        /// <summary>
        /// File body.
        /// </summary>
        public string Body { get; set; } = String.Empty;

        #endregion
    }
}
