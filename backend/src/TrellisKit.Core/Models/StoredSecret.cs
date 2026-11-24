namespace TrellisKit.Core.Models
{
    using System;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// One encrypted value read for re-encryption: where it lives, its row key, and its ciphertext.
    /// </summary>
    public class StoredSecret
    {
        #region Public-Members

        /// <summary>
        /// Where the value is stored.
        /// </summary>
        public SecretStoreEnum Store { get; set; } = SecretStoreEnum.PropertySetting;

        /// <summary>
        /// Row key: the setting key (PropertySetting) or the email message id (EmailPayload).
        /// </summary>
        public string RowKey { get; set; } = String.Empty;

        /// <summary>
        /// Ciphertext (base64 of ciphertext plus tag).
        /// </summary>
        public string Ciphertext { get; set; } = String.Empty;

        /// <summary>
        /// Nonce (base64).
        /// </summary>
        public string Nonce { get; set; } = String.Empty;

        /// <summary>
        /// Id of the key the value is encrypted with.
        /// </summary>
        public string KeyId { get; set; } = String.Empty;

        #endregion
    }
}
