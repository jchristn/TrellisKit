namespace TrellisKit.Core.Integrations.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;


    /// <summary>
    /// Encrypts and authenticates opaque tokens (AES-256-GCM with a random nonce per token).
    /// Implementations are safe to call concurrently.
    /// </summary>
    public interface ITokenProtector
    {
        /// <summary>
        /// Encrypt a payload into a URL-safe opaque token.
        /// </summary>
        /// <param name="payload">Plaintext bytes.</param>
        /// <returns>Token.</returns>
        /// <exception cref="ArgumentNullException">The payload is null.</exception>
        string Protect(byte[] payload);

        /// <summary>
        /// Decrypt and authenticate a token.
        /// </summary>
        /// <param name="token">Token.</param>
        /// <returns>Plaintext bytes, or null when the token is malformed, tampered with, or encrypted with another key.</returns>
        byte[]? Unprotect(string token);
    }
}
