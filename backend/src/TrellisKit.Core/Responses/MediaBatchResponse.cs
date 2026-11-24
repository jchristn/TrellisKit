namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Batch status (GET /api/v1/media?ids=), at most 20 items.
    /// </summary>
    public class MediaBatchResponse
    {
        #region Public-Members

        /// <summary>
        /// Visible media in request order (at most 20).
        /// </summary>
        public List<MediaView> Items { get; set; } = new List<MediaView>();

        #endregion
    }
}
