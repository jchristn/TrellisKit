namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// GET /api/v1/reactions/set: the property's reactions in order (fixed bound: the property configuration) and the one-tap key.
    /// </summary>
    public class ReactionSetResponse
    {
        #region Public-Members

        /// <summary>
        /// Reactions in display order (never null).
        /// </summary>
        public List<ReactionSetItem> Items
        {
            get { return _Items; }
            set { _Items = value ?? new List<ReactionSetItem>(); }
        }

        /// <summary>
        /// First key, or null when the property defines none.
        /// </summary>
        public string? LikeKey { get; set; } = null;

        #endregion

        #region Private-Members

        private List<ReactionSetItem> _Items = new List<ReactionSetItem>();

        #endregion
    }
}
