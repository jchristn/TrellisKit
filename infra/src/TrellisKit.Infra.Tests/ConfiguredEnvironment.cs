namespace TrellisKit.Infra.Tests
{
    using System;
    using System.Collections.Generic;
    using TrellisKit.Infra.Config;

    /// <summary>
    /// One synthesized environment of the sample property whose configuration a test adjusts before the stacks are
    /// built.
    /// </summary>
    public class ConfiguredEnvironment : SynthesizedApp
    {
        /// <summary>
        /// The stacks.
        /// </summary>
        public AppStacks Stacks { get; }

        /// <summary>
        /// Synthesize an environment after adjusting its configuration.
        /// </summary>
        /// <param name="name">Environment name (dev or prod).</param>
        /// <param name="configure">Changes applied to the configuration.</param>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        public ConfiguredEnvironment(string name, Action<EnvironmentConfig> configure)
            : base(new Dictionary<string, object> { [EnvironmentConfigFactory.EnvironmentContextKey] = name })
        {
            if (String.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            ArgumentNullException.ThrowIfNull(configure);
            EnvironmentConfig config = EnvironmentConfigFactory.FromContext(App);
            configure(config);
            Stacks = InfraAppBuilder.Build(App, config);
        }
    }
}
