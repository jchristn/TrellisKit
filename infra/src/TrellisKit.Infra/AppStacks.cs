namespace TrellisKit.Infra
{
    using System.Collections.Generic;
    using Amazon.CDK;
    using TrellisKit.Infra.Config;
    using TrellisKit.Infra.Stacks;

    /// <summary>
    /// Every stack of one environment, in deployment order. Returned by <see cref="InfraAppBuilder.Build"/>.
    /// </summary>
    public class AppStacks
    {
        /// <summary>
        /// Configuration the stacks were built from.
        /// </summary>
        public EnvironmentConfig Config { get; set; } = new EnvironmentConfig();

        /// <summary>
        /// Account-level GitHub OIDC provider stack (shared by every environment in the account), or null when
        /// GitHubSettings.ExistingOidcProviderArn names a provider created elsewhere.
        /// </summary>
        public GitHubOidcStack? GitHubOidc { get; set; }

        /// <summary>
        /// VPC and security groups.
        /// </summary>
        public NetworkStack? Network { get; set; }

        /// <summary>
        /// Hosted zones and the ALB certificate.
        /// </summary>
        public DnsStack? Dns { get; set; }

        /// <summary>
        /// CloudFront certificates (us-east-1).
        /// </summary>
        public EdgeCertificateStack? EdgeCertificates { get; set; }

        /// <summary>
        /// Buckets.
        /// </summary>
        public StorageStack? Storage { get; set; }

        /// <summary>
        /// Prometheus workspace, log groups, alarm topic.
        /// </summary>
        public ObservabilityStack? Observability { get; set; }

        /// <summary>
        /// ECR repository.
        /// </summary>
        public RegistryStack? Registry { get; set; }

        /// <summary>
        /// RDS PostgreSQL.
        /// </summary>
        public DataStack? Data { get; set; }

        /// <summary>
        /// Media CDN and MediaConvert wiring.
        /// </summary>
        public MediaStack? Media { get; set; }

        /// <summary>
        /// ECS service and ALB.
        /// </summary>
        public ApiStack? Api { get; set; }

        /// <summary>
        /// Website distribution.
        /// </summary>
        public WebStack? Web { get; set; }

        /// <summary>
        /// Vanity domain redirect.
        /// </summary>
        public RedirectStack? Redirect { get; set; }

        /// <summary>
        /// SES identity, or null when mail.provider is Mailgun or None.
        /// </summary>
        public EmailStack? Email { get; set; }

        /// <summary>
        /// CloudWatch alarms.
        /// </summary>
        public AlarmsStack? Alarms { get; set; }

        /// <summary>
        /// Monthly budget (us-east-1).
        /// </summary>
        public BudgetStack? Budget { get; set; }

        /// <summary>
        /// GitHub OIDC deploy role.
        /// </summary>
        public GitHubDeployStack? GitHubDeploy { get; set; }

        /// <summary>
        /// All stacks in deployment order, including the account-level <see cref="GitHubOidc"/> stack.
        /// </summary>
        public List<Stack> All
        {
            get
            {
                List<Stack> stacks = new List<Stack>();
                Stack?[] ordered = new Stack?[]
                {
                    Network, Dns, EdgeCertificates, Storage, Observability, Registry, Data, Media,
                    Api, Web, Redirect, Email, Alarms, Budget, GitHubOidc, GitHubDeploy
                };
                foreach (Stack? stack in ordered)
                {
                    if (stack != null) stacks.Add(stack);
                }

                return stacks;
            }
        }
    }
}
