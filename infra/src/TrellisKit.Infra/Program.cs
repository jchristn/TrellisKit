namespace TrellisKit.Infra
{
    using Amazon.CDK;
    using TrellisKit.Infra.Config;

    /// <summary>
    /// CDK entry point. Select the environment with -c env=dev or -c env=prod, and either one property with
    /// -c propertyDir=&lt;folder&gt; (per-property topology, the default) or a deployment file with -c deployment=&lt;file&gt;.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Synthesize the app.
        /// </summary>
        /// <param name="args">Unused.</param>
        public static void Main(string[] args)
        {
            App app = new App();
            DeploymentConfig deployment = EnvironmentConfigFactory.DeploymentFromContext(app);
            InfraAppBuilder.BuildDeployment(app, deployment);
            app.Synth();
        }
    }
}
