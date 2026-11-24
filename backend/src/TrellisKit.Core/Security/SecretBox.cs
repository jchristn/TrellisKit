namespace TrellisKit.Core.Security
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;

    /// <summary>
    /// Encrypts stored secrets with the engine secrets key (setting secrets.encryptionKey, TK_SECRETS_ENCRYPTION_KEY):
    /// AES-256-GCM with a random 12-byte nonce and associated data (for example "&lt;propertyId&gt;|&lt;key&gt;"), so a
    /// ciphertext copied to another property or key fails to decrypt (docs/MULTIPROPERTY_CONTRACT.md section 5.5).
    /// Previous keys still decrypt (rotation). Values are never logged.
    /// Thread safety: immutable; safe to share.
    /// </summary>
    public sealed class SecretBox
    {
        #region Public-Members

        /// <summary>
        /// Identifier of the current key: the first 8 hex characters of its SHA-256.
        /// </summary>
        public string KeyId { get; }

        /// <summary>
        /// Ids of the previous keys this box still decrypts with (values encrypted with them are re-encrypted by the
        /// rotation pass). Never null; empty when no previous key is configured.
        /// </summary>
        public IReadOnlyList<string> PreviousKeyIds
        {
            get { return _Keys.Keys.Where(k => !String.Equals(k, KeyId, StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal).ToList(); }
        }

        #endregion

        #region Private-Members

        private const int _NonceBytes = 12;
        private const int _TagBytes = 16;
        private readonly byte[] _Key;
        private readonly Dictionary<string, byte[]> _Keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with the current key and optional previous keys.
        /// </summary>
        /// <param name="key">Current key: base64 of 32 bytes.</param>
        /// <param name="previousKeys">Previous keys (comma-separated base64 values), or null.</param>
        /// <exception cref="ArgumentException">A key is not base64 of 32 bytes.</exception>
        public SecretBox(string key, string? previousKeys = null)
        {
            _Key = Parse(key, nameof(key));
            KeyId = IdOf(_Key);
            _Keys[KeyId] = _Key;
            foreach (string previous in (previousKeys ?? String.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                byte[] bytes = Parse(previous, nameof(previousKeys));
                _Keys.TryAdd(IdOf(bytes), bytes);
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Generate a new random key (base64 of 32 bytes).
        /// </summary>
        /// <returns>Key.</returns>
        public static string GenerateKey()
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        }

        /// <summary>
        /// Whether text is base64 of exactly 32 bytes.
        /// </summary>
        /// <param name="key">Text.</param>
        /// <returns>True when valid.</returns>
        public static bool IsValidKey(string? key)
        {
            if (String.IsNullOrWhiteSpace(key)) return false;
            try
            {
                return Convert.FromBase64String(key.Trim()).Length == 32;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        /// <summary>
        /// Encrypt a value.
        /// </summary>
        /// <param name="plaintext">Value.</param>
        /// <param name="associatedData">Associated data bound to the ciphertext.</param>
        /// <returns>Ciphertext (base64 of ciphertext plus tag), nonce (base64), and key id.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public SecretCiphertext Encrypt(string plaintext, string associatedData)
        {
            ArgumentNullException.ThrowIfNull(plaintext);
            ArgumentNullException.ThrowIfNull(associatedData);
            byte[] nonce = RandomNumberGenerator.GetBytes(_NonceBytes);
            byte[] input = Encoding.UTF8.GetBytes(plaintext);
            byte[] output = new byte[input.Length];
            byte[] tag = new byte[_TagBytes];
            using (AesGcm aes = new AesGcm(_Key, _TagBytes))
            {
                aes.Encrypt(nonce, input, output, tag, Encoding.UTF8.GetBytes(associatedData));
            }
            return new SecretCiphertext
            {
                Ciphertext = Convert.ToBase64String(output.Concat(tag).ToArray()),
                Nonce = Convert.ToBase64String(nonce),
                KeyId = KeyId
            };
        }

        /// <summary>
        /// Decrypt a value.
        /// </summary>
        /// <param name="ciphertext">Ciphertext (base64 of ciphertext plus tag).</param>
        /// <param name="nonce">Nonce (base64).</param>
        /// <param name="keyId">Key id recorded with the ciphertext.</param>
        /// <param name="associatedData">Associated data used at encryption.</param>
        /// <returns>Value, or null when the key is unknown or the ciphertext, nonce, or associated data do not match.</returns>
        public string? Decrypt(string? ciphertext, string? nonce, string? keyId, string associatedData)
        {
            if (String.IsNullOrEmpty(ciphertext) || String.IsNullOrEmpty(nonce) || String.IsNullOrEmpty(keyId) || associatedData == null) return null;
            if (!_Keys.TryGetValue(keyId, out byte[]? key)) return null;
            try
            {
                byte[] all = Convert.FromBase64String(ciphertext);
                byte[] iv = Convert.FromBase64String(nonce);
                if (all.Length < _TagBytes || iv.Length != _NonceBytes) return null;
                byte[] data = all.Take(all.Length - _TagBytes).ToArray();
                byte[] tag = all.Skip(all.Length - _TagBytes).ToArray();
                byte[] plain = new byte[data.Length];
                using (AesGcm aes = new AesGcm(key, _TagBytes))
                {
                    aes.Decrypt(iv, data, tag, plain, Encoding.UTF8.GetBytes(associatedData));
                }
                return Encoding.UTF8.GetString(plain);
            }
            catch (FormatException)
            {
                return null;
            }
            catch (CryptographicException)
            {
                return null;
            }
        }

        #endregion

        #region Private-Methods

        private static byte[] Parse(string? key, string name)
        {
            if (!IsValidKey(key)) throw new ArgumentException("The secrets key must be base64 of 32 bytes.", name);
            return Convert.FromBase64String(key!.Trim());
        }

        private static string IdOf(byte[] key)
        {
            return Convert.ToHexString(SHA256.HashData(key)).Substring(0, 8).ToLowerInvariant();
        }

        #endregion
    }
}
