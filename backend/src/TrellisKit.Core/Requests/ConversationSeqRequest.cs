namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body of the read and delivered routes.
    /// </summary>
    public class ConversationSeqRequest
    {
        #region Public-Members

        /// <summary>
        /// Position.
        /// </summary>
        public long? Seq { get; set; } = null;

        #endregion
    }
}
