namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Concurrency, queue, and retry limits.
    /// </summary>
    public class EndpointLimitsView
    {
        #region Public-Members

        /// <summary>
        /// In-flight calls.
        /// </summary>
        public int MaxConcurrentRequests { get; set; } = 2;

        /// <summary>
        /// Waiting calls.
        /// </summary>
        public int MaxQueueDepth { get; set; } = 20;

        /// <summary>
        /// Retries.
        /// </summary>
        public int MaxRetries { get; set; } = 2;

        #endregion
    }
}
