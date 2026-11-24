namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// The forum of a mute.
    /// </summary>
    public class ForumMuteForumRef
    {
        #region Public-Members

        /// <summary>
        /// Forum identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Forum name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        #endregion
    }
}
