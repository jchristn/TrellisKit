namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// POST /api/v1/admin/updates/{id}/symbolicate: a stack trace mapped through the update's private source map.
    /// </summary>
    public class SymbolicationView
    {
        /// <summary>
        /// Update id (upd_).
        /// </summary>
        public string UpdateId { get; set; } = String.Empty;

        /// <summary>
        /// Whether the update has a source map.
        /// </summary>
        public bool SourceMapAvailable { get; set; } = false;

        /// <summary>
        /// The source map used, or null.
        /// </summary>
        public SourceMapInfoView? SourceMap { get; set; } = null;

        /// <summary>
        /// Frames (at most 200).
        /// </summary>
        public List<SymbolicatedFrameView> Frames { get; set; } = new List<SymbolicatedFrameView>();

        /// <summary>
        /// The symbolicated stack as text.
        /// </summary>
        public string Text { get; set; } = String.Empty;

        /// <summary>
        /// Whether frames beyond 200 were dropped.
        /// </summary>
        public bool Truncated { get; set; } = false;
    }
}
