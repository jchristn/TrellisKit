namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Body of POST /api/v1/library/search: one search across courses (series), lessons, and techniques (catalog
    /// entries), with bounded typed results per kind.
    /// </summary>
    public class LibrarySearchRequest
    {
        #region Public-Members

        /// <summary>
        /// Shortest query in characters.
        /// </summary>
        public const int MinQueryLength = 2;

        /// <summary>
        /// Longest query in characters.
        /// </summary>
        public const int MaxQueryLength = 100;

        /// <summary>
        /// Most results per kind.
        /// </summary>
        public const int MaxResultsPerKindLimit = 25;

        /// <summary>
        /// Default results per kind.
        /// </summary>
        public const int DefaultResultsPerKind = 10;

        /// <summary>
        /// Search text, 2 to 100 characters after trimming.
        /// </summary>
        public string? Query { get; set; } = null;

        /// <summary>
        /// Kinds to search (Series, Lesson, CatalogEntry); null or empty searches every available kind. At most 3.
        /// </summary>
        public List<string>? Kinds { get; set; } = null;

        /// <summary>
        /// Results per kind, 1 to 25. Default 10.
        /// </summary>
        public int? MaxResultsPerKind { get; set; } = null;

        /// <summary>
        /// Labels every result carries (at most 20), or null.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Tags every result carries (at most 20), or null.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        #endregion
    }
}
