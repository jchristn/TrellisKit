namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// What a member needs before an awarder sees them as eligible for a rank (table rankrequirements, id prefix rkq_). A null level applies to every level of the option without its own row.
    /// </summary>
    public class RankRequirement : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier (rkq_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Key of the Rank profile field.
        /// </summary>
        public string FieldKey { get; set; } = String.Empty;

        /// <summary>
        /// Option the requirement leads to.
        /// </summary>
        public string OptionKey { get; set; } = String.Empty;

        /// <summary>
        /// Level the requirement leads to, or null for every level of the option without its own row.
        /// </summary>
        public int? Level { get; set; } = null;

        /// <summary>
        /// Days since the member's current verified award (0 to 3650).
        /// </summary>
        public int MinimumDaysInGrade { get; set; } = 0;

        /// <summary>
        /// Check-ins since the member's current verified award (0 to 10000).
        /// </summary>
        public int MinimumAttendance { get; set; } = 0;

        /// <summary>
        /// Notes for awarders (at most 1000 characters).
        /// </summary>
        public string? Notes { get; set; } = null;

        /// <summary>
        /// Curriculum whose required items for this option must be complete, or null.
        /// </summary>
        public string? CurriculumId { get; set; } = null;

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
