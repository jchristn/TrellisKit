namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// GET /api/v1/admin/email/staff-events (fixed-bound by the engine catalog).
    /// </summary>
    public class EmailStaffEventList
    {
        #region Public-Members

        /// <summary>
        /// Staff events.
        /// </summary>
        public List<EmailStaffEventView> Events
        {
            get { return _Events; }
            set { _Events = value ?? new List<EmailStaffEventView>(); }
        }

        #endregion

        #region Private-Members

        private List<EmailStaffEventView> _Events = new List<EmailStaffEventView>();

        #endregion
    }
}
