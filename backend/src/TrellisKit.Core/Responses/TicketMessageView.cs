namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A ticket message (docs/PHASE5_CONTRACT.md section 2.3).
    /// </summary>
    public class TicketMessageView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier (tkm_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Ticket.
        /// </summary>
        public string TicketId { get; set; } = String.Empty;

        /// <summary>
        /// Reply, Note (staff only), or System (staff timeline only).
        /// </summary>
        public TicketMessageKindEnum Kind { get; set; } = TicketMessageKindEnum.Reply;

        /// <summary>
        /// Author, or null for system lines (a deleted member renders as Deleted member).
        /// </summary>
        public UserSummary? Author { get; set; } = null;

        /// <summary>
        /// Member, Staff, or System.
        /// </summary>
        public TicketAuthorRoleEnum AuthorRole { get; set; } = TicketAuthorRoleEnum.Member;

        /// <summary>
        /// Display name for staff replies shown to members: the support team name plus the agent name when support.showAgentNames.
        /// </summary>
        public string? AuthorName { get; set; } = null;

        /// <summary>
        /// Body (Markdown subset; plain text for system lines).
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// System lines that changed the status: the new status.
        /// </summary>
        public TicketStatusEnum? StatusChange { get; set; } = null;

        /// <summary>
        /// Attachments with per-object URLs for the caller.
        /// </summary>
        public List<MediaView> Attachments
        {
            get { return _Attachments; }
            set { _Attachments = value ?? new List<MediaView>(); }
        }

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(StringComparer.Ordinal); }
        }

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private List<MediaView> _Attachments = new List<MediaView>();
        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>(StringComparer.Ordinal);

        #endregion
    }
}
