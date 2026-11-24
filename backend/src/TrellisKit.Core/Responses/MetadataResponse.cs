namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Labels and tags of one object, as returned by the generic metadata routes.
    /// </summary>
    public class MetadataResponse
    {
        #region Public-Members

        /// <summary>
        /// Object type name.
        /// </summary>
        public string TargetType { get; set; } = String.Empty;

        /// <summary>
        /// Object identifier.
        /// </summary>
        public string TargetId { get; set; } = String.Empty;

        /// <summary>
        /// Labels visible to the caller.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags visible to the caller.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
