namespace TrellisKit.Core.Integrations.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;


    /// <summary>
    /// Hashes and verifies member passwords.
    /// Implementations are safe to call concurrently.
    /// </summary>
    public interface IPasswordHasher
    {
        /// <summary>
        /// Hash a password. Output format: "pbkdf2-sha256$&lt;iterations&gt;$&lt;base64 salt&gt;$&lt;base64 hash&gt;".
        /// </summary>
        /// <param name="password">Plaintext password.</param>
        /// <returns>Encoded hash.</returns>
        /// <exception cref="ArgumentNullException">The password is null.</exception>
        string Hash(string password);

        /// <summary>
        /// Verify a password against an encoded hash in constant time.
        /// </summary>
        /// <param name="password">Plaintext password.</param>
        /// <param name="encodedHash">Encoded hash produced by <see cref="Hash"/>.</param>
        /// <param name="needsRehash">True when the hash uses weaker parameters than current settings and should be replaced.</param>
        /// <returns>True when the password matches.</returns>
        bool Verify(string password, string encodedHash, out bool needsRehash);
    }
}
