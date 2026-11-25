namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A curriculum with the viewer's progress; detail responses add the items.
    /// </summary>
    public class CurriculumView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier (cur_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Rank field whose options group the items.
        /// </summary>
        public string? FieldKey { get; set; } = null;

        /// <summary>
        /// Visibility.
        /// </summary>
        public CurriculumVisibilityEnum Visibility { get; set; } = CurriculumVisibilityEnum.Members;

        /// <summary>
        /// Status.
        /// </summary>
        public CurriculumStatusEnum Status { get; set; } = CurriculumStatusEnum.Draft;

        /// <summary>
        /// Sort position.
        /// </summary>
        public int Position { get; set; } = 0;

        /// <summary>
        /// Items.
        /// </summary>
        public int ItemCount { get; set; } = 0;

        /// <summary>
        /// The viewer's (or inspected member's) progress; null in staff lists.
        /// </summary>
        public CurriculumProgressSummary? Progress { get; set; } = null;

        /// <summary>
        /// Detail only: items in order (at most 500).
        /// </summary>
        public List<CurriculumItemView>? Items { get; set; } = null;

        /// <summary>
        /// Member whose progress is shown, or null.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// First publication time.
        /// </summary>
        public DateTime? PublishedUtc { get; set; } = null;

        /// <summary>
        /// Creation time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update time.
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
