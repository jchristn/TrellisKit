namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// What one batch of lesson payloads needs, loaded once per batch.
    /// </summary>
    internal sealed class LessonBatch
    {
        /// <summary>
        /// Levels by key.
        /// </summary>
        internal Dictionary<string, LibraryLevelView> Levels { get; set; } = new Dictionary<string, LibraryLevelView>(StringComparer.Ordinal);

        /// <summary>
        /// Series by id.
        /// </summary>
        internal Dictionary<string, LibrarySeries> Series { get; set; } = new Dictionary<string, LibrarySeries>(StringComparer.Ordinal);

        /// <summary>
        /// Video media by id.
        /// </summary>
        internal Dictionary<string, MediaView> Media { get; set; } = new Dictionary<string, MediaView>(StringComparer.Ordinal);

        /// <summary>
        /// Labels and tags by lesson id.
        /// </summary>
        internal Dictionary<string, ObjectMetadata> Metadata { get; set; } = new Dictionary<string, ObjectMetadata>(StringComparer.Ordinal);

        /// <summary>
        /// The caller's progress by lesson id.
        /// </summary>
        internal Dictionary<string, LessonProgress> Progress { get; set; } = new Dictionary<string, LessonProgress>(StringComparer.Ordinal);

        /// <summary>
        /// Effective library.downloads.enabled.
        /// </summary>
        internal bool DownloadsEnabled { get; set; } = false;

        /// <summary>
        /// Public sample payloads.
        /// </summary>
        internal bool PublicPaths { get; set; } = false;
    }
}
