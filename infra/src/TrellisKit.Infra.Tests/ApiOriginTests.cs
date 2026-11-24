namespace TrellisKit.Infra.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using TrellisKit.Infra.Config;
    using Xunit;

    /// <summary>
    /// Public API hosts remain independent of dedicated CloudFront API origin hosts.
    /// </summary>
    public class ApiOriginTests
    {
        /// <summary>
        /// Shared public hosts use a dedicated origin while separate API hosts preserve their existing DNS.
        /// </summary>
        /// <param name="name">Deployment environment.</param>
        /// <param name="shared">Whether the public API shares the website host.</param>
        [Theory]
        [InlineData("dev", true)]
        [InlineData("prod", true)]
        [InlineData("dev", false)]
        [InlineData("prod", false)]
        public void PublicAndOriginHostsProduceDistinctDnsAndCorrectCertificates(string name, bool shared)
        {
            string directory = CreateProperty(shared);
            try
            {
                SynthesizedEnvironment env = new SynthesizedEnvironment(name, directory);
                DomainSettings domains = env.Stacks.Config.Domains;
                string zone = (name == "dev" ? "dev." : String.Empty) + "example.test";
                string origin = (shared ? "api-origin." : "api.") + zone;
                Assert.Equal(shared ? zone : origin, domains.ApiHost);
                Assert.Equal("https://" + domains.ApiHost, domains.ApiUrl);
                Assert.Equal(origin, domains.ApiOriginHost);
                Assert.Equal(shared ? "example.test" : "api.example.test", env.Stacks.Config.Property.ApiDomain);
                Assert.Contains("https://" + zone, env.Stacks.Config.WebOrigins);

                JsonElement distribution = env.Resources(env.Stacks.Web, "AWS::CloudFront::Distribution").Single()
                    .GetProperty("Properties").GetProperty("DistributionConfig");
                JsonElement api = distribution.GetProperty("CacheBehaviors").EnumerateArray()
                    .Single(item => item.GetProperty("PathPattern").GetString() == "/api/*");
                string originId = api.GetProperty("TargetOriginId").GetString()!;
                JsonElement selectedOrigin = distribution.GetProperty("Origins").EnumerateArray()
                    .Single(item => item.GetProperty("Id").GetString() == originId);
                Assert.Equal(origin, selectedOrigin.GetProperty("DomainName").GetString());
                Assert.DoesNotContain(origin, distribution.GetProperty("Aliases").EnumerateArray().Select(item => item.GetString()));

                List<JsonElement> apiRecords = env.Resources(env.Stacks.Api, "AWS::Route53::RecordSet");
                Assert.Single(apiRecords);
                Assert.Equal(origin + ".", apiRecords[0].GetProperty("Properties").GetProperty("Name").GetString());
                List<string> recordKeys = apiRecords.Concat(env.Resources(env.Stacks.Web, "AWS::Route53::RecordSet"))
                    .Select(item => item.GetProperty("Properties").GetProperty("Name").GetString() + ":" + item.GetProperty("Properties").GetProperty("Type").GetString()).ToList();
                Assert.Equal(recordKeys.Count, recordKeys.Distinct().Count());
                Assert.Equal(origin, env.Resources(env.Stacks.Dns, "AWS::CertificateManager::Certificate").Single()
                    .GetProperty("Properties").GetProperty("DomainName").GetString());
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        /// <summary>
        /// External DNS exposes the custom origin record and certificate without claiming the website apex twice.
        /// </summary>
        [Fact]
        public void ExternalDnsUsesConfiguredOrigin()
        {
            string directory = CreateProperty(true, "backend.example.test", true);
            try
            {
                SynthesizedEnvironment env = new SynthesizedEnvironment("dev", directory);
                Assert.Equal("dev.example.test", env.Stacks.Config.Domains.ApiHost);
                Assert.Equal("backend.dev.example.test", env.Stacks.Config.Domains.ApiOriginHost);
                Assert.Empty(env.Resources(env.Stacks.Api, "AWS::Route53::RecordSet"));
                JsonElement outputs = env.Json(env.Stacks.Api).GetProperty("Outputs");
                string records = String.Join("\n", outputs.EnumerateObject().Where(item => item.Name.StartsWith("DnsRecord", StringComparison.Ordinal)).Select(item => item.Value.ToString()));
                Assert.Contains("backend.dev.example.test", records);
                Assert.DoesNotContain("\"dev.example.test\"", records);
                Assert.Contains("backend.dev.example.test", env.Json(env.Stacks.Dns).ToString());
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        /// <summary>
        /// Origin overrides cannot recurse to the website, collide with another service, or remove a public API DNS host.
        /// </summary>
        /// <param name="shared">Whether the public API shares the website host.</param>
        /// <param name="origin">Invalid origin.</param>
        [Theory]
        [InlineData(true, "example.test")]
        [InlineData(true, "media.example.test")]
        [InlineData(true, "www.example.test")]
        [InlineData(true, "outside.test")]
        [InlineData(true, "dev.example.test")]
        [InlineData(true, "www.dev.example.test")]
        [InlineData(true, "media.dev.example.test")]
        [InlineData(false, "backend.example.test")]
        public void InvalidOriginOverridesAreRejected(bool shared, string origin)
        {
            string directory = CreateProperty(shared, origin);
            try
            {
                Assert.Throws<ArgumentException>(() => PropertyDefinition.Load(directory));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        /// <summary>
        /// A development origin must not claim a production media host.
        /// </summary>
        [Fact]
        public void DevelopmentOriginCannotClaimProductionMedia()
        {
            string directory = CreateProperty(true, "backend.example.test");
            try
            {
                string file = Path.Combine(directory, "property.json");
                JsonNode property = JsonNode.Parse(File.ReadAllText(file))!;
                property["domains"]!["media"] = "backend.dev.example.test";
                File.WriteAllText(file, property.ToJsonString());
                Assert.Throws<ArgumentException>(() => PropertyDefinition.Load(directory));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        /// <summary>
        /// Each property owns its origin hosts across both environments, without counting its own shared public host twice.
        /// </summary>
        /// <param name="secondHost">Another property's conflicting website host.</param>
        [Theory]
        [InlineData("backend.example.test")]
        [InlineData("backend.dev.example.test")]
        public void OriginCannotBelongToAnotherProperty(string secondHost)
        {
            string first = CreateProperty(true, "backend.example.test");
            string second = CreateProperty(true);
            try
            {
                string file = Path.Combine(second, "property.json");
                JsonNode property = JsonNode.Parse(File.ReadAllText(file))!;
                property["id"] = "app_12345678_Abcdefgh12345678";
                property["slug"] = "second";
                property["domains"]!["web"] = secondHost;
                property["domains"]!["api"] = secondHost;
                property["domains"]!["media"] = "media." + secondHost;
                File.WriteAllText(file, property.ToJsonString());
                string deploymentFile = Path.Combine(first, "deployment.json");
                File.WriteAllText(deploymentFile, new JsonObject
                {
                    ["id"] = "origin-tests", ["topology"] = "PerProperty", ["properties"] = new JsonArray(first, second)
                }.ToJsonString());
                DeploymentDefinition definition = DeploymentDefinition.Load(deploymentFile);
                PropertyDefinition owner = PropertyDefinition.Load(first);
                Assert.Single(EnvironmentConfigFactory.CreateDeployment("prod", null, definition, new List<PropertyDefinition> { owner }).Properties);
                ArgumentException error = Assert.Throws<ArgumentException>(() => EnvironmentConfigFactory.CreateDeployment("prod", null, definition,
                    new List<PropertyDefinition> { owner, PropertyDefinition.Load(second) }));
                Assert.Contains("belongs to two properties", error.Message);
            }
            finally
            {
                Directory.Delete(first, true);
                Directory.Delete(second, true);
            }
        }

        /// <summary>
        /// Platform hosts in either environment cannot claim an API origin owned by a property.
        /// </summary>
        /// <param name="origin">Production API origin.</param>
        /// <param name="platform">Production platform host.</param>
        [Theory]
        [InlineData("backend.dev.example.test", "backend.example.test")]
        [InlineData("backend.example.test", "backend.dev.example.test")]
        public void PlatformCannotClaimOriginAcrossEnvironments(string origin, string platform)
        {
            string directory = CreateProperty(true, origin);
            try
            {
                string deploymentFile = Path.Combine(directory, "deployment.json");
                File.WriteAllText(deploymentFile, new JsonObject
                {
                    ["id"] = "origin-tests", ["topology"] = "Shared", ["properties"] = new JsonArray(directory),
                    ["platform"] = new JsonObject { ["hosts"] = new JsonArray(platform) }
                }.ToJsonString());
                DeploymentDefinition definition = DeploymentDefinition.Load(deploymentFile);
                ArgumentException error = Assert.Throws<ArgumentException>(() => EnvironmentConfigFactory.CreateDeployment("prod", null, definition,
                    new List<PropertyDefinition> { PropertyDefinition.Load(directory) }));
                Assert.Contains("already a host", error.Message);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static string CreateProperty(bool shared, string? origin = null, bool external = false)
        {
            string directory = Path.Combine(Path.GetTempPath(), "trelliskit-origin-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(directory, "deploy"));
            JsonNode property = JsonNode.Parse(File.ReadAllText(Path.Combine(PropertyDefinition.ResolveDirectory(null), "property.json")))!;
            property["domains"]!["api"] = shared ? "example.test" : "api.example.test";
            File.WriteAllText(Path.Combine(directory, "property.json"), property.ToJsonString());
            JsonObject deployment = new JsonObject();
            if (origin != null) deployment["apiOriginDomain"] = origin;
            if (external) deployment["dns"] = new JsonObject { ["mode"] = "External" };
            File.WriteAllText(Path.Combine(directory, "deploy", "infra.json"), deployment.ToJsonString());
            return directory;
        }
    }
}
