namespace TrellisKit.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// UpdateSigningKey payload; no private material.
    /// </summary>
    public class UpdateSigningKeyView
    {
        /// <summary>
        /// Identifier (usk_).
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Key id.
        /// </summary>
        public string KeyId { get; set; } = String.Empty;

        /// <summary>
        /// Algorithm.
        /// </summary>
        public string Algorithm { get; set; } = String.Empty;

        /// <summary>
        /// RSA key size.
        /// </summary>
        public int KeyBits { get; set; } = 0;

        /// <summary>
        /// Private key source.
        /// </summary>
        public UpdateSigningKeySourceEnum Source { get; set; } = UpdateSigningKeySourceEnum.Environment;

        /// <summary>
        /// Whether the server holds a private key that matches the certificate.
        /// </summary>
        public bool PrivateKeyAvailable { get; set; } = false;

        /// <summary>
        /// Certificate fingerprint (SHA-256 hex).
        /// </summary>
        public string CertificateSha256 { get; set; } = String.Empty;

        /// <summary>
        /// Certificate expiry.
        /// </summary>
        public DateTime? CertificateNotAfterUtc { get; set; } = null;

        /// <summary>
        /// Certificate subject.
        /// </summary>
        public string CertificateSubject { get; set; } = String.Empty;

        /// <summary>
        /// Key id of the issuing certificate in the key ring, or null when self-signed or unknown.
        /// </summary>
        public string? IssuedByKeyId { get; set; } = null;

        /// <summary>
        /// Whether the certificate can anchor a chain (rotation without a new binary).
        /// </summary>
        public bool IsCertificateAuthority { get; set; } = false;

        /// <summary>
        /// Status.
        /// </summary>
        public UpdateSigningKeyStatusEnum Status { get; set; } = UpdateSigningKeyStatusEnum.Standby;

        /// <summary>
        /// Last signature, or null.
        /// </summary>
        public DateTime? LastUsedUtc { get; set; } = null;

        /// <summary>
        /// Labels.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Tags.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

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
