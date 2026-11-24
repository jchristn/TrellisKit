namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Integrations.Interfaces;

    /// <summary>
    /// Issues and validates the per-object tokens of private media URLs (GET /api/v1/media/private/{token}/{file}). A token
    /// is the authenticated encryption (the integrations token protector, AES-GCM) of { typ: "pmedia", pid, mid, pur, scp,
    /// uid, exp }, so it cannot be forged or altered and names exactly one media item, one scope, one viewer, and an expiry.
    /// Thread safety: stateless; safe to call concurrently.
    /// </summary>
    public class PrivateMediaTokens
    {
        #region Public-Members

        /// <summary>
        /// Viewer key of URLs issued to the break-glass admin token (a SystemAdmin without a member account).
        /// </summary>
        public const string BreakGlassViewer = "*admin";

        #endregion

        #region Private-Members

        private const string _Type = "pmedia";
        private readonly ITokenProtector _Protector;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="protector">Token protector.</param>
        /// <exception cref="ArgumentNullException">The protector is null.</exception>
        public PrivateMediaTokens(ITokenProtector protector)
        {
            _Protector = protector ?? throw new ArgumentNullException(nameof(protector));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Issue a token.
        /// </summary>
        /// <param name="grant">Claims.</param>
        /// <returns>URL-safe token.</returns>
        /// <exception cref="ArgumentNullException">The grant is null.</exception>
        public string Issue(PrivateMediaGrant grant)
        {
            ArgumentNullException.ThrowIfNull(grant);
            Dictionary<string, object> payload = new Dictionary<string, object>
            {
                { "typ", _Type },
                { "pid", grant.PropertyId },
                { "mid", grant.MediaId },
                { "pur", grant.Purpose.ToString() },
                { "scp", grant.ScopeId },
                { "uid", grant.ViewerKey },
                { "exp", new DateTimeOffset(DateTime.SpecifyKind(grant.ExpiresUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds() }
            };
            return _Protector.Protect(JsonSerializer.SerializeToUtf8Bytes(payload));
        }

        /// <summary>
        /// Read a token's claims (expiry is not checked here, so callers can tell an expired URL from a forged one).
        /// </summary>
        /// <param name="token">Token.</param>
        /// <returns>Claims, or null when the token is malformed, forged, or not a private media token.</returns>
        public PrivateMediaGrant? Read(string? token)
        {
            if (String.IsNullOrWhiteSpace(token) || token.Length > 4096) return null;
            byte[]? plaintext;
            try
            {
                plaintext = _Protector.Unprotect(token);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
            catch (FormatException)
            {
                return null;
            }
            if (plaintext == null) return null;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(plaintext))
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) return null;
                    if (Text(root, "typ") != _Type) return null;
                    string? pid = Text(root, "pid");
                    string? mid = Text(root, "mid");
                    string? pur = Text(root, "pur");
                    string? scp = Text(root, "scp");
                    string? uid = Text(root, "uid");
                    if (pid == null || mid == null || pur == null || scp == null || uid == null) return null;
                    if (!Enum.TryParse(pur, false, out MediaPurposeEnum purpose) || purpose == MediaPurposeEnum.Content) return null;
                    if (!root.TryGetProperty("exp", out JsonElement exp) || exp.ValueKind != JsonValueKind.Number) return null;
                    return new PrivateMediaGrant
                    {
                        PropertyId = pid,
                        MediaId = mid,
                        Purpose = purpose,
                        ScopeId = scp,
                        ViewerKey = uid,
                        ExpiresUtc = DateTimeOffset.FromUnixTimeMilliseconds(exp.GetInt64()).UtcDateTime
                    };
                }
            }
            catch (JsonException)
            {
                return null;
            }
        }

        #endregion

        #region Private-Methods

        private static string? Text(JsonElement root, string name)
        {
            return root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }

        #endregion
    }
}
