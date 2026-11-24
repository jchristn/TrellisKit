namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A retrieved knowledge item with its score.
    /// </summary>
    public class CitationScoreView
    {
        #region Public-Members

        /// <summary>
        /// Key number.
        /// </summary>
        public int Index { get; set; } = 0;

        /// <summary>
        /// Content type.
        /// </summary>
        public string TargetType { get; set; } = String.Empty;

        /// <summary>
        /// Content id.
        /// </summary>
        public string TargetId { get; set; } = String.Empty;

        /// <summary>
        /// Title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Score.
        /// </summary>
        public double? Score { get; set; } = null;

        #endregion
    }
}
