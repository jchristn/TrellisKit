namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// POST /api/v1/admin/updates/{id}/symbolicate body: a pasted stack trace or a reported failure.
    /// </summary>
    public class SymbolicateRequest
    {
        /// <summary>
        /// Stack trace text (at most 65536 characters).
        /// </summary>
        public string? StackTrace { get; set; } = null;

        /// <summary>
        /// A reported failure (uev_) of this update.
        /// </summary>
        public string? EventId { get; set; } = null;
    }
}
