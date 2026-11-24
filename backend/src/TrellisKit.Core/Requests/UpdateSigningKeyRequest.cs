namespace TrellisKit.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Body of POST /api/v1/admin/updates/signing-keys.
    /// </summary>
    public class UpdateSigningKeyRequest
    {
        /// <summary>
        /// Key id, ^[A-Za-z0-9._-]{1,64}$.
        /// </summary>
        public string KeyId { get; set; } = String.Empty;

        /// <summary>
        /// X.509 certificate PEM (code signing).
        /// </summary>
        public string CertificatePem { get; set; } = String.Empty;

        /// <summary>
        /// PEM RSA private key, stored encrypted and never returned; null when the deployment supplies the key.
        /// </summary>
        public string? PrivateKeyPem { get; set; } = null;

        /// <summary>
        /// Initial status (Standby by default; Active only from a member session).
        /// </summary>
        public UpdateSigningKeyStatusEnum? Status { get; set; } = null;

        /// <summary>
        /// Labels, or null.
        /// </summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>
        /// Tags, or null.
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; } = null;
    }
}
