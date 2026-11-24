namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A support ticket (docs/PHASE5_CONTRACT.md section 2.3). Lists return it without messages (TicketSummary, AdminTicketSummary).
    /// </summary>
    public class TicketView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier (tkt_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Per-property number.
        /// </summary>
        public long Number { get; set; } = 0;

        /// <summary>
        /// Type.
        /// </summary>
        public TicketTypeRef Type { get; set; } = new TicketTypeRef();

        /// <summary>
        /// Title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Opening body.
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// Status.
        /// </summary>
        public TicketStatusEnum Status { get; set; } = TicketStatusEnum.Open;

        /// <summary>
        /// Priority (staff set; read-only for members).
        /// </summary>
        public TicketPriorityEnum Priority { get; set; } = TicketPriorityEnum.Normal;

        /// <summary>
        /// Requester (Deleted member after account deletion).
        /// </summary>
        public UserSummary Requester { get; set; } = new UserSummary();

        /// <summary>
        /// Assignee, or null.
        /// </summary>
        public UserSummary? Assignee { get; set; } = null;

        /// <summary>
        /// Device block, or null when diagnostics were not included.
        /// </summary>
        public TicketDeviceView? Device { get; set; } = null;

        /// <summary>
        /// Staff only: the requester membership snapshot at creation.
        /// </summary>
        public TicketMembershipView? Membership { get; set; } = null;

        /// <summary>
        /// Staff ticket detail only: the requester membership now.
        /// </summary>
        public TicketMembershipView? CurrentMembership { get; set; } = null;

        /// <summary>
        /// Linked idea, or null.
        /// </summary>
        public TicketIdeaRef? Idea { get; set; } = null;

        /// <summary>
        /// Ticket detail only: the newest messages.
        /// </summary>
        public TicketMessagePreview? Messages { get; set; } = null;

        /// <summary>
        /// First response target.
        /// </summary>
        public DateTime? FirstResponseDueUtc { get; set; } = null;

        /// <summary>
        /// First staff reply.
        /// </summary>
        public DateTime? FirstResponseUtc { get; set; } = null;

        /// <summary>
        /// Unanswered past the first response target and not Closed.
        /// </summary>
        public bool IsOverdue { get; set; } = false;

        /// <summary>
        /// Last activity.
        /// </summary>
        public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Resolved.
        /// </summary>
        public DateTime? ResolvedUtc { get; set; } = null;

        /// <summary>
        /// Closed.
        /// </summary>
        public DateTime? ClosedUtc { get; set; } = null;

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
        /// Caller capabilities.
        /// </summary>
        public TicketViewerView Viewer { get; set; } = new TicketViewerView();

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>(StringComparer.Ordinal);

        #endregion
    }
}
