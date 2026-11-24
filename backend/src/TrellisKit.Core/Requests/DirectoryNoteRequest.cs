namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// An optional note (verify, unverify).
    /// </summary>
    public class DirectoryNoteRequest
    {
        #region Public-Members

        /// <summary>
        /// Note (0 to 1000).
        /// </summary>
        public string? Note { get; set; } = null;

        #endregion
    }
}
