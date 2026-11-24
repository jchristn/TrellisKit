namespace TrellisKit.Core.Integrations.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// Verifies identity tokens from one external provider (Apple, Google, or Facebook).
    /// Implementations are safe to call concurrently.
    /// </summary>
    public interface IIdentityTokenVerifier
    {
        /// <summary>
        /// Provider this verifier handles.
        /// </summary>
        ExternalProviderEnum Provider { get; }

        /// <summary>
        /// Whether the integration has the settings it needs. When false, calls throw <see cref="IntegrationNotConfiguredException"/>.
        /// </summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Verify a token and return its claims. Apple and Google tokens are JWTs validated against the provider's JWKS
        /// (signature, issuer, audience against the configured client ids, expiry with 5 minutes of clock skew). Facebook
        /// accepts a Limited Login JWT or an access token (validated with debug_token against the configured app).
        /// </summary>
        /// <param name="token">Token presented by the client.</param>
        /// <param name="tokenType">Token kind.</param>
        /// <param name="expectedNonce">Nonce the client generated, when the provider supports one (Apple; Facebook Limited Login). Apple nonces are compared as SHA-256 hex of this value.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Verified claims.</returns>
        /// <exception cref="IdentityTokenException">The token is invalid.</exception>
        /// <exception cref="IntegrationNotConfiguredException">The provider is not configured.</exception>
        Task<ExternalIdentityClaims> VerifyAsync(string token, ExternalTokenTypeEnum tokenType, string? expectedNonce, CancellationToken cancellationToken = default);
    }
}
