namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// The conversation target of an object.
    /// </summary>
    public class CommentTargetRef
    {
        #region Public-Members

        /// <summary>
        /// Target type.
        /// </summary>
        public string Type { get; set; } = String.Empty;

        /// <summary>
        /// Target identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        #endregion
    }
}
