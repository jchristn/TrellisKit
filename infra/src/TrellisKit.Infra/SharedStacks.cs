namespace TrellisKit.Infra
{
    using System.Collections.Generic;
    using Amazon.CDK;
    using TrellisKit.Infra.Config;
    using TrellisKit.Infra.Stacks;

    /// <summary>
    /// The stacks every property of a shared deployment uses, in deployment order: network, storage, observability,
    /// registry, database, media pipeline, API, alarms, budget, and the deploy role.
    /// </summary>
    public class SharedStacks
    {
        /// <summary>
        /// Shared configuration the stacks were built from.
        /// </summary>
        public EnvironmentConfig Config { get; set; } = new EnvironmentConfig();

        /// <summary>
        /// Account-level GitHub OIDC provider stack, or null when GitHubSettings.ExistingOidcProviderArn names a
        /// provider created elsewhere.
        /// </summary>
        public GitHubOidcStack? GitHubOidc { get; set; }

        /// <summary>
        /// VPC and security groups.
        /// </summary>
        public NetworkStack? Network { get; set; }

        /// <summary>
        /// Media, updates, and access log buckets.
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
        /// Signing key group, MediaConvert role, job events.
        /// </summary>
        public MediaPipelineStack? MediaPipeline { get; set; }

        /// <summary>
        /// ECS service and ALB serving every property's API host.
        /// </summary>
        public ApiStack? Api { get; set; }

        /// <summary>
        /// CloudWatch alarms.
        /// </summary>
        public AlarmsStack? Alarms { get; set; }

        /// <summary>
        /// Monthly budget (us-east-1).
        /// </summary>
        public BudgetStack? Budget { get; set; }

        /// <summary>
        /// GitHub OIDC deploy role for the image, the service, and every property's website.
        /// </summary>
        public GitHubDeployStack? GitHubDeploy { get; set; }

        /// <summary>
        /// All shared stacks in deployment order.
        /// </summary>
        public List<Stack> All
        {
            get
            {
                List<Stack> stacks = new List<Stack>();
                Stack?[] ordered = new Stack?[] { Network, Storage, Observability, Registry, Data, MediaPipeline, Api, Alarms, Budget, GitHubOidc, GitHubDeploy };
                foreach (Stack? stack in ordered)
                {
                    if (stack != null) stacks.Add(stack);
                }

                return stacks;
            }
        }
    }
}
