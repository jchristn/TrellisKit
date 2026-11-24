namespace TrellisKit.Core.Security
{
    using System;

    /// <summary>
    /// An encrypted secret as stored (docs/MULTIPROPERTY_CONTRACT.md section 5.5).
    /// </summary>
    public class SecretCiphertext
    {
        #region Public-Members

        /// <summary>
        /// Base64 of the ciphertext followed by the 16-byte authentication tag.
        /// </summary>
        public string Ciphertext { get; set; } = String.Empty;

        /// <summary>
        /// Base64 of the 12-byte nonce.
        /// </summary>
        public string Nonce { get; set; } = String.Empty;

        /// <summary>
        /// Key id (first 8 hex characters of the key's SHA-256).
        /// </summary>
        public string KeyId { get; set; } = String.Empty;

        #endregion
    }
}
