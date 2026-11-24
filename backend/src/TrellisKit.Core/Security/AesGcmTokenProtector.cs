namespace TrellisKit.Core.Security
{
    using System;
    using System.Buffers.Text;
    using System.Security.Cryptography;
    using TrellisKit.Core.Integrations.Interfaces;

    /// <summary>
    /// AES-256-GCM token protection. Each token is base64url(version byte, 12-byte random nonce, ciphertext, 16-byte tag).
    /// The key comes from settings and is read on each call, so a rotated key applies live (and invalidates old tokens).
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class AesGcmTokenProtector : ITokenProtector
    {
        #region Private-Members

        private const byte _Version = 1;
        private const int _NonceBytes = 12;
        private const int _TagBytes = 16;
        private readonly Func<string> _KeyProvider;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the protector.
        /// </summary>
        /// <param name="keyProvider">Returns the base64-encoded 256-bit key.</param>
        /// <exception cref="ArgumentNullException">The provider is null.</exception>
        public AesGcmTokenProtector(Func<string> keyProvider)
        {
            _KeyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Generate a new random base64-encoded 256-bit key.
        /// </summary>
        /// <returns>Key.</returns>
        public static string GenerateKey()
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        }

        /// <inheritdoc />
        /// <exception cref="InvalidOperationException">The configured key is not a base64-encoded 256-bit key.</exception>
        public string Protect(byte[] payload)
        {
            ArgumentNullException.ThrowIfNull(payload);
            byte[] key = ReadKey();
            byte[] output = new byte[1 + _NonceBytes + payload.Length + _TagBytes];
            output[0] = _Version;
            Span<byte> nonce = output.AsSpan(1, _NonceBytes);
            RandomNumberGenerator.Fill(nonce);
            using (AesGcm aes = new AesGcm(key, _TagBytes))
            {
                aes.Encrypt(nonce, payload, output.AsSpan(1 + _NonceBytes, payload.Length), output.AsSpan(1 + _NonceBytes + payload.Length, _TagBytes), new byte[] { _Version });
            }
            return Base64Url.EncodeToString(output);
        }

        /// <inheritdoc />
        public byte[]? Unprotect(string token)
        {
            if (String.IsNullOrWhiteSpace(token) || token.Length > 8192) return null;
            byte[] data;
            byte[] key;
            try
            {
                data = Base64Url.DecodeFromChars(token);
                key = ReadKey();
            }
            catch (FormatException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }

            if (data.Length < 1 + _NonceBytes + _TagBytes || data[0] != _Version) return null;
            int cipherLength = data.Length - 1 - _NonceBytes - _TagBytes;
            byte[] plaintext = new byte[cipherLength];
            try
            {
                using (AesGcm aes = new AesGcm(key, _TagBytes))
                {
                    aes.Decrypt(data.AsSpan(1, _NonceBytes), data.AsSpan(1 + _NonceBytes, cipherLength), data.AsSpan(1 + _NonceBytes + cipherLength, _TagBytes), plaintext, new byte[] { _Version });
                }
                return plaintext;
            }
            catch (CryptographicException)
            {
                return null;
            }
        }

        #endregion

        #region Private-Methods

        private byte[] ReadKey()
        {
            string encoded = _KeyProvider() ?? String.Empty;
            byte[] key;
            try
            {
                key = Convert.FromBase64String(encoded);
            }
            catch (FormatException e)
            {
                throw new InvalidOperationException("auth.tokenEncryptionKey is not valid base64.", e);
            }
            if (key.Length != 32) throw new InvalidOperationException("auth.tokenEncryptionKey must decode to 32 bytes.");
            return key;
        }

        #endregion
    }
}
