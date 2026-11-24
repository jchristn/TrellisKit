namespace TrellisKit.Infra.Tests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Infra.Config;

    /// <summary>
    /// One synthesized environment (dev or prod) of one property in the per-property topology. Synthesis uses the
    /// same builder as the CDK entry point.
    /// Thread safety: not thread safe; tests run serially.
    /// </summary>
    public class SynthesizedEnvironment : SynthesizedApp
    {
        /// <summary>
        /// Every stack of the environment.
        /// </summary>
        public AppStacks Stacks { get; }

        /// <summary>
        /// Synthesize an environment.
        /// </summary>
        /// <param name="name">dev or prod.</param>
        /// <param name="propertyDir">Property directory, or null for the default sample property.</param>
        /// <exception cref="ArgumentNullException">The name is empty.</exception>
        public SynthesizedEnvironment(string name, string? propertyDir) : base(BuildContext(name, propertyDir))
        {
            EnvironmentConfig config = EnvironmentConfigFactory.FromContext(App);
            Stacks = InfraAppBuilder.Build(App, config);
        }

        private static Dictionary<string, object> BuildContext(string name, string? propertyDir)
        {
            if (String.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            Dictionary<string, object> context = new Dictionary<string, object>
            {
                [EnvironmentConfigFactory.EnvironmentContextKey] = name
            };
            if (!String.IsNullOrWhiteSpace(propertyDir)) context[EnvironmentConfigFactory.PropertyDirContextKey] = propertyDir;
            return context;
        }
    }
}
