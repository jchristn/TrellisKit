namespace TrellisKit.Server.Services
{
    using System;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using TrellisKit.Core.Models;

    /// <summary>
    /// One key of a property's signing key ring: the registry record, its parsed certificate, and the private key when
    /// the server holds one that matches the certificate. Never serialized.
    /// </summary>
    public class UpdateSigningMaterial
    {
        #region Public-Members

        /// <summary>
        /// Registry record.
        /// </summary>
        public UpdateSigningKey Record { get; set; } = new UpdateSigningKey();

        /// <summary>
        /// Parsed certificate, or null when the PEM is invalid.
        /// </summary>
        public X509Certificate2? Certificate { get; set; } = null;

        /// <summary>
        /// Private key, or null when unavailable.
        /// </summary>
        public RSA? PrivateKey { get; set; } = null;

        /// <summary>
        /// Why the key cannot sign, or null.
        /// </summary>
        public string? Problem { get; set; } = null;

        /// <summary>
        /// Key id of the issuing certificate in the ring, or null.
        /// </summary>
        public string? IssuedByKeyId { get; set; } = null;

        /// <summary>
        /// Whether this key can sign.
        /// </summary>
        public bool CanSign
        {
            get { return PrivateKey != null && Certificate != null && Problem == null; }
        }

        #endregion
    }
}
