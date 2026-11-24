namespace TrellisKit.Core.Models
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A live update code signing key and its certificate (table updatesigningkeys). Private material never leaves the server.
    /// </summary>
    public class UpdateSigningKey
    {
        /// <summary>
        /// Identifier (usk_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Key id (the keyid builds embed, for example main or standby).
        /// </summary>
        public string KeyId { get; set; } = String.Empty;

        /// <summary>
        /// Algorithm.
        /// </summary>
        public string Algorithm { get; set; } = "rsa-v1_5-sha256";

        /// <summary>
        /// RSA key size.
        /// </summary>
        public int KeyBits { get; set; } = 0;

        /// <summary>
        /// Where the private key comes from.
        /// </summary>
        public UpdateSigningKeySourceEnum Source { get; set; } = UpdateSigningKeySourceEnum.Environment;

        /// <summary>
        /// X.509 certificate PEM.
        /// </summary>
        public string CertificatePem { get; set; } = String.Empty;

        /// <summary>
        /// SHA-256 hex of the certificate DER.
        /// </summary>
        public string CertificateSha256 { get; set; } = String.Empty;

        /// <summary>
        /// Certificate expiry.
        /// </summary>
        public DateTime? CertificateNotAfterUtc { get; set; } = null;

        /// <summary>
        /// Whether the certificate is a CA (can anchor a certificate chain).
        /// </summary>
        public bool IsCertificateAuthority { get; set; } = false;

        /// <summary>
        /// AES-256-GCM ciphertext (base64) of a stored private key, or null.
        /// </summary>
        public string? PrivateKeyCiphertext { get; set; } = null;

        /// <summary>
        /// Nonce (base64), or null.
        /// </summary>
        public string? PrivateKeyNonce { get; set; } = null;

        /// <summary>
        /// First 8 hex characters of SHA-256 of the encryption key, or null.
        /// </summary>
        public string? PrivateKeyKeyId { get; set; } = null;

        /// <summary>
        /// Role in the key ring.
        /// </summary>
        public UpdateSigningKeyStatusEnum Status { get; set; } = UpdateSigningKeyStatusEnum.Standby;

        /// <summary>
        /// Last signature, or null.
        /// </summary>
        public DateTime? LastUsedUtc { get; set; } = null;

        /// <summary>
        /// Created time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Retire time, or null.
        /// </summary>
        public DateTime? RetiredUtc { get; set; } = null;
    }
}
