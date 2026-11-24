namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A directory instructor on an event (InstructorSummary subset).
    /// </summary>
    public class EventInstructorView
    {
        #region Public-Members

        /// <summary>
        /// Identifier (ins_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Slug.
        /// </summary>
        public string Slug { get; set; } = String.Empty;

        /// <summary>
        /// Display name.
        /// </summary>
        public string DisplayName { get; set; } = String.Empty;

        /// <summary>
        /// Photo media, or null.
        /// </summary>
        public string? PhotoMediaId { get; set; } = null;

        #endregion
    }
}
