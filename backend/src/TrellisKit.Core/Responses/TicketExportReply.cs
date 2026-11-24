namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One reply of an exported ticket (internal notes are never exported).
    /// </summary>
    public class TicketExportReply
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Member or Staff.
        /// </summary>
        public TicketAuthorRoleEnum AuthorRole { get; set; } = TicketAuthorRoleEnum.Member;

        /// <summary>
        /// Body.
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
