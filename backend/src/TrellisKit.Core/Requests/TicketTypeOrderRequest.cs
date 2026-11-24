namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// PUT /api/v1/admin/support/ticket-types/order body.
    /// </summary>
    public class TicketTypeOrderRequest
    {
        #region Public-Members

        /// <summary>
        /// Ticket type identifiers in the new order (types not listed keep their relative order after them).
        /// </summary>
        public List<string>? TicketTypeIds { get; set; } = null;

        #endregion
    }
}
