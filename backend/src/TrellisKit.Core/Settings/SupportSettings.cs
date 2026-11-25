namespace TrellisKit.Core.Settings
{
    using System;

    /// <summary>
    /// Support ticket settings (docs/PHASE5_CONTRACT.md section 14).
    /// </summary>
    public class SupportSettings
    {
        #region Public-Members

        /// <summary>
        /// Most characters in a ticket body or reply. Default 10000. Minimum 100, maximum 100000.
        /// </summary>
        public int MaxBodyLength
        {
            get { return _MaxBodyLength; }
            set { _MaxBodyLength = Math.Clamp(value, 100, 100000); }
        }

        /// <summary>
        /// Most images on a ticket message. Default 5. Minimum 0, maximum 20.
        /// </summary>
        public int MaxAttachments
        {
            get { return _MaxAttachments; }
            set { _MaxAttachments = Math.Clamp(value, 0, 20); }
        }

        /// <summary>
        /// Whether a ticket message may carry one video. Default false.
        /// </summary>
        public bool AllowVideo { get; set; } = false;

        /// <summary>
        /// Delete tickets when their requester deletes their account, except tickets labeled staff:retain-legal or
        /// staff:retain-billing. Default false. Operators must review and remove retention holds when no longer required.
        /// </summary>
        public bool DeleteOnAccountDeletion { get; set; } = false;

        /// <summary>
        /// Tickets a member may have that are not Closed. Default 10. Minimum 1, maximum 100.
        /// </summary>
        public int MaxOpenTicketsPerMember
        {
            get { return _MaxOpenTicketsPerMember; }
            set { _MaxOpenTicketsPerMember = Math.Clamp(value, 1, 100); }
        }

        /// <summary>
        /// Sets each ticket first response due time. Default 24. Minimum 1, maximum 720.
        /// </summary>
        public int FirstResponseTargetHours
        {
            get { return _FirstResponseTargetHours; }
            set { _FirstResponseTargetHours = Math.Clamp(value, 1, 720); }
        }

        /// <summary>
        /// Resolved tickets with no member reply for this long become Closed. Default 7. Minimum 1, maximum 365.
        /// </summary>
        public int AutoCloseDays
        {
            get { return _AutoCloseDays; }
            set { _AutoCloseDays = Math.Clamp(value, 1, 365); }
        }

        /// <summary>
        /// A member may reopen or reply to a closed ticket for this long. Default 14. Minimum 1, maximum 365.
        /// </summary>
        public int ReopenDays
        {
            get { return _ReopenDays; }
            set { _ReopenDays = Math.Clamp(value, 1, 365); }
        }

        /// <summary>
        /// Whether members see the support agent display name on replies. Default true.
        /// </summary>
        public bool ShowAgentNames { get; set; } = true;

        /// <summary>
        /// Tickets a member may open each day. Default 5. Minimum 1, maximum 1000.
        /// </summary>
        public int TicketsPerDay
        {
            get { return _TicketsPerDay; }
            set { _TicketsPerDay = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// Ticket replies a member may post each hour. Default 30. Minimum 1, maximum 10000.
        /// </summary>
        public int RepliesPerHour
        {
            get { return _RepliesPerHour; }
            set { _RepliesPerHour = Math.Clamp(value, 1, 10000); }
        }

        #endregion

        #region Private-Members

        private int _MaxBodyLength = 10000;
        private int _MaxAttachments = 5;
        private int _MaxOpenTicketsPerMember = 10;
        private int _FirstResponseTargetHours = 24;
        private int _AutoCloseDays = 7;
        private int _ReopenDays = 14;
        private int _TicketsPerDay = 5;
        private int _RepliesPerHour = 30;

        #endregion
    }
}
