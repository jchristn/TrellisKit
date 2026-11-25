namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// A member's self-completion or verification of a curriculum item (table curriculumprogress, id prefix cup_). Lesson and Series items are satisfied from library progress and need no row.
    /// </summary>
    public class CurriculumProgress : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier (cup_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Curriculum.
        /// </summary>
        public string CurriculumId { get; set; } = String.Empty;

        /// <summary>
        /// Item.
        /// </summary>
        public string ItemId { get; set; } = String.Empty;

        /// <summary>
        /// Member.
        /// </summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>
        /// SelfComplete or Verified.
        /// </summary>
        public CurriculumProgressStatusEnum Status { get; set; } = CurriculumProgressStatusEnum.SelfComplete;

        /// <summary>
        /// Verifier.
        /// </summary>
        public string? VerifiedBy { get; set; } = null;

        /// <summary>
        /// Verification time.
        /// </summary>
        public DateTime? VerifiedUtc { get; set; } = null;

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
