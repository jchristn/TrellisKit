namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// Body of PUT /api/v1/conversations/{id}/folder.
    /// </summary>
    public class ConversationFolderRequest
    {
        #region Public-Members

        /// <summary>
        /// Inbox or Archived.
        /// </summary>
        public string? Folder { get; set; } = null;

        #endregion
    }
}
