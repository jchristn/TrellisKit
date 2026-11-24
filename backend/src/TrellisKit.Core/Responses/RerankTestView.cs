namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Rerank test output.
    /// </summary>
    public class RerankTestView
    {
        #region Public-Members

        /// <summary>
        /// Scores, highest first (at most 10).
        /// </summary>
        public List<RerankScoreView> Scores { get; set; } = new List<RerankScoreView>();

        #endregion
    }
}
