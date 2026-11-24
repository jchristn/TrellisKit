namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One rerank score.
    /// </summary>
    public class RerankScoreView
    {
        #region Public-Members

        /// <summary>
        /// Document index.
        /// </summary>
        public int Index { get; set; } = 0;

        /// <summary>
        /// Score.
        /// </summary>
        public double Score { get; set; } = 0;

        #endregion
    }
}
