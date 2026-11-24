namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body of PUT /api/v1/messages/{id}.
    /// </summary>
    public class MessageEditRequest
    {
        #region Public-Members

        /// <summary>
        /// New text.
        /// </summary>
        public string? Body { get; set; } = null;

        #endregion
    }
}
