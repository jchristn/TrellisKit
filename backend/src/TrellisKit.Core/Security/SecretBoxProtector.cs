namespace TrellisKit.Core.Security
{
    using System;
    using System.Linq;
    using System.Security.Cryptography;
    using TrellisKit.Core.Integrations.Interfaces;

    /// <summary>
    /// <see cref="ISecretProtector"/> over the engine's one secrets implementation, <see cref="SecretBox"/> (setting
    /// secrets.encryptionKey, TK_SECRETS_ENCRYPTION_KEY; previous keys in secrets.previousEncryptionKeys). Values that
    /// live in a single column (AI model endpoint credentials) are stored as the envelope
    /// "v1:&lt;keyId&gt;:&lt;base64 of nonce, ciphertext, and tag&gt;", where keyId is <see cref="SecretBox.KeyId"/>; the
    /// cryptography, key ids, and rotation rules are SecretBox's, so the same key decrypts every stored secret and the
    /// secrets rotation worker re-encrypts these values too.
    /// Thread safety: stateless apart from the box supplier; safe to call concurrently.
    /// </summary>
    public class SecretBoxProtector : ISecretProtector
    {
        #region Public-Members

        /// <summary>
        /// Whether a valid current key is configured (secrets.encryptionKey is base64 of 32 bytes).
        /// </summary>
        public bool IsConfigured
        {
            get { return _IsConfigured(); }
        }

        /// <summary>
        /// Identifier of the current key, or empty when none is configured.
        /// </summary>
        public string CurrentKeyId
        {
            get { return _IsConfigured() ? _Box().KeyId : String.Empty; }
        }

        #endregion

        #region Private-Members

        private const string _Version = "v1";
        private const int _NonceBytes = 12;
        private readonly Func<SecretBox> _Box;
        private readonly Func<bool> _IsConfigured;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the protector.
        /// </summary>
        /// <param name="box">Supplies the secret box of the running settings (read on each call, so rotations apply live).</param>
        /// <param name="isConfigured">Whether a valid current key is configured.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public SecretBoxProtector(Func<SecretBox> box, Func<bool> isConfigured)
        {
            _Box = box ?? throw new ArgumentNullException(nameof(box));
            _IsConfigured = isConfigured ?? throw new ArgumentNullException(nameof(isConfigured));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Associated data of an AI model endpoint secret, binding it to its property, endpoint, and field.
        /// </summary>
        /// <param name="propertyId">Property identifier.</param>
        /// <param name="endpointId">Endpoint identifier.</param>
        /// <param name="field">Field: authValue or extraHeader:&lt;lowercase name&gt;.</param>
        /// <returns>Associated data.</returns>
        public static string EndpointAssociatedData(string propertyId, string endpointId, string field)
        {
            return "tk-ai-secret:v1|" + propertyId + "|" + endpointId + "|" + field;
        }

        /// <summary>
        /// Pack a SecretBox ciphertext into the single-column envelope.
        /// </summary>
        /// <param name="ciphertext">Ciphertext.</param>
        /// <returns>Envelope.</returns>
        /// <exception cref="ArgumentNullException">The ciphertext is null.</exception>
        public static string Pack(SecretCiphertext ciphertext)
        {
            ArgumentNullException.ThrowIfNull(ciphertext);
            byte[] payload = Convert.FromBase64String(ciphertext.Nonce).Concat(Convert.FromBase64String(ciphertext.Ciphertext)).ToArray();
            return _Version + ":" + ciphertext.KeyId + ":" + Convert.ToBase64String(payload);
        }

        /// <summary>
        /// Unpack a single-column envelope into SecretBox parts.
        /// </summary>
        /// <param name="stored">Envelope.</param>
        /// <returns>Ciphertext parts, or null when the envelope is malformed.</returns>
        public static SecretCiphertext? Unpack(string? stored)
        {
            if (String.IsNullOrEmpty(stored)) return null;
            string[] parts = stored.Split(':');
            if (parts.Length != 3 || parts[0] != _Version || parts[1].Length == 0) return null;
            byte[] payload;
            try
            {
                payload = Convert.FromBase64String(parts[2]);
            }
            catch (FormatException)
            {
                return null;
            }
            if (payload.Length <= _NonceBytes) return null;
            return new SecretCiphertext
            {
                KeyId = parts[1],
                Nonce = Convert.ToBase64String(payload, 0, _NonceBytes),
                Ciphertext = Convert.ToBase64String(payload, _NonceBytes, payload.Length - _NonceBytes)
            };
        }

        /// <summary>
        /// Key id of an envelope, or null when malformed.
        /// </summary>
        /// <param name="stored">Envelope.</param>
        /// <returns>Key id.</returns>
        public static string? KeyIdOf(string? stored)
        {
            return Unpack(stored)?.KeyId;
        }

        /// <summary>
        /// Encrypt a value with the current key.
        /// </summary>
        /// <param name="plaintext">Value.</param>
        /// <param name="associatedData">Associated data.</param>
        /// <returns>Envelope.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="InvalidOperationException">No valid key is configured.</exception>
        public string Protect(string plaintext, string associatedData)
        {
            ArgumentNullException.ThrowIfNull(plaintext);
            ArgumentNullException.ThrowIfNull(associatedData);
            if (!_IsConfigured()) throw new InvalidOperationException("secrets.encryptionKey (TK_SECRETS_ENCRYPTION_KEY) is not set.");
            return Pack(_Box().Encrypt(plaintext, associatedData));
        }

        /// <summary>
        /// Decrypt an envelope with the current or a previous key.
        /// </summary>
        /// <param name="stored">Envelope.</param>
        /// <param name="associatedData">Associated data used at encryption.</param>
        /// <returns>Value.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="CryptographicException">The envelope is malformed, its key is unknown, or the associated data does not match.</exception>
        public string Unprotect(string stored, string associatedData)
        {
            ArgumentNullException.ThrowIfNull(stored);
            ArgumentNullException.ThrowIfNull(associatedData);
            SecretCiphertext parts = Unpack(stored) ?? throw new CryptographicException("The stored secret is malformed.");
            return _Box().Decrypt(parts.Ciphertext, parts.Nonce, parts.KeyId, associatedData)
                ?? throw new CryptographicException("The stored secret does not decrypt with the configured keys (key " + parts.KeyId + ").");
        }

        /// <summary>
        /// Whether a stored envelope uses a key other than the current one.
        /// </summary>
        /// <param name="stored">Envelope, or null.</param>
        /// <returns>True when it should be re-encrypted.</returns>
        public bool NeedsRotation(string? stored)
        {
            string? keyId = KeyIdOf(stored);
            string current = CurrentKeyId;
            return keyId != null && current.Length > 0 && !String.Equals(keyId, current, StringComparison.Ordinal);
        }

        #endregion
    }
}
