namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The idea a ticket is linked to.
    /// </summary>
    public class TicketIdeaRef
    {
        #region Public-Members

        /// <summary>
        /// Idea identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Idea title, or null.
        /// </summary>
        public string? Title { get; set; } = null;

        #endregion
    }
}
