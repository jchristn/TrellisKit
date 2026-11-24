namespace TrellisKit.Core.PropertyConfig
{
    using System;

    /// <summary>
    /// One reaction members can add to content.
    /// </summary>
    public class PropertyReaction
    {
        #region Public-Members

        /// <summary>
        /// Stable key stored in the database and sent over the API.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Emoji shown for the reaction.
        /// </summary>
        public string Emoji { get; set; } = String.Empty;

        /// <summary>
        /// Display label.
        /// </summary>
        public string Label { get; set; } = String.Empty;

        #endregion
    }
}
