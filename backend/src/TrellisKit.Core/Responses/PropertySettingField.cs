namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// A per-property setting (docs/MULTIPROPERTY_CONTRACT.md section 5.7): the settings field shape plus its scope and whether
    /// the deployment value applies. Secret values are never returned (value null, isSet tells whether one is stored).
    /// </summary>
    public class PropertySettingField : SettingField
    {
        #region Public-Members

        /// <summary>
        /// Scope: Property.
        /// </summary>
        public string Scope { get; set; } = "Property";

        /// <summary>
        /// True when the property has no value and the deployment value applies (SingleProperty only; value shows it unless secret).
        /// </summary>
        public bool Inherited { get; set; } = false;

        #endregion
    }
}
