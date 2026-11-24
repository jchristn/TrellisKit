namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// GET /api/v1/admin/app/banners/{id}.
    /// </summary>
    public class BannerDetailView
    {
        /// <summary>
        /// The draft when one is pending, else the published banner.
        /// </summary>
        public BannerView Banner { get; set; } = new BannerView();

        /// <summary>
        /// The published banner, or null.
        /// </summary>
        public BannerView? Published { get; set; } = null;

        /// <summary>
        /// Published revisions.
        /// </summary>
        public ContentHistoryPreview History { get; set; } = new ContentHistoryPreview();
    }
}
