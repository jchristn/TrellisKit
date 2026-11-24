namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Body of POST /api/v1/admin/app/drafts/discard.
    /// </summary>
    public class DraftDiscardRequest
    {
        /// <summary>
        /// Flags whose drafts to discard, or null for every draft.
        /// </summary>
        public List<string>? ObjectIds { get; set; } = null;
    }
}
