namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// The few ticket columns the ideas board reads when staff publish or link an idea from a support ticket
    /// (docs/PHASE5_CONTRACT.md section 6.2). Not the support module's ticket model.
    /// </summary>
    public class IdeaTicket
    {
        #region Public-Members

        /// <summary>
        /// Ticket identifier (tkt_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Ticket type key.
        /// </summary>
        public string TypeKey { get; set; } = String.Empty;

        /// <summary>
        /// Title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Body.
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// Requester, or null (deleted account).
        /// </summary>
        public string? RequesterUserId { get; set; } = null;

        /// <summary>
        /// Linked idea, or null.
        /// </summary>
        public string? IdeaId { get; set; } = null;

        #endregion
    }
}
