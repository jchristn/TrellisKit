namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// Members holding one option of a select or rank field.
    /// </summary>
    public class ProfileOptionCount : TrellisKit.Core.Responses.CatalogEntryMetadata
    {
        #region Public-Members

        /// <summary>
        /// Option key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Members.
        /// </summary>
        public long Count { get; set; } = 0;

        #endregion
    }
}
