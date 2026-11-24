namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Filters of the ticket enumerations (member own list and staff inbox).
    /// </summary>
    public class TicketFilter
    {
        #region Public-Members

        /// <summary>
        /// Only these statuses, or empty for any.
        /// </summary>
        public List<TicketStatusEnum> Statuses
        {
            get { return _Statuses; }
            set { _Statuses = value ?? new List<TicketStatusEnum>(); }
        }

        /// <summary>
        /// Exclude Closed tickets (the inbox default when no status filter is given).
        /// </summary>
        public bool ExcludeClosed { get; set; } = false;

        /// <summary>
        /// Type key, or null.
        /// </summary>
        public string? TypeKey { get; set; } = null;

        /// <summary>
        /// Priority, or null.
        /// </summary>
        public TicketPriorityEnum? Priority { get; set; } = null;

        /// <summary>
        /// Assignee, or null.
        /// </summary>
        public string? AssigneeUserId { get; set; } = null;

        /// <summary>
        /// Only unassigned tickets when true, only assigned when false, any when null.
        /// </summary>
        public bool? Unassigned { get; set; } = null;

        /// <summary>
        /// Requester, or null.
        /// </summary>
        public string? RequesterUserId { get; set; } = null;

        /// <summary>
        /// Only overdue tickets when true (first response past due, not answered, not Closed), not overdue when false.
        /// </summary>
        public bool? Overdue { get; set; } = null;

        /// <summary>
        /// Only tickets linked to an idea when true, without when false.
        /// </summary>
        public bool? HasIdea { get; set; } = null;

        /// <summary>
        /// Evaluation time of the overdue filter (UTC).
        /// </summary>
        public DateTime NowUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private List<TicketStatusEnum> _Statuses = new List<TicketStatusEnum>();

        #endregion
    }
}
