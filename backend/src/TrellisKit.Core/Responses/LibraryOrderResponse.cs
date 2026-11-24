namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// Result of an order request.
    /// </summary>
    public class LibraryOrderResponse
    {
        #region Public-Members

        /// <summary>
        /// Rows reordered.
        /// </summary>
        public int Updated { get; set; } = 0;

        #endregion
    }
}
