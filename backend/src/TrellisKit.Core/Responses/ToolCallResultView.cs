namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// One item a tool call returned, with its app path.
    /// </summary>
    public class ToolCallResultView
    {
        #region Public-Members

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
