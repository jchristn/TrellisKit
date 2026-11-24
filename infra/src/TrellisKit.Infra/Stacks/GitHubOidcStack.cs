namespace TrellisKit.Infra.Stacks
{
    using System;
    using Amazon.CDK;
    using Amazon.CDK.AWS.IAM;
    using TrellisKit.Infra.Config;
    using Constructs;

    /// <summary>
    /// The GitHub Actions OIDC provider of one account. IAM allows one provider per issuer and account, so it lives in
    /// an account-level stack (<see cref="GitHubSettings.OidcProviderStackName"/>) rather than in each environment's
    /// GitHubDeploy stack: dev and prod deployed into one account (and several properties in one account) synthesize
    /// the same stack with the same template, and whichever deploys first creates the provider. The template carries
    /// nothing environment or property specific, and the provider is retained when the stack is deleted, because
    /// every deploy role in the account trusts it.
    /// </summary>
    public class GitHubOidcStack : Stack
    {
        /// <summary>
        /// The provider.
        /// </summary>
        public OidcProviderNative Provider { get; }

        /// <summary>
        /// ARN of the provider (the fixed ARN IAM derives from the issuer, so consumers need no cross-stack reference).
        /// </summary>
        public string OidcProviderArn { get; }

        /// <summary>
        /// Instantiate the stack.
        /// </summary>
        /// <param name="scope">Parent construct.</param>
        /// <param name="id">Construct id (the stack name).</param>
        /// <param name="config">Configuration of any environment deployed into the account (only its GitHub issuer, audience, and account are read).</param>
        /// <param name="props">Stack properties.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public GitHubOidcStack(Construct scope, string id, EnvironmentConfig config, IStackProps props) : base(scope, id, props)
        {
            ArgumentNullException.ThrowIfNull(config);

            Provider = new OidcProviderNative(this, "GitHubOidcProvider", new OidcProviderNativeProps
            {
                Url = config.GitHub.OidcProviderUrl,
                ClientIds = new string[] { config.GitHub.Audience }
            });
            Provider.ApplyRemovalPolicy(RemovalPolicy.RETAIN);
            OidcProviderArn = config.GitHub.OidcProviderArnIn(config.Account);

            new CfnOutput(this, "OidcProviderArn", new CfnOutputProps { Value = Provider.OidcProviderArn });
        }
    }
}
