namespace TrellisKit.Core.Responses
{
    using System.Collections.Generic;

    /// <summary>
    /// All editable settings.
    /// </summary>
    public class SettingsResponse
    {
        #region Public-Members

        /// <summary>
        /// Fields in display order. Never null.
        /// </summary>
        public List<SettingField> Fields
        {
            get { return _Fields; }
            set { _Fields = value ?? new List<SettingField>(); }
        }

        #endregion

        #region Private-Members

        private List<SettingField> _Fields = new List<SettingField>();

        #endregion
    }
}
