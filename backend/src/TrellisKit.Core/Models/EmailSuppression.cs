namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// A suppressed address: the engine sends no email to it (docs/TRELLISKIT.md section 12.1).
    /// </summary>
    public class EmailSuppression : IMetadataPayload
    {
        #region Public-Members

        /// <summary>
        /// Identifier (esu_ prefix).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Address.
        /// </summary>
        public string Email { get; set; } = String.Empty;

        /// <summary>
        /// Reason.
        /// </summary>
        public EmailSuppressionReasonEnum Reason { get; set; } = EmailSuppressionReasonEnum.Manual;

        /// <summary>
        /// Source: webhook event name or staff.
        /// </summary>
        public string Source { get; set; } = String.Empty;

        /// <summary>
        /// Staff note.
        /// </summary>
        public string? Note { get; set; } = null;

        /// <summary>
        /// Added (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(StringComparer.Ordinal); }
        }

        #endregion

        #region Private-Members

        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>(StringComparer.Ordinal);

        #endregion
    }
}
