namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Text;
    using System.Text.Json;

    /// <summary>
    /// A parsed compact JWS (JWT): decoded header and payload, signing input, and signature. Parsing does not verify anything.
    /// Immutable and thread safe.
    /// </summary>
    internal sealed class JwtToken
    {
        #region Internal-Members

        /// <summary>
        /// Decoded header object.
        /// </summary>
        internal JsonElement Header { get; }

        /// <summary>
        /// Decoded payload object.
        /// </summary>
        internal JsonElement Payload { get; }

        /// <summary>
        /// Decoded payload as JSON text.
        /// </summary>
        internal string PayloadJson { get; }

        /// <summary>
        /// ASCII bytes of "header.payload" (the data that was signed).
        /// </summary>
        internal byte[] SigningInput { get; }

        /// <summary>
        /// Raw signature bytes.
        /// </summary>
        internal byte[] Signature { get; }

        /// <summary>
        /// Header alg, or empty.
        /// </summary>
        internal string Algorithm { get; }

        /// <summary>
        /// Header kid, or null.
        /// </summary>
        internal string? KeyId { get; }

        #endregion

        #region Constructors-and-Factories

        private JwtToken(JsonElement header, JsonElement payload, string payloadJson, byte[] signingInput, byte[] signature)
        {
            Header = header;
            Payload = payload;
            PayloadJson = payloadJson;
            SigningInput = signingInput;
            Signature = signature;
            Algorithm = JsonClaims.GetString(header, "alg") ?? String.Empty;
            KeyId = JsonClaims.GetString(header, "kid");
        }

        /// <summary>
        /// Parse a compact JWS.
        /// </summary>
        /// <param name="compact">Token text.</param>
        /// <returns>Parsed token.</returns>
        /// <exception cref="JwtValidationException">The token is not a well-formed compact JWS with JSON object header and payload.</exception>
        internal static JwtToken Parse(string? compact)
        {
            if (String.IsNullOrWhiteSpace(compact)) throw new JwtValidationException("The token is empty.");
            string trimmed = compact.Trim();
            string[] parts = trimmed.Split('.');
            if (parts.Length != 3 || parts[0].Length == 0 || parts[1].Length == 0 || parts[2].Length == 0)
            {
                throw new JwtValidationException("The token is not a compact JWS (expected three non-empty segments).");
            }

            try
            {
                byte[] headerBytes = Base64Url.Decode(parts[0]);
                byte[] payloadBytes = Base64Url.Decode(parts[1]);
                byte[] signature = Base64Url.Decode(parts[2]);
                JsonElement header = ParseObject(headerBytes, "header");
                JsonElement payload = ParseObject(payloadBytes, "payload");
                byte[] signingInput = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
                return new JwtToken(header, payload, Encoding.UTF8.GetString(payloadBytes), signingInput, signature);
            }
            catch (FormatException e)
            {
                throw new JwtValidationException("The token contains invalid base64url.", e);
            }
            catch (JsonException e)
            {
                throw new JwtValidationException("The token header or payload is not valid JSON.", e);
            }
        }

        #endregion

        #region Private-Methods

        private static JsonElement ParseObject(byte[] bytes, string what)
        {
            using (JsonDocument document = JsonDocument.Parse(bytes))
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new JwtValidationException("The token " + what + " is not a JSON object.");
                }
                return document.RootElement.Clone();
            }
        }

        #endregion
    }
}
