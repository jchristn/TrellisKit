namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The newest messages of a ticket (at most 20) with the total and the enumerate path.
    /// </summary>
    public class TicketMessagePreview
    {
        #region Public-Members

        /// <summary>
        /// Messages, oldest first.
        /// </summary>
        public List<TicketMessageView> Objects
        {
            get { return _Objects; }
            set { _Objects = value ?? new List<TicketMessageView>(); }
        }

        /// <summary>
        /// Messages the caller can see.
        /// </summary>
        public long TotalRecords { get; set; } = 0;

        /// <summary>
        /// Enumeration path.
        /// </summary>
        public string Enumerate { get; set; } = String.Empty;

        #endregion

        #region Private-Members

        private List<TicketMessageView> _Objects = new List<TicketMessageView>();

        #endregion
    }
}
