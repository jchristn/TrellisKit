namespace TrellisKit.Core.Integrations.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A presigned single PUT: the client sends exactly these headers and nothing else.
    /// </summary>
    public class PresignedUpload
    {
        #region Public-Members

        /// <summary>
        /// HTTP method, always PUT.
        /// </summary>
        public string Method { get; set; } = "PUT";

        /// <summary>
        /// Absolute URL.
        /// </summary>
        public string Url { get; set; } = String.Empty;

        /// <summary>
        /// Headers the client must send. Never null.
        /// </summary>
        public Dictionary<string, string> Headers
        {
            get { return _Headers; }
            set { _Headers = value ?? new Dictionary<string, string>(); }
        }

        #endregion

        #region Private-Members

        private Dictionary<string, string> _Headers = new Dictionary<string, string>();

        #endregion
    }
}
