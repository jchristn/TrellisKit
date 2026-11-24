namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One comment revision (contract section 7.6). Labels and tags are the comment's metadata in that version, copied when the revision was stored (read-only; staff: entries only for staff).
    /// </summary>
    public class CommentRevisionView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Revision number.
        /// </summary>
        public int Revision { get; set; } = 0;

        /// <summary>
        /// Body of that version.
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// Attachment change text, or null.
        /// </summary>
        public string? AttachmentSummary { get; set; } = null;

        /// <summary>
        /// When the version was replaced (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Labels the comment had in this version (never null).
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Tags the comment had in this version (never null).
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(); }
        }

        #endregion

        #region Private-Members

        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

        #endregion
    }
}
