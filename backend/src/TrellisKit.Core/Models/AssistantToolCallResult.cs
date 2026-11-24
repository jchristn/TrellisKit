namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// One item a tool call returned (for citations and the member's tool activity links).
    /// </summary>
    public class AssistantToolCallResult
    {
        #region Public-Members

        /// <summary>
        /// Content type, for example Lesson.
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
        /// App path, for example /lessons/les_x.
        /// </summary>
        public string Path { get; set; } = String.Empty;

        #endregion
    }
}
