namespace TrellisKit.Core.Integrations.Implementations
{
    using System;

    /// <summary>
    /// Fields of a Google service account key used to sign OAuth assertions. Holds a private key; never log it.
    /// Immutable and thread safe.
    /// </summary>
    internal sealed class ServiceAccount
    {
        #region Internal-Members

        /// <summary>
        /// client_email.
        /// </summary>
        internal string Email { get; }

        /// <summary>
        /// private_key (PKCS#8 PEM).
        /// </summary>
        internal string PrivateKeyPem { get; }

        /// <summary>
        /// private_key_id, or null.
        /// </summary>
        internal string? KeyId { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the account.
        /// </summary>
        /// <param name="email">client_email.</param>
        /// <param name="privateKeyPem">private_key.</param>
        /// <param name="keyId">private_key_id, or null.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        internal ServiceAccount(string email, string privateKeyPem, string? keyId)
        {
            Email = email ?? throw new ArgumentNullException(nameof(email));
            PrivateKeyPem = privateKeyPem ?? throw new ArgumentNullException(nameof(privateKeyPem));
            KeyId = keyId;
        }

        #endregion
    }
}
