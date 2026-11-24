namespace TrellisKit.Core.Requests
{
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Body of POST and PUT /api/v1/admin/moderation/word-filter.
    /// </summary>
    public class WordFilterTermRequest
    {
        #region Public-Members

        /// <summary>
        /// Term (required on create; 1 to 100 characters).
        /// </summary>
        public string? Term { get; set; } = null;

        /// <summary>
        /// Word or Substring. Default Word on create.
        /// </summary>
        public WordFilterMatchTypeEnum? MatchType { get; set; } = null;

        /// <summary>
        /// Block or Flag. Default Block on create.
        /// </summary>
        public WordFilterActionEnum? Action { get; set; } = null;

        /// <summary>
        /// Optional note (up to 500 characters).
        /// </summary>
        public string? Note { get; set; } = null;

        /// <summary>
        /// Replacement labels, or null for unchanged (docs/ENUMERATION_AND_METADATA.md).
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Replacement tags, or null for unchanged.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
