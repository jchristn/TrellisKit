namespace Test.Shared.Fakes
{
    using System;
    using System.Collections.Concurrent;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.Integrations.Models;

    /// <summary>
    /// Identity verifier that accepts only tokens a test has scripted.
    /// </summary>
    public sealed class FakeIdentityTokenVerifier : IIdentityTokenVerifier
    {
        private readonly ConcurrentDictionary<string, ExternalIdentityClaims> _Tokens = new ConcurrentDictionary<string, ExternalIdentityClaims>(StringComparer.Ordinal);

        /// <summary>
        /// Instantiate the verifier.
        /// </summary>
        /// <param name="provider">Provider.</param>
        public FakeIdentityTokenVerifier(ExternalProviderEnum provider)
        {
            Provider = provider;
        }

        /// <inheritdoc />
        public ExternalProviderEnum Provider { get; }

        /// <summary>
        /// Whether the provider reports configured. Default true.
        /// </summary>
        public bool Configured { get; set; } = true;

        /// <inheritdoc />
        public bool IsConfigured
        {
            get { return Configured; }
        }

        /// <summary>
        /// Nonce passed with the last verification, or null.
        /// </summary>
        public string? LastNonce { get; private set; } = null;

        /// <summary>
        /// Token type passed with the last verification.
        /// </summary>
        public ExternalTokenTypeEnum LastTokenType { get; private set; } = ExternalTokenTypeEnum.Jwt;

        /// <summary>
        /// Script a token that verifies to the given claims.
        /// </summary>
        /// <param name="token">Token text.</param>
        /// <param name="subject">Subject.</param>
        /// <param name="email">Email, or null.</param>
        /// <param name="emailVerified">Whether the email is verified.</param>
        /// <param name="name">Display name, or null.</param>
        /// <returns>The token.</returns>
        public string Script(string token, string subject, string? email, bool emailVerified = true, string? name = null)
        {
            _Tokens[token] = new ExternalIdentityClaims { Provider = Provider, Subject = subject, Email = email, EmailVerified = emailVerified, DisplayName = name };
            return token;
        }

        /// <inheritdoc />
        public Task<ExternalIdentityClaims> VerifyAsync(string token, ExternalTokenTypeEnum tokenType, string? expectedNonce, CancellationToken cancellationToken = default)
        {
            if (!Configured) throw new IntegrationNotConfiguredException(Provider + " is not configured.");
            LastNonce = expectedNonce;
            LastTokenType = tokenType;
            if (token != null && _Tokens.TryGetValue(token, out ExternalIdentityClaims? claims)) return Task.FromResult(claims);
            throw new IdentityTokenException("Unknown test token.");
        }
    }
}
