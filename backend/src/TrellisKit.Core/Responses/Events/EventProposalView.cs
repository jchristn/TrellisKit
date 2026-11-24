namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A member's event proposal (resolved decision 10).
    /// </summary>
    public class EventProposalView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Event identifier (evt_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Title.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// Proposed, Published, Draft, Rejected, Withdrawn, or Canceled.
        /// </summary>
        public EventStatusEnum Status { get; set; } = EventStatusEnum.Proposed;

        /// <summary>
        /// Start.
        /// </summary>
        public DateTime StartUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Time zone.
        /// </summary>
        public string TimeZone { get; set; } = "UTC";

        /// <summary>
        /// Note to the reviewers.
        /// </summary>
        public string? ProposalNote { get; set; } = null;

        /// <summary>
        /// Reviewer's note.
        /// </summary>
        public string? ReviewNote { get; set; } = null;

        /// <summary>
        /// When reviewed.
        /// </summary>
        public DateTime? ReviewedUtc { get; set; } = null;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        #endregion
    }
}
