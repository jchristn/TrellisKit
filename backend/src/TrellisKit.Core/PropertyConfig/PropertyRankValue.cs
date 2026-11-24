namespace TrellisKit.Core.PropertyConfig
{
    using System;

    /// <summary>
    /// One rank a member can hold.
    /// </summary>
    public class PropertyRankValue
    {
        #region Public-Members

        /// <summary>
        /// Stable key stored in the database and sent over the API.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Display label.
        /// </summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>
        /// Display color as a hex value, for example #1F5FBF.
        /// </summary>
        public string Color { get; set; } = String.Empty;

        #endregion
    }
}
