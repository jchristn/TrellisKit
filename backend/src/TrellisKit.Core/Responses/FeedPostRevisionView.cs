namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One feed post revision (contract section 5.5). Labels and tags are the post's metadata in that version, copied when the revision was stored (read-only; staff: entries only for staff).
    /// </summary>
    public class FeedPostRevisionView : IMetadataPayload
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
        /// Title of that version.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Body of that version.
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// Attachment change text, or null.
        /// </summary>
        public string? AttachmentSummary { get; set; } = null;

        /// <summary>
        /// Who made the edit.
        /// </summary>
        public UserSummary EditedBy { get; set; } = new UserSummary();

        /// <summary>
        /// Editor identifier (null for the admin token).
        /// </summary>
        public string? EditedByUserId { get; set; } = null;

        /// <summary>
        /// Editor display name.
        /// </summary>
        public string EditedByName { get; set; } = String.Empty;

        /// <summary>
        /// When the version was replaced (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Labels the post had in this version (never null).
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Tags the post had in this version (never null).
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
