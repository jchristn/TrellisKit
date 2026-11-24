namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// GET /api/v1/admin/property/settings (bounded by the property settings registry).
    /// </summary>
    public class PropertySettingsResponse
    {
        #region Public-Members

        /// <summary>
        /// Property id.
        /// </summary>
        public string PropertyId { get; set; } = String.Empty;

        /// <summary>
        /// Fields in display order.
        /// </summary>
        public List<PropertySettingField> Fields
        {
            get { return _Fields; }
            set { _Fields = value ?? new List<PropertySettingField>(); }
        }

        #endregion

        #region Private-Members

        private List<PropertySettingField> _Fields = new List<PropertySettingField>();

        #endregion
    }
}
