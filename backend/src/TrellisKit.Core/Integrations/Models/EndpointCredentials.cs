namespace TrellisKit.Core.Integrations.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Decrypted credentials of one model endpoint. Lives only in memory inside the client factory and the auth handler
    /// (docs/AI_CONTRACT.md section 2.2); never logged, serialized, or returned.
    /// </summary>
    public sealed class EndpointCredentials
    {
        #region Public-Members

        /// <summary>
        /// Auth type.
        /// </summary>
        public ModelAuthTypeEnum AuthType { get; set; } = ModelAuthTypeEnum.None;

        /// <summary>
        /// Header name (Header auth).
        /// </summary>
        public string? HeaderName { get; set; } = null;

        /// <summary>
        /// Query parameter name (QueryParameter auth).
        /// </summary>
        public string? QueryParameter { get; set; } = null;

        /// <summary>
        /// Username (Basic auth).
        /// </summary>
        public string? Username { get; set; } = null;

        /// <summary>
        /// Decrypted auth value, or null.
        /// </summary>
        public string? AuthValue { get; set; } = null;

        /// <summary>
        /// Extra headers with decrypted values, in order.
        /// </summary>
        public List<KeyValuePair<string, string>> ExtraHeaders { get; set; } = new List<KeyValuePair<string, string>>();

        /// <summary>
        /// Every secret value (auth value and extra header values) for exact-match redaction.
        /// </summary>
        public IEnumerable<string> SecretValues
        {
            get
            {
                if (!String.IsNullOrEmpty(AuthValue)) yield return AuthValue;
                foreach (KeyValuePair<string, string> header in ExtraHeaders)
                    if (!String.IsNullOrEmpty(header.Value)) yield return header.Value;
            }
        }

        #endregion
    }
}
