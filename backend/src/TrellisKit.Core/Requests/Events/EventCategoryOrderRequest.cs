namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Order the event categories.
    /// </summary>
    public class EventCategoryOrderRequest
    {
        #region Public-Members

        /// <summary>
        /// Every category id in the new order.
        /// </summary>
        public List<string>? CategoryIds { get; set; } = null;

        #endregion
    }
}
