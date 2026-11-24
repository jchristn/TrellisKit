namespace TrellisKit.Infra.Config
{
    using System;

    /// <summary>
    /// GitHub Actions OIDC deploy role settings.
    /// </summary>
    public class GitHubSettings
    {
        private string _Owner = "example-owner";
        private string _Repository = "trelliskit";
        private string _DeploymentEnvironment = "dev";
        private string _OidcProviderUrl = "https://token.actions.githubusercontent.com";
        private string _Audience = "sts.amazonaws.com";
        private string _ExistingOidcProviderArn = String.Empty;
        private int _MaxSessionMinutes = 60;
        private string _OidcProviderStackName = "TrellisKit-GitHubOidc";

        /// <summary>
        /// Repository owner. Default example-owner (placeholder; set github.owner in the property's deploy/infra.json).
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string Owner
        {
            get { return _Owner; }
            set { _Owner = Guard.NotEmpty(value, nameof(Owner)); }
        }

        /// <summary>
        /// Repository name. Default trelliskit (placeholder; set github.repository in the property's deploy/infra.json).
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string Repository
        {
            get { return _Repository; }
            set { _Repository = Guard.NotEmpty(value, nameof(Repository)); }
        }

        /// <summary>
        /// GitHub deployment environment whose jobs may assume the role. Protect it with required reviewers in
        /// production. Default dev.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string DeploymentEnvironment
        {
            get { return _DeploymentEnvironment; }
            set { _DeploymentEnvironment = Guard.NotEmpty(value, nameof(DeploymentEnvironment)); }
        }

        /// <summary>
        /// GitHub OIDC issuer. Default https://token.actions.githubusercontent.com.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string OidcProviderUrl
        {
            get { return _OidcProviderUrl; }
            set { _OidcProviderUrl = Guard.NotEmpty(value, nameof(OidcProviderUrl)); }
        }

        /// <summary>
        /// Token audience. Default sts.amazonaws.com.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string Audience
        {
            get { return _Audience; }
            set { _Audience = Guard.NotEmpty(value, nameof(Audience)); }
        }

        /// <summary>
        /// ARN of an OIDC provider that already exists in the account (an account holds one per issuer) and was created
        /// outside these stacks. Default empty, which creates the provider in the account-level stack
        /// <see cref="OidcProviderStackName"/>.
        /// </summary>
        public string ExistingOidcProviderArn
        {
            get { return _ExistingOidcProviderArn; }
            set { _ExistingOidcProviderArn = value ?? String.Empty; }
        }

        /// <summary>
        /// Name of the account-level stack that owns the GitHub OIDC provider when <see cref="ExistingOidcProviderArn"/>
        /// is empty. An account holds one provider per issuer, so the stack is shared by every environment and property
        /// deployed into the account and region (dev and prod in one account deploy the same stack, which is identical
        /// in both). Default TrellisKit-GitHubOidc.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string OidcProviderStackName
        {
            get { return _OidcProviderStackName; }
            set { _OidcProviderStackName = Guard.NotEmpty(value, nameof(OidcProviderStackName)); }
        }

        /// <summary>
        /// Maximum role session length. Default 60. Minimum 60, maximum 720.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int MaxSessionMinutes
        {
            get { return _MaxSessionMinutes; }
            set { _MaxSessionMinutes = Guard.Range(value, 60, 720, nameof(MaxSessionMinutes)); }
        }

        /// <summary>
        /// The token subject the role trusts, for example repo:example-owner/trelliskit:environment:prod.
        /// </summary>
        public string Subject
        {
            get { return "repo:" + _Owner + "/" + _Repository + ":environment:" + _DeploymentEnvironment; }
        }

        /// <summary>
        /// The ARN the provider of <see cref="OidcProviderUrl"/> has in an account (IAM derives it from the issuer host).
        /// </summary>
        /// <param name="account">Twelve-digit account id.</param>
        /// <returns>Provider ARN.</returns>
        /// <exception cref="ArgumentNullException">The account is empty.</exception>
        public string OidcProviderArnIn(string account)
        {
            return "arn:aws:iam::" + Guard.NotEmpty(account, nameof(account)) + ":oidc-provider/" + OidcProviderHost;
        }

        /// <summary>
        /// The OIDC issuer host without scheme, used as the condition key prefix.
        /// </summary>
        public string OidcProviderHost
        {
            get { return _OidcProviderUrl.Replace("https://", String.Empty); }
        }
    }
}
