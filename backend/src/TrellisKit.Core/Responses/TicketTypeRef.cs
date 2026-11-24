namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A ticket type reference on a ticket.
    /// </summary>
    public class TicketTypeRef
    {
        #region Public-Members

        /// <summary>
        /// Key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Label.
        /// </summary>
        public string Label { get; set; } = String.Empty;

        #endregion
    }
}
