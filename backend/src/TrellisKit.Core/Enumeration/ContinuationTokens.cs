namespace TrellisKit.Core.Enumeration
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Implementations;

    /// <summary>
    /// Opaque keyset continuation tokens: the fingerprint of the scope they were issued for, the ordering, and the sort
    /// key and id of the last returned object. Clients never parse them; the format may change.
    /// Thread safety: stateless; safe to call concurrently.
    /// </summary>
    public static class ContinuationTokens
    {
        #region Private-Members

        private const int _Version = 1;
        private const int _MaxLength = 4096;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Encode a token.
        /// </summary>
        /// <param name="fingerprint">Scope fingerprint.</param>
        /// <param name="ordering">Ordering name.</param>
        /// <param name="key">Sort key of the last object.</param>
        /// <param name="id">Id of the last object.</param>
        /// <returns>Token.</returns>
        public static string Encode(string fingerprint, string ordering, string? key, string id)
        {
            Dictionary<string, object?> payload = new Dictionary<string, object?>
            {
                { "v", _Version }, { "f", fingerprint }, { "o", ordering }, { "k", key }, { "i", id }
            };
            return Base64Url.Encode(JsonSerializer.SerializeToUtf8Bytes(payload));
        }

        /// <summary>
        /// Decode a token issued for a scope.
        /// </summary>
        /// <param name="token">Token.</param>
        /// <param name="fingerprint">Expected scope fingerprint.</param>
        /// <param name="ordering">Ordering stored in the token.</param>
        /// <param name="key">Sort key stored in the token.</param>
        /// <param name="id">Id stored in the token.</param>
        /// <exception cref="ApiErrorException">400 InvalidContinuationToken when malformed or issued for another scope or filter set.</exception>
        public static void Decode(string token, string fingerprint, out string ordering, out string? key, out string id)
        {
            ordering = String.Empty;
            key = null;
            id = String.Empty;
            if (String.IsNullOrWhiteSpace(token) || token.Length > _MaxLength) throw Invalid();
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(Base64Url.Decode(token.Trim())))
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) throw Invalid();
                    if (!root.TryGetProperty("v", out JsonElement v) || v.ValueKind != JsonValueKind.Number || v.GetInt32() != _Version) throw Invalid();
                    if (!root.TryGetProperty("f", out JsonElement f) || f.GetString() != fingerprint) throw Invalid();
                    if (!root.TryGetProperty("o", out JsonElement o) || o.ValueKind != JsonValueKind.String) throw Invalid();
                    if (!root.TryGetProperty("i", out JsonElement i) || i.ValueKind != JsonValueKind.String) throw Invalid();
                    ordering = o.GetString() ?? String.Empty;
                    id = i.GetString() ?? String.Empty;
                    if (root.TryGetProperty("k", out JsonElement k) && k.ValueKind == JsonValueKind.String) key = k.GetString();
                    if (id.Length == 0) throw Invalid();
                }
            }
            catch (FormatException)
            {
                throw Invalid();
            }
            catch (JsonException)
            {
                throw Invalid();
            }
            catch (InvalidOperationException)
            {
                throw Invalid();
            }
        }

        /// <summary>
        /// The 400 InvalidContinuationToken error.
        /// </summary>
        /// <returns>Exception.</returns>
        public static ApiErrorException Invalid()
        {
            return new ApiErrorException(400, "InvalidContinuationToken", "The continuation token is malformed or was issued for a different list or filter set.");
        }

        #endregion
    }
}
