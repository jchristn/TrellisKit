namespace TrellisKit.Infra.Tests
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using Amazon.CDK;
    using TrellisKit.Infra.Stacks;

    /// <summary>
    /// Shared readers for synthesized templates (distribution config, server container environment and secrets).
    /// </summary>
    public static class TemplateReader
    {
        /// <summary>
        /// The DistributionConfig of the only distribution in a stack.
        /// </summary>
        /// <param name="app">Synthesized app.</param>
        /// <param name="stack">Stack.</param>
        /// <returns>DistributionConfig element.</returns>
        public static JsonElement DistributionConfig(SynthesizedApp app, Stack? stack)
        {
            JsonElement distribution = app.Resources(stack, "AWS::CloudFront::Distribution").Single();
            return distribution.GetProperty("Properties").GetProperty("DistributionConfig");
        }

        /// <summary>
        /// The cache behavior with a path pattern.
        /// </summary>
        /// <param name="config">DistributionConfig element.</param>
        /// <param name="pattern">Path pattern.</param>
        /// <returns>Behavior element.</returns>
        public static JsonElement Behavior(JsonElement config, string pattern)
        {
            return config.GetProperty("CacheBehaviors").EnumerateArray().Single(b => b.GetProperty("PathPattern").GetString() == pattern);
        }

        /// <summary>
        /// Plain environment variables of the server container (token values as raw JSON).
        /// </summary>
        /// <param name="app">Synthesized app.</param>
        /// <param name="api">API stack.</param>
        /// <returns>Name to value.</returns>
        public static Dictionary<string, string> ServerEnvironment(SynthesizedApp app, Stack? api)
        {
            Dictionary<string, string> environment = new Dictionary<string, string>();
            foreach (JsonElement item in ServerContainer(app, api).GetProperty("Environment").EnumerateArray())
            {
                JsonElement value = item.GetProperty("Value");
                environment[item.GetProperty("Name").GetString() ?? string.Empty] = value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();
            }

            return environment;
        }

        /// <summary>
        /// Names of the ECS secrets of the server container.
        /// </summary>
        /// <param name="app">Synthesized app.</param>
        /// <param name="api">API stack.</param>
        /// <returns>Names.</returns>
        public static List<string> ServerSecrets(SynthesizedApp app, Stack? api)
        {
            return ServerContainer(app, api).GetProperty("Secrets").EnumerateArray().Select(e => e.GetProperty("Name").GetString() ?? string.Empty).ToList();
        }

        /// <summary>
        /// A container definition of the API task definition (the one with the server container) by name.
        /// </summary>
        /// <param name="app">Synthesized app.</param>
        /// <param name="api">API stack.</param>
        /// <param name="name">Container name.</param>
        /// <returns>Container definition.</returns>
        public static JsonElement ApiContainer(SynthesizedApp app, Stack? api, string name)
        {
            JsonElement taskDefinition = app.Resources(api, "AWS::ECS::TaskDefinition")
                .Single(t => t.GetProperty("Properties").GetProperty("ContainerDefinitions").EnumerateArray().Any(c => c.GetProperty("Name").GetString() == ApiStack.ServerContainerName));
            return taskDefinition.GetProperty("Properties").GetProperty("ContainerDefinitions").EnumerateArray()
                .Single(c => c.GetProperty("Name").GetString() == name);
        }

        private static JsonElement ServerContainer(SynthesizedApp app, Stack? api)
        {
            return ApiContainer(app, api, ApiStack.ServerContainerName);
        }
    }
}
