namespace TrellisKit.Infra.Tests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Infra.Config;

    /// <summary>
    /// One synthesized environment of a deployment file (any topology), built exactly as the CDK entry point does.
    /// Thread safety: not thread safe; tests run serially.
    /// </summary>
    public class SynthesizedDeployment : SynthesizedApp
    {
        /// <summary>
        /// The deployment configuration.
        /// </summary>
        public DeploymentConfig Config { get; }

        /// <summary>
        /// Every stack of the deployment.
        /// </summary>
        public DeploymentStacks Stacks { get; }

        /// <summary>
        /// Synthesize a deployment.
        /// </summary>
        /// <param name="name">dev or prod.</param>
        /// <param name="deploymentFile">Deployment file (absolute, or relative to the engine root).</param>
        /// <exception cref="ArgumentNullException">A required argument is empty.</exception>
        public SynthesizedDeployment(string name, string deploymentFile) : base(BuildContext(name, deploymentFile))
        {
            Config = EnvironmentConfigFactory.DeploymentFromContext(App);
            Stacks = InfraAppBuilder.BuildDeployment(App, Config);
        }

        private static Dictionary<string, object> BuildContext(string name, string deploymentFile)
        {
            if (String.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            if (String.IsNullOrWhiteSpace(deploymentFile)) throw new ArgumentNullException(nameof(deploymentFile));
            return new Dictionary<string, object>
            {
                [EnvironmentConfigFactory.EnvironmentContextKey] = name,
                [EnvironmentConfigFactory.DeploymentContextKey] = deploymentFile
            };
        }
    }
}
