namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>Property-scoped profile gallery filters.</summary>
    public class ProfileMediaFilter
    {
        /// <summary>Required profile owner.</summary>
        public string ProfileUserId { get; set; } = String.Empty;
        /// <summary>Include moderated items for authorized staff.</summary>
        public bool IncludeModerated { get; set; } = false;
        /// <summary>Excluded uploaders.</summary>
        public List<string> HiddenAuthorIds { get; set; } = new List<string>();
        /// <summary>Optional uploader filter.</summary>
        public string? AuthorUserId { get; set; } = null;
        /// <summary>Image or video, or all.</summary>
        public MediaKindEnum? Kind { get; set; } = null;
    }
}
