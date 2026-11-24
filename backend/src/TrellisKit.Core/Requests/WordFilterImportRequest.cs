namespace TrellisKit.Core.Requests
{
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Body of POST /api/v1/admin/moderation/word-filter/import.
    /// </summary>
    public class WordFilterImportRequest
    {
        #region Public-Members

        /// <summary>
        /// Terms (1 to 1000). Null is treated as empty.
        /// </summary>
        public List<string?>? Terms { get; set; } = null;

        /// <summary>
        /// Match type for every imported term. Default Word.
        /// </summary>
        public WordFilterMatchTypeEnum? MatchType { get; set; } = null;

        /// <summary>
        /// Action for every imported term. Default Block.
        /// </summary>
        public WordFilterActionEnum? Action { get; set; } = null;

        #endregion
    }
}
