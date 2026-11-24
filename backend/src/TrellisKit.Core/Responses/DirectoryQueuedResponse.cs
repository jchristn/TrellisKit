namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// The answer of a background request.
    /// </summary>
    public class DirectoryQueuedResponse
    {
        #region Public-Members

        /// <summary>
        /// Items queued.
        /// </summary>
        public int Queued { get; set; } = 0;

        #endregion
    }
}
