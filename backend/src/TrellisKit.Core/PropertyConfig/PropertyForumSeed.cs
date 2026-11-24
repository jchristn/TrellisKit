namespace TrellisKit.Core.PropertyConfig
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One initial forum in property.json (docs/PHASE3_CONTRACT.md section 15), applied only when the property has no
    /// forums yet.
    /// </summary>
    public class PropertyForumSeed
    {
        #region Public-Members

        /// <summary>
        /// Slug (2 to 60 of a-z, 0-9, and -), or null to derive it from the name.
        /// </summary>
        public string? Slug { get; set; } = null;

        /// <summary>
        /// Name (2 to 60 characters).
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Description, or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Section header, or null.
        /// </summary>
        public string? Section { get; set; } = null;

        /// <summary>
        /// Allowed thread types (Discussion, Question, Poll, Feedback), or null for all four.
        /// </summary>
        public List<string>? AllowedThreadTypes { get; set; } = null;

        #endregion
    }
}
