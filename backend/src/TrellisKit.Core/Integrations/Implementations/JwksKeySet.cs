namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Security.Cryptography;

    /// <summary>
    /// An immutable snapshot of RSA signing keys fetched from a JWKS endpoint.
    /// Thread safe.
    /// </summary>
    internal sealed class JwksKeySet
    {
        #region Internal-Members

        /// <summary>
        /// Keys by kid.
        /// </summary>
        internal IReadOnlyDictionary<string, RSAParameters> Keys { get; }

        /// <summary>
        /// When the snapshot was fetched (UTC).
        /// </summary>
        internal DateTime FetchedUtc { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the snapshot.
        /// </summary>
        /// <param name="keys">Keys by kid.</param>
        /// <param name="fetchedUtc">Fetch time (UTC).</param>
        /// <exception cref="ArgumentNullException">The keys are null.</exception>
        internal JwksKeySet(IReadOnlyDictionary<string, RSAParameters> keys, DateTime fetchedUtc)
        {
            Keys = keys ?? throw new ArgumentNullException(nameof(keys));
            FetchedUtc = fetchedUtc;
        }

        #endregion
    }
}
