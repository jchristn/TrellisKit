namespace TrellisKit.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// What <see cref="JwtValidator"/> requires of a token's registered claims.
    /// Not thread safe while being populated; treat as immutable once passed to the validator.
    /// </summary>
    internal sealed class JwtValidationOptions
    {
        #region Internal-Members

        /// <summary>
        /// Accepted iss values (exact, ordinal). Must not be empty.
        /// </summary>
        internal IReadOnlyList<string> ValidIssuers { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Accepted aud values (exact, ordinal). A token passes when any of its audiences is accepted. Must not be empty.
        /// </summary>
        internal IReadOnlyList<string> ValidAudiences { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Allowed clock skew for exp, nbf, and iat. Default 5 minutes.
        /// </summary>
        internal TimeSpan ClockSkew { get; set; } = TimeSpan.FromMinutes(5);

        #endregion
    }
}
