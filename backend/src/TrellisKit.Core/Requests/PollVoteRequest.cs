namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// PUT /api/v1/polls/{id}/votes body.
    /// </summary>
    public class PollVoteRequest
    {
        #region Public-Members

        /// <summary>
        /// Chosen option identifiers.
        /// </summary>
        public List<string>? OptionIds { get; set; } = null;

        #endregion
    }
}
