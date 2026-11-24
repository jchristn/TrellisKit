namespace TrellisKit.Core.Responses
{
    using System;

    /// <summary>
    /// The DNS TXT ownership challenge of a domain added by a property administrator (resolved decision 6).
    /// </summary>
    public class DomainVerificationView
    {
        #region Public-Members

        /// <summary>
        /// TXT record name to create, for example _trelliskit-verify.www.example.test.
        /// </summary>
        public string RecordName { get; set; } = String.Empty;

        /// <summary>
        /// TXT record value, for example trelliskit-verify=abc123.
        /// </summary>
        public string RecordValue { get; set; } = String.Empty;

        /// <summary>
        /// Last check (UTC), or null.
        /// </summary>
        public DateTime? LastCheckedUtc { get; set; } = null;

        /// <summary>
        /// Why the last check failed, or null.
        /// </summary>
        public string? Error { get; set; } = null;

        #endregion
    }
}
