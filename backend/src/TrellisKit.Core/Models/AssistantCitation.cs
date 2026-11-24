namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A knowledge item cited by a reply.
    /// </summary>
    public class AssistantCitation
    {
        #region Public-Members

        /// <summary>
        /// Citation key number.
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
        /// App path.
        /// </summary>
        public string Path { get; set; } = String.Empty;

        /// <summary>
        /// Retrieval score.
        /// </summary>
        public double? Score { get; set; } = null;

        #endregion
    }
}
