namespace Test.Shared.IntegrationTests
{
    using System;
    using TrellisKit.Core.Integrations.Implementations;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Apple verifier wired to a fake JWKS that serves one key.
    /// </summary>
    public sealed class AppleSignInHarness : TestHttp
    {
        #region Public-Members

        /// <summary>
        /// Live settings used by the verifier.
        /// </summary>
        public OAuthSettings Settings { get; } = new OAuthSettings { AppleBundleId = AppleSignInSuite.BundleId, AppleServicesId = AppleSignInSuite.ServicesId };

        /// <summary>
        /// Verifier under test.
        /// </summary>
        public AppleIdentityTokenVerifier Verifier { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the harness.
        /// </summary>
        /// <param name="key">Key served by the JWKS.</param>
        /// <exception cref="ArgumentNullException">The key is null.</exception>
        public AppleSignInHarness(TestRsaKey key)
        {
            ArgumentNullException.ThrowIfNull(key);
            Handler.MapJson("GET", AppleSignInSuite.JwksUrl, 200, TestTokens.Jwks(key));
            Verifier = new AppleIdentityTokenVerifier(Settings, Client, AppleSignInSuite.JwksUrl);
        }

        #endregion
    }
}
