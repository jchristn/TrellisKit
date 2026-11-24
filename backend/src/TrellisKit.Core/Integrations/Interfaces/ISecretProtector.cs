namespace TrellisKit.Core.Integrations.Interfaces
{
    using System;
    using System.Security.Cryptography;

    /// <summary>
    /// Encrypts secrets stored at rest (model endpoint credentials and other per-property integration secrets) with the
    /// engine secret key, binding each ciphertext to its row through associated data (docs/AI_CONTRACT.md section 2.2).
    /// </summary>
    public interface ISecretProtector
    {
        /// <summary>
        /// Whether a current key is configured.
        /// </summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Identifier of the current key, or empty when none is configured.
        /// </summary>
        string CurrentKeyId { get; }

        /// <summary>
        /// Encrypt a secret.
        /// </summary>
        /// <param name="plaintext">Secret value.</param>
        /// <param name="associatedData">Associated data that must match on decryption.</param>
        /// <returns>Stored form: key id and ciphertext.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="InvalidOperationException">No key is configured.</exception>
        string Protect(string plaintext, string associatedData);

        /// <summary>
        /// Decrypt a stored secret.
        /// </summary>
        /// <param name="stored">Stored form from <see cref="Protect"/>.</param>
        /// <param name="associatedData">Associated data used on encryption.</param>
        /// <returns>Secret value.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="CryptographicException">The value is malformed, was encrypted with an unknown key, or the
        /// associated data does not match.</exception>
        string Unprotect(string stored, string associatedData);

        /// <summary>
        /// Whether a stored value was encrypted with a key other than the current one.
        /// </summary>
        /// <param name="stored">Stored form.</param>
        /// <returns>True when it should be re-encrypted.</returns>
        bool NeedsRotation(string? stored);
    }
}
