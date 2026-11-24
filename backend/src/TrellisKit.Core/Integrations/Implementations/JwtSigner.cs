namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json.Nodes;

    /// <summary>
    /// Creates compact JWS tokens for outbound API authentication (RS256 and ES256).
    /// Thread safe when each caller uses its own key instance.
    /// </summary>
    internal static class JwtSigner
    {
        #region Internal-Methods

        /// <summary>
        /// Sign a JWT with RS256.
        /// </summary>
        /// <param name="header">Header object (alg is set to RS256).</param>
        /// <param name="payload">Payload object.</param>
        /// <param name="key">RSA private key.</param>
        /// <returns>Compact token.</returns>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        /// <exception cref="CryptographicException">Signing failed.</exception>
        internal static string SignRs256(JsonObject header, JsonObject payload, RSA key)
        {
            ArgumentNullException.ThrowIfNull(header);
            ArgumentNullException.ThrowIfNull(payload);
            ArgumentNullException.ThrowIfNull(key);
            header["alg"] = "RS256";
            string signingInput = SigningInput(header, payload);
            byte[] signature = key.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return signingInput + "." + Base64Url.Encode(signature);
        }

        /// <summary>
        /// Sign a JWT with ES256 (IEEE P1363 r||s signature).
        /// </summary>
        /// <param name="header">Header object (alg is set to ES256).</param>
        /// <param name="payload">Payload object.</param>
        /// <param name="key">P-256 private key.</param>
        /// <returns>Compact token.</returns>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        /// <exception cref="CryptographicException">Signing failed.</exception>
        internal static string SignEs256(JsonObject header, JsonObject payload, ECDsa key)
        {
            ArgumentNullException.ThrowIfNull(header);
            ArgumentNullException.ThrowIfNull(payload);
            ArgumentNullException.ThrowIfNull(key);
            header["alg"] = "ES256";
            string signingInput = SigningInput(header, payload);
            byte[] signature = key.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            return signingInput + "." + Base64Url.Encode(signature);
        }

        #endregion

        #region Private-Methods

        private static string SigningInput(JsonObject header, JsonObject payload)
        {
            return Base64Url.Encode(Encoding.UTF8.GetBytes(header.ToJsonString())) + "." + Base64Url.Encode(Encoding.UTF8.GetBytes(payload.ToJsonString()));
        }

        #endregion
    }
}
