namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Bounded preview of a reply's tool calls.
    /// </summary>
    public class ToolCallPageView
    {
        #region Public-Members

        /// <summary>
        /// Tool calls, in order (at most 12, the per-turn cap).
        /// </summary>
        public List<ToolCallView> Objects { get; set; } = new List<ToolCallView>();

        /// <summary>
        /// Count.
        /// </summary>
        public long TotalRecords { get; set; } = 0;

        #endregion
    }
}
