namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// A polymorphic content reference: { type, id }.
    /// </summary>
    public class ContentTarget
    {
        #region Public-Members

        /// <summary>
        /// Target type, for example FeedPost.
        /// </summary>
        public string Type { get; set; } = String.Empty;

        /// <summary>
        /// Target identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        #endregion
    }
}
