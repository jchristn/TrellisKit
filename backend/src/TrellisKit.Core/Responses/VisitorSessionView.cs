namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A visitor token for public assistants (docs/AI_CONTRACT.md section 13.4).
    /// </summary>
    public class VisitorSessionView
    {
        #region Public-Members

        /// <summary>
        /// Visitor id.
        /// </summary>
        public string VisitorId { get; set; } = String.Empty;

        /// <summary>
        /// Opaque token sent as X-TK-Visitor-Token.
        /// </summary>
        public string VisitorToken { get; set; } = String.Empty;

        #endregion
    }
}
