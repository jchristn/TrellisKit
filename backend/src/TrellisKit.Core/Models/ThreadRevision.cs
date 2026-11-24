namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// A thread edit history entry (docs/PHASE3_CONTRACT.md section 4.1).
    /// </summary>
    public class ThreadRevision
    {
        #region Public-Members

        /// <summary>
        /// Identifier (thv_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Thread identifier.
        /// </summary>
        public string ThreadId { get; set; } = String.Empty;

        /// <summary>
        /// Revision number (1 is the oldest).
        /// </summary>
        public int Revision { get; set; } = 0;

        /// <summary>
        /// Title before the edit.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Body before the edit, or null.
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// Type before the edit.
        /// </summary>
        public ThreadTypeEnum Type { get; set; } = ThreadTypeEnum.Discussion;

        /// <summary>
        /// What changed in the attachments, or null.
        /// </summary>
        public string? AttachmentSummary { get; set; } = null;

        /// <summary>
        /// Who edited, or null for the admin token.
        /// </summary>
        public string? EditedByUserId { get; set; } = null;

        /// <summary>
        /// Author or Moderator.
        /// </summary>
        public ThreadEditorRoleEnum EditedByRole { get; set; } = ThreadEditorRoleEnum.Author;

        /// <summary>
        /// When the revision was stored.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
