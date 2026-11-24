namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Body of POST /api/v1/admin/ai/feedback/{id}/review.
    /// </summary>
    public class FeedbackReviewRequest
    {
        #region Public-Members

        /// <summary>
        /// Optional note (audited).
        /// </summary>
        public string? Note { get; set; } = null;

        #endregion
    }
}
