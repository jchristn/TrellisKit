namespace Test.Shared.IntegrationTests
{
    using System;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json.Nodes;

    /// <summary>
    /// Builds JWKS documents and signs JWTs and JWS objects for offline tests.
    /// </summary>
    public static class TestTokens
    {
        #region Public-Members

        /// <summary>
        /// Current Unix time in seconds.
        /// </summary>
        public static long Now
        {
            get { return DateTimeOffset.UtcNow.ToUnixTimeSeconds(); }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Base64url-encode bytes without padding.
        /// </summary>
        /// <param name="data">Bytes.</param>
        /// <returns>Encoded text.</returns>
        public static string Base64Url(byte[] data)
        {
            return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>
        /// Decode base64url text.
        /// </summary>
        /// <param name="text">Encoded text.</param>
        /// <returns>Bytes.</returns>
        public static byte[] FromBase64Url(string text)
        {
            string normalized = text.Replace('-', '+').Replace('_', '/');
            while (normalized.Length % 4 != 0) normalized += "=";
            return Convert.FromBase64String(normalized);
        }

        /// <summary>
        /// Build a JWKS document for RSA keys.
        /// </summary>
        /// <param name="keys">Keys.</param>
        /// <returns>JSON text.</returns>
        public static string Jwks(params TestRsaKey[] keys)
        {
            JsonArray array = new JsonArray();
            foreach (TestRsaKey key in keys)
            {
                RSAParameters parameters = key.Rsa.ExportParameters(false);
                array.Add(new JsonObject
                {
                    ["kty"] = "RSA",
                    ["kid"] = key.Kid,
                    ["use"] = "sig",
                    ["alg"] = "RS256",
                    ["n"] = Base64Url(parameters.Modulus!),
                    ["e"] = Base64Url(parameters.Exponent!)
                });
            }
            return new JsonObject { ["keys"] = array }.ToJsonString();
        }

        /// <summary>
        /// Sign an RS256 JWT.
        /// </summary>
        /// <param name="key">Signing key.</param>
        /// <param name="payload">Payload.</param>
        /// <param name="kid">Header kid; null uses the key's kid.</param>
        /// <returns>Compact JWT.</returns>
        public static string SignRs256(TestRsaKey key, JsonObject payload, string? kid = null)
        {
            JsonObject header = new JsonObject { ["alg"] = "RS256", ["kid"] = kid ?? key.Kid, ["typ"] = "JWT" };
            string input = Segment(header) + "." + Segment(payload);
            byte[] signature = key.Rsa.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return input + "." + Base64Url(signature);
        }

        /// <summary>
        /// Sign an ES256 JWS with an arbitrary header.
        /// </summary>
        /// <param name="key">P-256 key.</param>
        /// <param name="header">Header (alg is set to ES256).</param>
        /// <param name="payload">Payload.</param>
        /// <returns>Compact JWS.</returns>
        public static string SignEs256(ECDsa key, JsonObject header, JsonObject payload)
        {
            header["alg"] = "ES256";
            string input = Segment(header) + "." + Segment(payload);
            byte[] signature = key.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            return input + "." + Base64Url(signature);
        }

        /// <summary>
        /// Replace a compact token's payload with another, keeping the original header and signature.
        /// </summary>
        /// <param name="compact">Token.</param>
        /// <param name="payload">Replacement payload.</param>
        /// <returns>Tampered token.</returns>
        public static string ReplacePayload(string compact, JsonObject payload)
        {
            string[] parts = compact.Split('.');
            return parts[0] + "." + Segment(payload) + "." + parts[2];
        }

        /// <summary>
        /// Decode a compact token's header or payload segment.
        /// </summary>
        /// <param name="compact">Token.</param>
        /// <param name="index">0 for header, 1 for payload.</param>
        /// <returns>JSON object.</returns>
        public static JsonObject DecodeSegment(string compact, int index)
        {
            string[] parts = compact.Split('.');
            return JsonNode.Parse(Encoding.UTF8.GetString(FromBase64Url(parts[index])))!.AsObject();
        }

        /// <summary>
        /// Lowercase SHA-256 hex of a string.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>Hex digest.</returns>
        public static string Sha256Hex(string value)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        }

        #endregion

        #region Private-Methods

        private static string Segment(JsonObject value)
        {
            return Base64Url(Encoding.UTF8.GetBytes(value.ToJsonString()));
        }

        #endregion
    }
}
