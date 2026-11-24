namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One ticket in the account export (docs/PHASE5_CONTRACT.md section 12): metadata and replies, never notes.
    /// </summary>
    public class TicketExportItem
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Number.
        /// </summary>
        public long Number { get; set; } = 0;

        /// <summary>
        /// Type key.
        /// </summary>
        public string Type { get; set; } = String.Empty;

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
        /// Priority.
        /// </summary>
        public TicketPriorityEnum Priority { get; set; } = TicketPriorityEnum.Normal;

        /// <summary>
        /// Device block, or null.
        /// </summary>
        public TicketDeviceView? Device { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Resolved.
        /// </summary>
        public DateTime? ResolvedUtc { get; set; } = null;

        /// <summary>
        /// Closed.
        /// </summary>
        public DateTime? ClosedUtc { get; set; } = null;

        /// <summary>
        /// Replies, oldest first.
        /// </summary>
        public List<TicketExportReply> Replies
        {
            get { return _Replies; }
            set { _Replies = value ?? new List<TicketExportReply>(); }
        }

        #endregion

        #region Private-Members

        private List<TicketExportReply> _Replies = new List<TicketExportReply>();

        #endregion
    }
}
