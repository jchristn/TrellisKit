namespace TrellisKit.Server.Services
{
    using System;

    /// <summary>
    /// An Expo Updates code signature over one multipart part body.
    /// </summary>
    public class UpdateSignature
    {
        #region Public-Members

        /// <summary>
        /// Base64 RSASSA-PKCS1-v1_5 SHA-256 signature.
        /// </summary>
        public string Signature { get; set; } = String.Empty;

        /// <summary>
        /// Key id of the key that signed.
        /// </summary>
        public string KeyId { get; set; } = String.Empty;

        /// <summary>
        /// Algorithm (rsa-v1_5-sha256).
        /// </summary>
        public string Algorithm { get; set; } = UpdateSigningService.Algorithm;

        /// <summary>
        /// PEM certificates from the signing key up to, not including, the certificate the binary embeds (the
        /// certificate_chain part), or null when the embedded certificate's own key signed.
        /// </summary>
        public string? CertificateChainPem { get; set; } = null;

        /// <summary>
        /// The expo-signature header value: sig="...", keyid="...", alg="...".
        /// </summary>
        public string HeaderValue
        {
            get { return "sig=" + StructuredFields.Quote(Signature) + ", keyid=" + StructuredFields.Quote(KeyId) + ", alg=" + StructuredFields.Quote(Algorithm); }
        }

        #endregion
    }
}
