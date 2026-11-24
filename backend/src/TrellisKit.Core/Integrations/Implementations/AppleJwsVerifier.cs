namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;
    using System.Text.Json;

    /// <summary>
    /// Verifies App Store JWS objects (signed transactions, renewal info, and notifications): the x5c header chain must
    /// build to the trusted root with the Apple marker extensions on the leaf and intermediate, and the ES256 signature
    /// must verify with the leaf key. Revocation is not checked.
    /// Thread safe.
    /// </summary>
    internal static class AppleJwsVerifier
    {
        #region Internal-Members

        /// <summary>
        /// Extension that marks an App Store receipt signing (leaf) certificate.
        /// </summary>
        internal const string LeafMarkerOid = "1.2.840.113635.100.6.11.1";

        /// <summary>
        /// Extension that marks the Apple Worldwide Developer Relations intermediate certificate.
        /// </summary>
        internal const string IntermediateMarkerOid = "1.2.840.113635.100.6.2.1";

        #endregion

        #region Internal-Methods

        /// <summary>
        /// Verify a JWS and return its parsed form.
        /// </summary>
        /// <param name="jws">Compact JWS.</param>
        /// <param name="trustedRoot">Trusted root certificate.</param>
        /// <param name="verificationTimeUtc">Time at which certificate validity is evaluated (UTC).</param>
        /// <returns>Verified token.</returns>
        /// <exception cref="ArgumentNullException">The root is null.</exception>
        /// <exception cref="JwtValidationException">The JWS is malformed or fails verification.</exception>
        internal static JwtToken Verify(string? jws, X509Certificate2 trustedRoot, DateTime verificationTimeUtc)
        {
            ArgumentNullException.ThrowIfNull(trustedRoot);

            JwtToken token = JwtToken.Parse(jws);
            if (!String.Equals(token.Algorithm, "ES256", StringComparison.Ordinal))
            {
                throw new JwtValidationException("Unsupported JWS algorithm; ES256 is required.");
            }

            List<byte[]> encoded = ReadChain(token.Header);
            List<X509Certificate2> certificates = new List<X509Certificate2>();
            try
            {
                foreach (byte[] der in encoded)
                {
                    try
                    {
                        certificates.Add(X509CertificateLoader.LoadCertificate(der));
                    }
                    catch (CryptographicException e)
                    {
                        throw new JwtValidationException("The x5c header contains an invalid certificate.", e);
                    }
                }

                X509Certificate2 leaf = certificates[0];
                X509Certificate2 intermediate = certificates[1];
                if (leaf.Extensions[LeafMarkerOid] == null) throw new JwtValidationException("The signing certificate is not an App Store signing certificate.");
                if (intermediate.Extensions[IntermediateMarkerOid] == null) throw new JwtValidationException("The intermediate certificate is not an Apple WWDR certificate.");

                BuildChain(leaf, intermediate, trustedRoot, verificationTimeUtc);

                using (ECDsa? key = leaf.GetECDsaPublicKey())
                {
                    if (key == null) throw new JwtValidationException("The signing certificate does not hold an EC key.");
                    bool valid;
                    try
                    {
                        valid = key.VerifyData(token.SigningInput, token.Signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
                    }
                    catch (CryptographicException e)
                    {
                        throw new JwtValidationException("The JWS signature could not be checked.", e);
                    }
                    if (!valid) throw new JwtValidationException("The JWS signature is invalid.");
                }
                return token;
            }
            finally
            {
                foreach (X509Certificate2 certificate in certificates) certificate.Dispose();
            }
        }

        #endregion

        #region Private-Methods

        private static List<byte[]> ReadChain(JsonElement header)
        {
            if (!JsonClaims.TryGetArray(header, "x5c", out JsonElement x5c)) throw new JwtValidationException("The JWS header has no x5c certificate chain.");
            List<byte[]> encoded = new List<byte[]>();
            foreach (JsonElement item in x5c.EnumerateArray())
            {
                string? text = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                if (String.IsNullOrEmpty(text)) throw new JwtValidationException("The x5c header contains an empty entry.");
                try
                {
                    encoded.Add(Convert.FromBase64String(text));
                }
                catch (FormatException e)
                {
                    throw new JwtValidationException("The x5c header contains invalid base64.", e);
                }
            }
            if (encoded.Count < 2 || encoded.Count > 3) throw new JwtValidationException("The x5c header must contain the leaf, intermediate, and root certificates.");
            return encoded;
        }

        private static void BuildChain(X509Certificate2 leaf, X509Certificate2 intermediate, X509Certificate2 trustedRoot, DateTime verificationTimeUtc)
        {
            using (X509Chain chain = new X509Chain())
            {
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(trustedRoot);
                chain.ChainPolicy.ExtraStore.Add(intermediate);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
                chain.ChainPolicy.DisableCertificateDownloads = true;
                chain.ChainPolicy.VerificationTime = verificationTimeUtc;

                try
                {
                    bool built = chain.Build(leaf);
                    if (!built)
                    {
                        string statuses = String.Join(", ", chain.ChainStatus.Select(s => s.Status.ToString()).Distinct());
                        throw new JwtValidationException("The certificate chain does not build to the trusted Apple root (" + statuses + ").");
                    }
                    if (chain.ChainElements.Count != 3)
                    {
                        throw new JwtValidationException("The certificate chain has " + chain.ChainElements.Count + " elements; expected leaf, intermediate, and root.");
                    }
                    if (!chain.ChainElements[1].Certificate.RawDataMemory.Span.SequenceEqual(intermediate.RawDataMemory.Span))
                    {
                        throw new JwtValidationException("The certificate chain does not use the supplied intermediate.");
                    }
                    if (!chain.ChainElements[2].Certificate.RawDataMemory.Span.SequenceEqual(trustedRoot.RawDataMemory.Span))
                    {
                        throw new JwtValidationException("The certificate chain does not end at the trusted Apple root.");
                    }
                }
                finally
                {
                    foreach (X509ChainElement element in chain.ChainElements)
                    {
                        X509Certificate2 copy = element.Certificate;
                        if (!ReferenceEquals(copy, leaf) && !ReferenceEquals(copy, intermediate) && !ReferenceEquals(copy, trustedRoot)) copy.Dispose();
                    }
                }
            }
        }

        #endregion
    }
}
