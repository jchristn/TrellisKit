namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using TrellisKit.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// Local mode media grant (contract section 4.7): the tk_media cookie is an AES-256-GCM protected
    /// { typ: "media", uid, exp } (same key as access tokens), Path /api/v1/media/files, no Domain, Secure except over
    /// plain http to a loopback host. The typ field keeps an access token from being accepted as a media cookie.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class LocalMediaCookieSigner : IMediaDeliverySigner
    {
        #region Public-Members

        /// <inheritdoc />
        public MediaDeliveryModeEnum Mode
        {
            get { return MediaDeliveryModeEnum.Local; }
        }

        #endregion

        #region Private-Members

        private const string _Type = "media";
        private readonly ITokenProtector _Protector;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the signer.
        /// </summary>
        /// <param name="protector">Token protector (the access token key).</param>
        /// <exception cref="ArgumentNullException">The protector is null.</exception>
        public LocalMediaCookieSigner(ITokenProtector protector)
        {
            _Protector = protector ?? throw new ArgumentNullException(nameof(protector));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public List<MediaCookie> CreateCookies(string userId, DateTime expiresUtc, bool insecureLoopback)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            Dictionary<string, object> payload = new Dictionary<string, object>
            {
                { "typ", _Type },
                { "uid", userId },
                { "exp", new DateTimeOffset(DateTime.SpecifyKind(expiresUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds() }
            };
            string value = _Protector.Protect(JsonSerializer.SerializeToUtf8Bytes(payload));
            return new List<MediaCookie>
            {
                new MediaCookie { Name = Constants.MediaCookieName, Value = value, Domain = null, Path = Constants.MediaCookiePath, ExpiresUtc = expiresUtc, Secure = !insecureLoopback }
            };
        }

        /// <summary>
        /// Validate a tk_media cookie value.
        /// </summary>
        /// <param name="value">Cookie value.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <returns>The granted user identifier, or null when the cookie is invalid, tampered with, of another type, or expired.</returns>
        public string? Validate(string? value, DateTime nowUtc)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            byte[]? plaintext;
            try
            {
                plaintext = _Protector.Unprotect(value);
            }
            catch (InvalidOperationException)
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
                    if (!root.TryGetProperty("typ", out JsonElement typ) || typ.GetString() != _Type) return null;
                    if (!root.TryGetProperty("uid", out JsonElement uid) || uid.ValueKind != JsonValueKind.String) return null;
                    if (!root.TryGetProperty("exp", out JsonElement exp) || exp.ValueKind != JsonValueKind.Number) return null;
                    if (DateTimeOffset.FromUnixTimeMilliseconds(exp.GetInt64()).UtcDateTime <= nowUtc) return null;
                    return uid.GetString();
                }
            }
            catch (JsonException)
            {
                return null;
            }
        }

        #endregion
    }
}
