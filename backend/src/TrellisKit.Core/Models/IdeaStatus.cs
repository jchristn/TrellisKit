namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// A configured idea status row (docs/PHASE5_CONTRACT.md sections 2.4 and 16).
    /// </summary>
    public class IdeaStatus
    {
        #region Public-Members

        /// <summary>
        /// Identifier (ids_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Key (1 to 64 letters, digits, underscores, or hyphens; unique case-insensitively).
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Display label.
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Hex color, or null.
        /// </summary>
        public string? Color { get; set; } = null;

        /// <summary>
        /// Whether ideas in this status are closed (no votes unless ideas.voteOnClosed).
        /// </summary>
        public bool IsClosed { get; set; } = false;

        /// <summary>
        /// Whether new ideas start in this status (exactly one active status is initial).
        /// </summary>
        public bool IsInitial { get; set; } = false;

        /// <summary>
        /// Display order (ascending).
        /// </summary>
        public int SortOrder { get; set; } = 0;

        /// <summary>
        /// Whether the status is retired (kept for existing ideas, not offered for new changes).
        /// </summary>
        public bool IsRetired { get; set; } = false;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Updated (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
