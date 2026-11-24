namespace TrellisKit.Core.PropertyConfig
{
    using System;

    /// <summary>
    /// One idea status in property.json ideas.statuses (docs/PHASE5_CONTRACT.md section 2.4), seeded when the property
    /// has no idea statuses yet.
    /// </summary>
    public class PropertyIdeaStatus
    {
        #region Public-Members

        /// <summary>
        /// Key (1 to 64 letters, digits, underscores, or hyphens; unique).
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Display label.
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Hex color such as #2E7D32, or null.
        /// </summary>
        public string? Color { get; set; } = null;

        /// <summary>
        /// Whether the status is closed (no votes unless ideas.voteOnClosed). Default false.
        /// </summary>
        public bool IsClosed { get; set; } = false;

        /// <summary>
        /// Whether new ideas start in this status (exactly one status is initial). Default false.
        /// </summary>
        public bool IsInitial { get; set; } = false;

        #endregion
    }
}
