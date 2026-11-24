namespace TrellisKit.Core.Models
{
    using System;

    /// <summary>
    /// One stored per-property setting (propertysettings). Secret values are stored only as ciphertext.
    /// </summary>
    public class PropertySettingRow
    {
        #region Public-Members

        /// <summary>
        /// Setting key.
        /// </summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// Non-secret value in its string form, or null.
        /// </summary>
        public string? ValueText { get; set; } = null;

        /// <summary>
        /// Secret ciphertext (base64), or null.
        /// </summary>
        public string? Ciphertext { get; set; } = null;

        /// <summary>
        /// Secret nonce (base64), or null.
        /// </summary>
        public string? Nonce { get; set; } = null;

        /// <summary>
        /// Secrets key id, or null.
        /// </summary>
        public string? KeyId { get; set; } = null;

        /// <summary>
        /// Who changed it.
        /// </summary>
        public string? UpdatedByUserId { get; set; } = null;

        /// <summary>
        /// When it changed (UTC).
        /// </summary>
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
