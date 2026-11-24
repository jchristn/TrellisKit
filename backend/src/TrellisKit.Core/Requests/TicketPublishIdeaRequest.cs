namespace TrellisKit.Core.Requests
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// POST /api/v1/admin/tickets/{id}/publish-idea: publish an Enhancement ticket as an idea.
    /// </summary>
    public class TicketPublishIdeaRequest
    {
        #region Public-Members

        /// <summary>
        /// Title, or null for the ticket title.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Body, or null for the ticket body.
        /// </summary>
        public string? Body { get; set; } = null;

        /// <summary>
        /// Show the requester as the idea's author (default false).
        /// </summary>
        public bool? ShowRequester { get; set; } = null;

        /// <summary>
        /// Audience, or null for the default.
        /// </summary>
        public ContentAudienceEnum? Audience { get; set; } = null;

        #endregion
    }
}
