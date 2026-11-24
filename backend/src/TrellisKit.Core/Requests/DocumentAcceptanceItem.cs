namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// One accepted document in a request: its key and the version the member saw.
    /// </summary>
    public class DocumentAcceptanceItem
    {
        #region Public-Members

        /// <summary>
        /// Document key.
        /// </summary>
        public string? Key { get; set; } = null;

        /// <summary>
        /// Display version the member accepted (must be the current version).
        /// </summary>
        public string? Version { get; set; } = null;

        #endregion
    }
}
