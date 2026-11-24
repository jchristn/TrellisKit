namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// GET /api/v1/admin/app/assets/{id}.
    /// </summary>
    public class RemoteAssetDetailView
    {
        /// <summary>
        /// The draft when one is pending, else the published asset.
        /// </summary>
        public RemoteAssetView Asset { get; set; } = new RemoteAssetView();

        /// <summary>
        /// The published asset, or null.
        /// </summary>
        public RemoteAssetView? Published { get; set; } = null;

        /// <summary>
        /// Published revisions.
        /// </summary>
        public ContentHistoryPreview History { get; set; } = new ContentHistoryPreview();
    }
}
