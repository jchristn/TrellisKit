namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// GET /api/v1/admin/app/strings/{key}.
    /// </summary>
    public class RemoteStringDetailView
    {
        /// <summary>
        /// The key and its override.
        /// </summary>
        public RemoteStringView String { get; set; } = new RemoteStringView();

        /// <summary>
        /// Published revisions.
        /// </summary>
        public ContentHistoryPreview History { get; set; } = new ContentHistoryPreview();
    }
}
