namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// A configured option in the public property document (a directory program, an event category, or a library level).
    /// </summary>
    public class PublicOption
    {
        #region Public-Members

        /// <summary>
        /// Stable key.
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

        #endregion
    }
}
