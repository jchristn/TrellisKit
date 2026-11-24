namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Collection filters of a document acceptance enumeration.
    /// </summary>
    public class DocumentAcceptanceFilter
    {
        #region Public-Members

        /// <summary>
        /// Only this document, or null.
        /// </summary>
        public string? DocumentKey { get; set; } = null;

        /// <summary>
        /// Only this member, or null.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Only this display version, or null.
        /// </summary>
        public string? Version { get; set; } = null;

        /// <summary>
        /// Only this method, or null.
        /// </summary>
        public DocumentAcceptanceMethodEnum? Method { get; set; } = null;

        /// <summary>
        /// Only this client, or null.
        /// </summary>
        public DocumentClientEnum? Client { get; set; } = null;

        #endregion
    }
}
