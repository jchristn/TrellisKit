namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// PUT /api/v1/admin/support/ticket-types/order result.
    /// </summary>
    public class TicketTypeOrderResponse
    {
        #region Public-Members

        /// <summary>
        /// Types reordered.
        /// </summary>
        public int Updated { get; set; } = 0;

        /// <summary>
        /// Every ticket type in the new order (at most 50).
        /// </summary>
        public List<TicketTypeView> Objects
        {
            get { return _Objects; }
            set { _Objects = value ?? new List<TicketTypeView>(); }
        }

        #endregion

        #region Private-Members

        private List<TicketTypeView> _Objects = new List<TicketTypeView>();

        #endregion
    }
}
