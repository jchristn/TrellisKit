namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A thread revision (docs/PHASE3_CONTRACT.md section 4.1).
    /// </summary>
    public class ThreadRevisionView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier (thv_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Revision number.
        /// </summary>
        public int Revision { get; set; } = 0;

        /// <summary>
        /// Title before the edit.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Body before the edit.
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// Type before the edit.
        /// </summary>
        public ThreadTypeEnum Type { get; set; } = ThreadTypeEnum.Discussion;

        /// <summary>
        /// Attachment changes.
        /// </summary>
        public string? AttachmentSummary { get; set; } = null;

        /// <summary>
        /// Who edited.
        /// </summary>
        public UserSummary EditedBy
        {
            get { return _EditedBy; }
            set { _EditedBy = value ?? new UserSummary(); }
        }

        /// <summary>
        /// Author or Moderator.
        /// </summary>
        public ThreadEditorRoleEnum EditedByRole { get; set; } = ThreadEditorRoleEnum.Author;

        /// <summary>
        /// When the revision was stored.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Labels of that version.
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Tags of that version.
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(); }
        }

        #endregion

        #region Private-Members

        private UserSummary _EditedBy = new UserSummary();
        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

        #endregion
    }
}
