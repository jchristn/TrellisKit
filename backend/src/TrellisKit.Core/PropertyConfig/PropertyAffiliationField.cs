namespace TrellisKit.Core.PropertyConfig
{
    using System;

    /// <summary>
    /// The affiliation profile field (the group a member belongs to).
    /// </summary>
    public class PropertyAffiliationField
    {
        #region Public-Members

        /// <summary>
        /// Whether members can set an affiliation.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Display label.
        /// </summary>
        public string Label { get; set; } = String.Empty;

        #endregion
    }
}
