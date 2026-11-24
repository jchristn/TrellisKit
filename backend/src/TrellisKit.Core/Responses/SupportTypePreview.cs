namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Tickets per type (at most 20) with the number of types and the type enumerate path.
    /// </summary>
    public class SupportTypePreview
    {
        #region Public-Members

        /// <summary>
        /// Counts, most first.
        /// </summary>
        public List<SupportTypeCount> Objects
        {
            get { return _Objects; }
            set { _Objects = value ?? new List<SupportTypeCount>(); }
        }

        /// <summary>
        /// Types with tickets in the window.
        /// </summary>
        public long TotalRecords { get; set; } = 0;

        /// <summary>
        /// Ticket type enumerate path.
        /// </summary>
        public string Enumerate { get; set; } = String.Empty;

        #endregion

        #region Private-Members

        private List<SupportTypeCount> _Objects = new List<SupportTypeCount>();

        #endregion
    }
}
