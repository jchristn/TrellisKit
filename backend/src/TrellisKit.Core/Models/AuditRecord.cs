namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// A durable record of an administrative or account action.
    /// </summary>
    public class AuditRecord : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier (prefix aud_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Member who acted, or null for the admin token or the system.
        /// </summary>
        public string? ActorUserId { get; set; } = null;

        /// <summary>
        /// Actor display name at the time, or null.
        /// </summary>
        public string? ActorName { get; set; } = null;

        /// <summary>
        /// Action, for example member.ban or signupCode.create.
        /// </summary>
        public string Action { get; set; } = String.Empty;

        /// <summary>
        /// Target type, for example User, SignupCode, Settings, or null.
        /// </summary>
        public string? TargetType { get; set; } = null;

        /// <summary>
        /// Target identifier, or null.
        /// </summary>
        public string? TargetId { get; set; } = null;

        /// <summary>
        /// Human-readable summary, or null.
        /// </summary>
        public string? Summary { get; set; } = null;

        /// <summary>
        /// Source IP, or null.
        /// </summary>
        public string? SourceIp { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

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
