namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;

    /// <summary>
    /// Body of PUT /api/v1/admin/property/settings.
    /// </summary>
    public class PropertySettingsUpdateRequest
    {
        #region Public-Members

        /// <summary>
        /// Keys and new values (secrets are encrypted at rest).
        /// </summary>
        public Dictionary<string, JsonElement> Values
        {
            get { return _Values; }
            set { _Values = value ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal); }
        }

        /// <summary>
        /// Keys to remove (empty the value).
        /// </summary>
        public List<string> Clear
        {
            get { return _Clear; }
            set { _Clear = value ?? new List<string>(); }
        }

        #endregion

        #region Private-Members

        private Dictionary<string, JsonElement> _Values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        private List<string> _Clear = new List<string>();

        #endregion
    }
}
