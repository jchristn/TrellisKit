namespace TrellisKit.Core.Responses
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A linked identity in the account export.
    /// </summary>
    public class IdentityExportItem
    {
        #region Public-Members

        /// <summary>
        /// Provider.
        /// </summary>
        public ExternalProviderEnum Provider { get; set; } = ExternalProviderEnum.Apple;

        /// <summary>
        /// Email.
        /// </summary>
        public string? Email { get; set; } = null;

        /// <summary>
        /// Verified.
        /// </summary>
        public bool EmailVerified { get; set; } = false;

        /// <summary>
        /// Linked.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last used.
        /// </summary>
        public DateTime? LastUsedUtc { get; set; } = null;

        #endregion
    }
}
