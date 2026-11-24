namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// One reaction of the property's set.
    /// </summary>
    public class ReactionSetItem : CatalogEntryMetadata
    {
        #region Public-Members

        /// <summary>
        /// Key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Glyph.
        /// </summary>
        public string Emoji { get; set; } = String.Empty;

        /// <summary>
        /// Accessible name.
        /// </summary>
        public string Label { get; set; } = String.Empty;

        #endregion
    }
}
