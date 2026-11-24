namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A citation of a reply.
    /// </summary>
    public class CitationView
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
        /// App path.
        /// </summary>
        public string Path { get; set; } = String.Empty;

        #endregion
    }
}
