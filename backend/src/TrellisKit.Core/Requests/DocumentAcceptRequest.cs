namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Body of POST /api/v1/me/documents/accept.
    /// </summary>
    public class DocumentAcceptRequest
    {
        #region Public-Members

        /// <summary>
        /// Whether the member checked the terms checkbox, which accepts every document it covers (AcceptedWithTerms, and
        /// RequiredAtSignup without its own checkbox) at its current version. Default false.
        /// </summary>
        public bool AcceptTerms { get; set; } = false;

        /// <summary>
        /// Documents accepted explicitly (at most 30), or null.
        /// </summary>
        public List<DocumentAcceptanceItem>? Documents { get; set; } = null;

        #endregion
    }
}
