namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A forum reference on a thread or search result.
    /// </summary>
    public class ForumRefView
    {
        #region Public-Members

        /// <summary>
        /// Forum identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Forum slug.
        /// </summary>
        public string Slug { get; set; } = String.Empty;

        /// <summary>
        /// Forum name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        #endregion
    }
}
