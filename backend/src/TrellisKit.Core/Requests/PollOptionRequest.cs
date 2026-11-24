namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// One poll option in a request.
    /// </summary>
    public class PollOptionRequest
    {
        #region Public-Members

        /// <summary>
        /// Label (1 to 140 characters).
        /// </summary>
        public string? Label { get; set; } = null;

        #endregion
    }
}
