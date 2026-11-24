namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// PUT /api/v1/admin/tickets/{id} body; omitted fields stay (assigneeUserId null unassigns).
    /// </summary>
    public class TicketUpdateRequest
    {
        #region Public-Members

        /// <summary>
        /// Open, InProgress, WaitingOnMember, Resolved, or Closed.
        /// </summary>
        public string? Status { get; set; } = null;

        /// <summary>
        /// Low, Normal, High, or Urgent.
        /// </summary>
        public string? Priority { get; set; } = null;

        /// <summary>
        /// Ticket type key.
        /// </summary>
        public string? Type { get; set; } = null;

        /// <summary>
        /// Assignee (a SupportAgent or SystemAdmin), or null to unassign when present.
        /// </summary>
        public string? AssigneeUserId { get; set; } = null;

        /// <summary>
        /// Replacement labels, or null.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Replacement tags, or null.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
