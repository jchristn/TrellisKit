namespace TrellisKit.Infra.Stacks
{
    using System;
    using Amazon.CDK;
    using Amazon.CDK.AWS.ECR;
    using TrellisKit.Infra.Config;
    using Constructs;

    /// <summary>
    /// ECR repository of the server image. It is separate from <see cref="ApiStack"/> so an image can be pushed
    /// before the ECS service is first created (a service without an image never stabilizes).
    /// </summary>
    public class RegistryStack : Stack
    {
        /// <summary>
        /// Server image repository.
        /// </summary>
        public Repository Repository { get; }

        /// <summary>
        /// Instantiate the stack.
        /// </summary>
        /// <param name="scope">Parent construct.</param>
        /// <param name="id">Construct id.</param>
        /// <param name="config">Environment configuration.</param>
        /// <param name="props">Stack properties.</param>
        /// <exception cref="ArgumentNullException">The configuration is null.</exception>
        public RegistryStack(Construct scope, string id, EnvironmentConfig config, IStackProps props) : base(scope, id, props)
        {
            ArgumentNullException.ThrowIfNull(config);

            Repository = new Repository(this, "ServerRepository", new RepositoryProps
            {
                RepositoryName = config.ResourceName("server"),
                ImageScanOnPush = true,
                // CI pushes an immutable commit SHA tag and moves the latest tag, so tags must stay mutable.
                ImageTagMutability = TagMutability.MUTABLE,
                Encryption = RepositoryEncryption.AES_256,
                RemovalPolicy = config.StatefulRemovalPolicy,
                EmptyOnDelete = false,
                LifecycleRules = new ILifecycleRule[]
                {
                    new LifecycleRule
                    {
                        Description = "Keep the most recent images",
                        MaxImageCount = config.Api.MaxImageCount
                    }
                }
            });

            new CfnOutput(this, "RepositoryUri", new CfnOutputProps { Value = Repository.RepositoryUri });
        }
    }
}
