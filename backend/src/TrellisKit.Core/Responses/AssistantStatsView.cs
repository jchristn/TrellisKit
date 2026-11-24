namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// 30-day statistics of an assistant.
    /// </summary>
    public class AssistantStatsView
    {
        #region Public-Members

        /// <summary>
        /// Conversations.
        /// </summary>
        public long Conversations30d { get; set; } = 0;

        /// <summary>
        /// Messages.
        /// </summary>
        public long Messages30d { get; set; } = 0;

        /// <summary>
        /// Up rate.
        /// </summary>
        public double? ThumbsUpRate30d { get; set; } = null;

        #endregion
    }
}
