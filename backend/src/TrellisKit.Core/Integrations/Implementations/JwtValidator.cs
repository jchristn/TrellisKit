namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Validates RS256 JWTs against a JWKS: signature, iss, aud, exp, nbf, and iat.
    /// Thread safe.
    /// </summary>
    internal static class JwtValidator
    {
        #region Internal-Methods

        /// <summary>
        /// Parse and validate an RS256 JWT.
        /// </summary>
        /// <param name="compact">Token text.</param>
        /// <param name="keys">JWKS cache for the issuer.</param>
        /// <param name="options">Claim requirements.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The validated token.</returns>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        /// <exception cref="JwtValidationException">The token is malformed or fails validation.</exception>
        internal static async Task<JwtToken> ValidateRs256Async(string? compact, JwksCache keys, JwtValidationOptions options, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(keys);
            ArgumentNullException.ThrowIfNull(options);

            JwtToken jwt = JwtToken.Parse(compact);
            if (!String.Equals(jwt.Algorithm, "RS256", StringComparison.Ordinal))
            {
                throw new JwtValidationException("Unsupported token algorithm '" + Truncate(jwt.Algorithm) + "'; RS256 is required.");
            }
            if (String.IsNullOrEmpty(jwt.KeyId)) throw new JwtValidationException("The token header has no kid.");

            RSAParameters? parameters = await keys.GetKeyAsync(jwt.KeyId, token).ConfigureAwait(false);
            if (parameters == null) throw new JwtValidationException("The token was signed with an unknown key (kid '" + Truncate(jwt.KeyId) + "').");

            bool valid;
            try
            {
                using (RSA rsa = RSA.Create())
                {
                    rsa.ImportParameters(parameters.Value);
                    valid = rsa.VerifyData(jwt.SigningInput, jwt.Signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                }
            }
            catch (CryptographicException e)
            {
                throw new JwtValidationException("The token signature could not be checked.", e);
            }
            if (!valid) throw new JwtValidationException("The token signature is invalid.");

            ValidateClaims(jwt.Payload, options, DateTime.UtcNow);
            return jwt;
        }

        /// <summary>
        /// Validate registered claims of an already signature-checked payload.
        /// </summary>
        /// <param name="payload">Payload object.</param>
        /// <param name="options">Claim requirements.</param>
        /// <param name="nowUtc">Current time (UTC).</param>
        /// <exception cref="ArgumentNullException">The options are null.</exception>
        /// <exception cref="JwtValidationException">A claim fails validation.</exception>
        internal static void ValidateClaims(JsonElement payload, JwtValidationOptions options, DateTime nowUtc)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (options.ValidIssuers.Count < 1 || options.ValidAudiences.Count < 1)
            {
                throw new JwtValidationException("No accepted issuers or audiences are configured.");
            }

            string? issuer = JsonClaims.GetString(payload, "iss");
            if (issuer == null || !options.ValidIssuers.Contains(issuer, StringComparer.Ordinal))
            {
                throw new JwtValidationException("The token issuer is not accepted.");
            }

            List<string> audiences = new List<string>();
            if (payload.TryGetProperty("aud", out JsonElement aud))
            {
                if (aud.ValueKind == JsonValueKind.String)
                {
                    string? single = aud.GetString();
                    if (single != null) audiences.Add(single);
                }
                else if (aud.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in aud.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String && item.GetString() is string value) audiences.Add(value);
                    }
                }
            }
            if (!audiences.Any(a => options.ValidAudiences.Contains(a, StringComparer.Ordinal)))
            {
                throw new JwtValidationException("The token audience is not accepted.");
            }

            DateTime? expires = JsonClaims.GetUnixSeconds(payload, "exp");
            if (expires == null) throw new JwtValidationException("The token has no exp claim.");
            if (nowUtc - options.ClockSkew >= expires.Value) throw new JwtValidationException("The token has expired.");

            DateTime? notBefore = JsonClaims.GetUnixSeconds(payload, "nbf");
            if (notBefore != null && nowUtc + options.ClockSkew < notBefore.Value) throw new JwtValidationException("The token is not valid yet (nbf).");

            DateTime? issuedAt = JsonClaims.GetUnixSeconds(payload, "iat");
            if (issuedAt != null && nowUtc + options.ClockSkew < issuedAt.Value) throw new JwtValidationException("The token was issued in the future (iat).");
        }

        #endregion

        #region Private-Methods

        private static string Truncate(string? value)
        {
            if (value == null) return String.Empty;
            return value.Length <= 64 ? value : value.Substring(0, 64);
        }

        #endregion
    }
}
