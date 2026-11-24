namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// An active session.
    /// </summary>
    public class SessionView : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Client.
        /// </summary>
        public ClientTypeEnum Client { get; set; } = ClientTypeEnum.Other;

        /// <summary>
        /// Created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last used.
        /// </summary>
        public DateTime LastUsedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Expiry.
        /// </summary>
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Source IP at sign-in.
        /// </summary>
        public string? SourceIp { get; set; } = null;

        /// <summary>
        /// User agent at sign-in.
        /// </summary>
        public string? UserAgent { get; set; } = null;

        /// <summary>
        /// Labels visible to the caller (staff: entries only for staff). Never null.
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Tags visible to the caller (staff: keys only for staff). Never null.
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(); }
        }

        #endregion

        #region Private-Members

        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();

        #endregion
    }
}
