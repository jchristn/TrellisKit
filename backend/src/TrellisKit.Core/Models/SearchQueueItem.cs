namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// A pending search index update.
    /// </summary>
    public class SearchQueueItem
    {
        #region Public-Members

        /// <summary>
        /// Object type.
        /// </summary>
        public TargetTypeEnum TargetType { get; set; } = TargetTypeEnum.Thread;

        /// <summary>
        /// Object identifier.
        /// </summary>
        public string TargetId { get; set; } = String.Empty;

        /// <summary>
        /// When the update was queued.
        /// </summary>
        public DateTime EnqueuedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
