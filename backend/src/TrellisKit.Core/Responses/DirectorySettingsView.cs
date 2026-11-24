namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// GET /api/v1/admin/directory/settings: the stored configuration and the per-property geocoder, map, and public listing settings (secrets never returned).
    /// </summary>
    public class DirectorySettingsView
    {
        #region Public-Members

        /// <summary>
        /// Labels and switches.
        /// </summary>
        public DirectoryConfigView Config { get; set; } = new DirectoryConfigView();

        /// <summary>
        /// directory.* property settings.
        /// </summary>
        public List<PropertySettingField> Settings { get; set; } = new List<PropertySettingField>();

        /// <summary>
        /// Whether the selected geocoder has its required values.
        /// </summary>
        public bool GeocoderConfigured { get; set; } = false;

        #endregion
    }
}
