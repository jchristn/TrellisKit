namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// The status block of an Idea: { key, label, color, isClosed }.
    /// </summary>
    public class IdeaStatusRefView
    {
        #region Public-Members

        /// <summary>
        /// Status key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Label.
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Hex color, or null.
        /// </summary>
        public string? Color { get; set; } = null;

        /// <summary>
        /// Whether the status is closed.
        /// </summary>
        public bool IsClosed { get; set; } = false;

        #endregion
    }
}
