namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text;
    using TrellisKit.Core;

    /// <summary>
    /// Signs Local mode upload URLs (contract section 4.8): sig is the lowercase hex HMAC-SHA256 of
    /// "PUT\n&lt;path&gt;\n&lt;exp&gt;" with a key derived from auth.tokenEncryptionKey by HKDF-SHA256 with info
    /// "tk-media-upload". The key is read on every call, so a rotated key applies live.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class LocalUploadSigner
    {
        #region Public-Members

        /// <summary>
        /// HKDF info string.
        /// </summary>
        public const string KeyInfo = "tk-media-upload";

        /// <summary>
        /// Route prefix of local uploads.
        /// </summary>
        public const string PathPrefix = Constants.ApiPrefix + "/media/local-uploads/";

        #endregion

        #region Private-Members

        private readonly Func<string> _KeyProvider;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the signer.
        /// </summary>
        /// <param name="keyProvider">Returns the base64-encoded token encryption key.</param>
        /// <exception cref="ArgumentNullException">The provider is null.</exception>
        public LocalUploadSigner(Func<string> keyProvider)
        {
            _KeyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Path of a single upload: /api/v1/media/local-uploads/{id}.
        /// </summary>
        /// <param name="mediaId">Media identifier.</param>
        /// <returns>Path.</returns>
        public static string SinglePath(string mediaId)
        {
            return PathPrefix + mediaId;
        }

        /// <summary>
        /// Path of a part upload: /api/v1/media/local-uploads/{id}/parts/{n}.
        /// </summary>
        /// <param name="mediaId">Media identifier.</param>
        /// <param name="partNumber">Part number.</param>
        /// <returns>Path.</returns>
        public static string PartPath(string mediaId, int partNumber)
        {
            return PathPrefix + mediaId + "/parts/" + partNumber.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Signature of a path and expiry.
        /// </summary>
        /// <param name="path">Request path without query.</param>
        /// <param name="expiresUnixSeconds">Expiry in Unix seconds.</param>
        /// <returns>Lowercase hex signature.</returns>
        /// <exception cref="ArgumentNullException">The path is null or empty.</exception>
        /// <exception cref="InvalidOperationException">The token encryption key is not a base64 key.</exception>
        public string Sign(string path, long expiresUnixSeconds)
        {
            if (String.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            byte[] key = DeriveKey();
            byte[] message = Encoding.UTF8.GetBytes("PUT\n" + path + "\n" + expiresUnixSeconds.ToString(CultureInfo.InvariantCulture));
            return Convert.ToHexString(HMACSHA256.HashData(key, message)).ToLowerInvariant();
        }

        /// <summary>
        /// Absolute signed URL.
        /// </summary>
        /// <param name="origin">Origin, for example http://127.0.0.1:8000 (no trailing slash needed). Empty yields a root-relative URL.</param>
        /// <param name="path">Path.</param>
        /// <param name="expiresUtc">Expiry.</param>
        /// <returns>URL with exp and sig.</returns>
        public string BuildUrl(string origin, string path, DateTime expiresUtc)
        {
            long exp = new DateTimeOffset(DateTime.SpecifyKind(expiresUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
            return (origin ?? String.Empty).TrimEnd('/') + path + "?exp=" + exp.ToString(CultureInfo.InvariantCulture) + "&sig=" + Sign(path, exp);
        }

        /// <summary>
        /// Verify a signature in constant time and check the expiry.
        /// </summary>
        /// <param name="path">Request path without query.</param>
        /// <param name="exp">exp query value.</param>
        /// <param name="sig">sig query value.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <returns>True when valid and not expired.</returns>
        public bool Verify(string path, string? exp, string? sig, DateTime nowUtc)
        {
            if (String.IsNullOrEmpty(path) || String.IsNullOrEmpty(exp) || String.IsNullOrEmpty(sig) || sig.Length != 64) return false;
            if (!Int64.TryParse(exp, NumberStyles.None, CultureInfo.InvariantCulture, out long expires)) return false;
            if (expires < new DateTimeOffset(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc)).ToUnixTimeSeconds()) return false;
            string expected;
            try
            {
                expected = Sign(path, expires);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(sig.ToLowerInvariant()));
        }

        #endregion

        #region Private-Methods

        private byte[] DeriveKey()
        {
            byte[] ikm;
            try
            {
                ikm = Convert.FromBase64String(_KeyProvider() ?? String.Empty);
            }
            catch (FormatException e)
            {
                throw new InvalidOperationException("auth.tokenEncryptionKey is not a base64 key.", e);
            }
            if (ikm.Length < 16) throw new InvalidOperationException("auth.tokenEncryptionKey is not set.");
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 32, Array.Empty<byte>(), Encoding.UTF8.GetBytes(KeyInfo));
        }

        #endregion
    }
}
