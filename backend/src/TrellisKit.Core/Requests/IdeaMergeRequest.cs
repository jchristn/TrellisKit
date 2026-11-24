namespace TrellisKit.Core.Requests
{
    using System;

    /// <summary>
    /// POST /api/v1/admin/ideas/{id}/merge.
    /// </summary>
    public class IdeaMergeRequest
    {
        #region Public-Members

        /// <summary>
        /// Target idea (required).
        /// </summary>
        public string? IntoIdeaId { get; set; } = null;

        #endregion
    }
}
