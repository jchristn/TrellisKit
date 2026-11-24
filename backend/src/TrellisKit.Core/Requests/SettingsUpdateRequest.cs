namespace TrellisKit.Core.Requests
{
    using System.Collections.Generic;
    using System.Text.Json;

    /// <summary>
    /// Settings update. Only changed keys are sent.
    /// Values are <see cref="JsonElement"/> because each key carries a different type (boolean, integer, string, or enum
    /// name); the settings registry validates and converts each value against the key's declared type.
    /// </summary>
    public class SettingsUpdateRequest
    {
        #region Public-Members

        /// <summary>
        /// New values keyed by setting key, for example "requestHistory.retentionDays". Never null.
        /// </summary>
        public Dictionary<string, JsonElement> Values
        {
            get { return _Values; }
            set { _Values = value ?? new Dictionary<string, JsonElement>(); }
        }

        #endregion

        #region Private-Members

        private Dictionary<string, JsonElement> _Values = new Dictionary<string, JsonElement>();

        #endregion
    }
}
