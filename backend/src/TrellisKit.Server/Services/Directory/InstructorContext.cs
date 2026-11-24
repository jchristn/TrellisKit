namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Data loaded once for a page of instructor views.
    /// </summary>
    public class InstructorContext
    {
        #region Public-Members

        /// <summary>
        /// Directory configuration.
        /// </summary>
        public DirectoryConfig Config { get; set; } = new DirectoryConfig();

        /// <summary>
        /// The configured Rank profile field, or null.
        /// </summary>
        public ProfileField? RankField { get; set; } = null;

        /// <summary>
        /// Labels and tags by instructor id.
        /// </summary>
        public Dictionary<string, ObjectMetadata> Metadata { get; set; } = new Dictionary<string, ObjectMetadata>(StringComparer.Ordinal);

        /// <summary>
        /// Photos by instructor id.
        /// </summary>
        public Dictionary<string, List<MediaView>> Photos { get; set; } = new Dictionary<string, List<MediaView>>(StringComparer.Ordinal);

        /// <summary>
        /// Linked member summaries by user id.
        /// </summary>
        public Dictionary<string, UserSummary> Users { get; set; } = new Dictionary<string, UserSummary>(StringComparer.Ordinal);

        /// <summary>
        /// Roles at the venue being listed, by instructor id.
        /// </summary>
        public Dictionary<string, VenueInstructorRoleEnum> Roles { get; set; } = new Dictionary<string, VenueInstructorRoleEnum>(StringComparer.Ordinal);

        /// <summary>
        /// Pending submission counts by instructor id (admin views).
        /// </summary>
        public Dictionary<string, int> Pending { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>
        /// Whether the caller is already linked to an instructor entry.
        /// </summary>
        public bool CallerLinked { get; set; } = false;

        #endregion
    }
}
