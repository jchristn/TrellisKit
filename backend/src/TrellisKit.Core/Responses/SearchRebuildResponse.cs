namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// POST /api/v1/admin/search/rebuild result: the index was cleared and every searchable object queued.
    /// </summary>
    public class SearchRebuildResponse
    {
        #region Public-Members

        /// <summary>
        /// Objects queued for indexing.
        /// </summary>
        public int Queued { get; set; } = 0;

        /// <summary>
        /// When the rebuild was requested.
        /// </summary>
        public DateTime RequestedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
