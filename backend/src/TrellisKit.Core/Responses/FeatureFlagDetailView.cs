namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// GET /api/v1/admin/app/flags/{id}: the draft view (or the published one without a draft), the live definition, and history.
    /// </summary>
    public class FeatureFlagDetailView
    {
        /// <summary>
        /// Draft, or the published revision when there is no draft.
        /// </summary>
        public FeatureFlagView Flag { get; set; } = new FeatureFlagView();

        /// <summary>
        /// Live definition, or null.
        /// </summary>
        public FeatureFlagView? Published { get; set; } = null;

        /// <summary>
        /// History preview.
        /// </summary>
        public FlagHistoryPreview History { get; set; } = new FlagHistoryPreview();
    }
}
