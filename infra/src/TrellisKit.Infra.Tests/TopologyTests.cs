namespace TrellisKit.Infra.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using Amazon.CDK;
    using Amazon.CDK.Assertions;
    using TrellisKit.Infra.Config;
    using TrellisKit.Infra.Stacks;
    using Xunit;
    using Annotations = Amazon.CDK.Assertions.Annotations;

    /// <summary>
    /// Deployment topologies: the shared topology fixture (one shared API and database for the sample property and a
    /// second fixture property with a vanity domain), per-property isolation inside it, and deployment file rules.
    /// </summary>
    public class TopologyTests
    {
        /// <summary>
        /// One shared set (network, storage, observability, registry, data, media pipeline, API, alarms, budget, deploy
        /// role) plus each property's DNS, certificates, media CDN, website, and email; a redirect stack only for the
        /// property with vanity domains.
        /// </summary>
        /// <param name="name">Shared deployment.</param>
        [Theory]
        [InlineData("shared-dev")]
        [InlineData("shared-prod")]
        public void SharedTopologyBuildsSharedAndPerPropertyStacks(string name)
        {
            SynthesizedDeployment deployment = Environments.GetShared(name);
            DeploymentStacks stacks = deployment.Stacks;
            string environment = name.Substring("shared-".Length);

            Assert.Equal(DeploymentTopologyEnum.Shared, stacks.Topology);
            Assert.Empty(stacks.PerProperty);
            Assert.NotNull(stacks.Shared);
            Assert.Equal(11, stacks.Shared!.All.Count);
            foreach (Stack stack in stacks.Shared.All.Where(s => s != stacks.Shared.GitHubOidc))
            {
                Assert.StartsWith("fixture-shared-" + environment + "-", stack.StackName);
            }

            Assert.Equal(stacks.Shared.Config.GitHub.OidcProviderStackName, stacks.Shared.GitHubOidc!.StackName);

            Assert.Equal(2, stacks.Properties.Count);
            PropertyStacks plain = stacks.Properties.Single(p => p.Config.Domains.RedirectDomains.Count == 0);
            PropertyStacks vanity = stacks.Properties.Single(p => p.Config.Domains.RedirectDomains.Count > 0);
            Assert.Null(plain.Redirect);
            Assert.NotNull(vanity.Redirect);
            Assert.Equal(5, plain.All.Count);
            Assert.Equal(6, vanity.All.Count);
            foreach (PropertyStacks property in stacks.Properties)
            {
                foreach (Stack stack in property.All) Assert.StartsWith(property.Config.Property.Slug + "-" + environment + "-", stack.StackName);
                Assert.Equal("us-east-1", property.EdgeCertificates!.Region);
            }

            Assert.Equal(22, stacks.All.Count);
            Assert.Equal(stacks.All.Count, stacks.All.Select(s => s.StackName).Distinct().Count());
            Assert.Equal("us-east-1", stacks.Shared.Budget!.Region);
        }

        /// <summary>
        /// Every stack of the shared deployment synthesizes without errors or unsuppressed cdk-nag warnings, and without lookups.
        /// </summary>
        /// <param name="name">Shared deployment.</param>
        [Theory]
        [InlineData("shared-dev")]
        [InlineData("shared-prod")]
        public void SharedTopologyIsNagCleanAndNeedsNoLookups(string name)
        {
            SynthesizedDeployment deployment = Environments.GetShared(name);
            foreach (Stack stack in deployment.Stacks.All)
            {
                Annotations.FromStack(stack).HasNoError("*", Match.AnyValue());
                Annotations.FromStack(stack).HasNoWarning("*", Match.AnyValue());
            }

            Assert.Empty(deployment.NagFindings());

            deployment.App.Synth();
            using (JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(deployment.App.Outdir, "manifest.json"))))
            {
                Assert.False(manifest.RootElement.TryGetProperty("missing", out JsonElement missing), "Missing context: " + missing.ToString());
            }
        }

        /// <summary>
        /// The shared API load balancer carries every property's API certificate and alias record, runs the backend in
        /// MultiProperty mode, allows every property's website origins, and leaves per-property variables unset.
        /// </summary>
        /// <param name="name">Shared deployment.</param>
        [Theory]
        [InlineData("shared-dev")]
        [InlineData("shared-prod")]
        public void SharedApiServesEveryPropertyHost(string name)
        {
            SynthesizedDeployment deployment = Environments.GetShared(name);
            SharedStacks shared = deployment.Stacks.Shared!;
            string api = deployment.Json(shared.Api).GetRawText();

            List<JsonElement> records = deployment.Resources(shared.Api, "AWS::Route53::RecordSet");
            Assert.Equal(deployment.Stacks.Properties.Count + shared.Config.Platform.Hosts.Count, records.Count);
            foreach (PropertyStacks property in deployment.Stacks.Properties)
            {
                Assert.Single(records, r => r.GetProperty("Properties").GetProperty("Name").GetString() == property.Config.Domains.ApiHost + ".");
                Assert.Contains(property.Dns!.StackName + ":ExportsOutputRefApiCertificate", api);
                Assert.Contains(property.Dns.StackName + ":ExportsOutputRefPrimaryZone", api);
            }

            // Public listener: every API host and platform host. CloudFront origin listener: the API hosts only.
            deployment.Template(shared.Api).ResourceCountIs("AWS::ElasticLoadBalancingV2::ListenerCertificate",
                (deployment.Stacks.Properties.Count + shared.Config.Platform.Hosts.Count - 1) + (deployment.Stacks.Properties.Count - 1));

            Dictionary<string, string> environment = TemplateReader.ServerEnvironment(deployment, shared.Api);
            Assert.Equal("MultiProperty", environment[ServerEnvironmentKeys.DeploymentMode]);
            foreach (PropertyStacks property in deployment.Stacks.Properties)
            {
                foreach (string origin in property.Config.WebOrigins) Assert.Contains(origin, environment["TK_CORS_ORIGINS"].Split(','));
            }

            foreach (string key in ServerEnvironmentKeys.PerPropertyKeys) Assert.DoesNotContain(key, environment.Keys);
            Assert.DoesNotContain("TK_APPSTORE_BUNDLE_ID", environment.Keys);
            Assert.Contains(ServerEnvironmentKeys.UpdatesBucket, environment.Keys);
            Assert.Contains(ServerEnvironmentKeys.UpdatesSigningKey, TemplateReader.ServerSecrets(deployment, shared.Api));

            // Shared resources are named after the deployment, not after a property.
            Assert.Equal("fixture-shared", shared.Config.DeploymentId);
            Assert.Equal("fixture-shared/" + shared.Config.Name + "/server", shared.Config.ResourcePath("server"));
            deployment.Template(shared.Data).HasResourceProperties("AWS::RDS::DBInstance", new Dictionary<string, object>
            {
                ["DBInstanceIdentifier"] = "fixture-shared-" + shared.Config.Name
            });
        }

        /// <summary>
        /// Each property owns its website bucket, readable only by its own distribution, and the shared storage stack
        /// holds no website bucket.
        /// </summary>
        /// <param name="name">Shared deployment.</param>
        [Theory]
        [InlineData("shared-dev")]
        [InlineData("shared-prod")]
        public void WebsiteBucketsAreIsolatedPerProperty(string name)
        {
            SynthesizedDeployment deployment = Environments.GetShared(name);
            SharedStacks shared = deployment.Stacks.Shared!;
            Assert.Null(shared.Storage!.WebsiteBucket);
            foreach (JsonElement bucket in deployment.Resources(shared.Storage, "AWS::S3::Bucket"))
            {
                Assert.DoesNotContain("-website-", bucket.GetProperty("Properties").GetProperty("BucketName").GetString());
            }

            HashSet<string> names = new HashSet<string>();
            foreach (PropertyStacks property in deployment.Stacks.Properties)
            {
                WebStack web = property.Web!;
                Assert.NotNull(web.OwnedWebsiteBucket);
                JsonElement bucket = deployment.Resources(web, "AWS::S3::Bucket").Single();
                string bucketName = bucket.GetProperty("Properties").GetProperty("BucketName").GetString() ?? string.Empty;
                Assert.Equal(property.Config.BucketName("website"), bucketName);
                Assert.True(names.Add(bucketName));
                Assert.Contains("s3/sites/" + property.Config.Property.Slug + "/", bucket.GetProperty("Properties").GetProperty("LoggingConfiguration").GetRawText());

                // The read grant names this stack's distribution, never every distribution of the account.
                string policy = deployment.Resources(web, "AWS::S3::BucketPolicy").Single().GetRawText();
                Assert.Contains("cloudfront.amazonaws.com", policy);
                Assert.Contains("\"Ref\":\"WebDistribution", policy.Replace(" ", string.Empty));
                Assert.DoesNotContain("distribution/*", policy);
                deployment.Template(web).ResourceCountIs("AWS::CloudFront::OriginAccessControl", 1);

                foreach (PropertyStacks other in deployment.Stacks.Properties.Where(p => p != property))
                {
                    Assert.DoesNotContain(other.Config.BucketName("website"), deployment.Json(web).GetRawText());
                }
            }

            string deploy = string.Join("\n", deployment.Resources(shared.GitHubDeploy, "AWS::IAM::Policy").Select(p => p.GetRawText()));
            foreach (PropertyStacks property in deployment.Stacks.Properties)
            {
                Assert.Contains(property.Web!.StackName + ":ExportsOutputFnGetAttWebsiteBucket", deploy);
            }
        }

        /// <summary>
        /// Each property's certificates cover only its own hosts, and its distributions use its own certificates.
        /// </summary>
        /// <param name="name">Shared deployment.</param>
        [Theory]
        [InlineData("shared-dev")]
        [InlineData("shared-prod")]
        public void CertificatesAreIsolatedPerProperty(string name)
        {
            SynthesizedDeployment deployment = Environments.GetShared(name);
            foreach (PropertyStacks property in deployment.Stacks.Properties)
            {
                List<string> own = new List<string>(property.Config.Domains.RedirectDomains) { property.Config.Domains.PrimaryDomain };
                List<string> names = new List<string>();
                foreach (Stack stack in new Stack[] { property.EdgeCertificates!, property.Dns! })
                {
                    foreach (JsonElement certificate in deployment.Resources(stack, "AWS::CertificateManager::Certificate"))
                    {
                        JsonElement properties = certificate.GetProperty("Properties");
                        names.Add(properties.GetProperty("DomainName").GetString() ?? string.Empty);
                        if (properties.TryGetProperty("SubjectAlternativeNames", out JsonElement alternatives))
                        {
                            names.AddRange(alternatives.EnumerateArray().Select(a => a.GetString() ?? string.Empty));
                        }
                    }
                }

                Assert.Contains(property.Config.Domains.ApiHost, names);
                Assert.Contains(property.Config.Domains.MediaHost, names);
                foreach (string host in names)
                {
                    Assert.True(own.Any(d => host == d || host.EndsWith("." + d, StringComparison.Ordinal)), host + " is not a host of " + property.Config.Property.Id);
                }

                string ownEdge = property.EdgeCertificates!.StackName.Replace("-", string.Empty);
                foreach (Stack stack in new Stack?[] { property.Web, property.Media, property.Redirect }.Where(s => s != null).Cast<Stack>())
                {
                    string viewer = TemplateReader.DistributionConfig(deployment, stack).GetProperty("ViewerCertificate").GetRawText();
                    Assert.Contains(ownEdge, viewer);
                    foreach (PropertyStacks other in deployment.Stacks.Properties.Where(p => p != property))
                    {
                        Assert.DoesNotContain(other.EdgeCertificates!.StackName.Replace("-", string.Empty), viewer);
                    }
                }
            }
        }

        /// <summary>
        /// Each property's media distribution trusts the shared key group and answers CORS for its own website only.
        /// </summary>
        /// <param name="name">Shared deployment.</param>
        [Theory]
        [InlineData("shared-dev")]
        [InlineData("shared-prod")]
        public void PropertyMediaDistributionsUseTheSharedPipeline(string name)
        {
            SynthesizedDeployment deployment = Environments.GetShared(name);
            SharedStacks shared = deployment.Stacks.Shared!;
            deployment.Template(shared.MediaPipeline).ResourceCountIs("AWS::CloudFront::KeyGroup", 1);
            foreach (PropertyStacks property in deployment.Stacks.Properties)
            {
                deployment.Template(property.Media).ResourceCountIs("AWS::CloudFront::KeyGroup", 0);
                JsonElement config = TemplateReader.DistributionConfig(deployment, property.Media);
                Assert.Contains(shared.MediaPipeline!.StackName + ":ExportsOutputRefSigningKeyGroup", config.GetProperty("DefaultCacheBehavior").GetRawText());
                string cors = deployment.Resources(property.Media, "AWS::CloudFront::ResponseHeadersPolicy")
                    .Single(p => p.GetRawText().Contains("media-cors")).GetRawText();
                foreach (string origin in property.Config.WebOrigins) Assert.Contains(origin, cors);
                foreach (PropertyStacks other in deployment.Stacks.Properties.Where(p => p != property))
                {
                    Assert.DoesNotContain(other.Config.Domains.WebsiteUrl + "\"", cors);
                }
            }
        }

        /// <summary>
        /// A deployment file in the per-property topology yields one full stack set per listed property.
        /// </summary>
        [Fact]
        public void PerPropertyDeploymentFileBuildsOneFullSetPerProperty()
        {
            string directory = TemporaryDirectory();
            string fixtures = Path.Combine(PropertyDefinition.EngineRoot(), "infra", "fixtures", "shared");
            WriteDeployment(directory, "PerProperty", "per-property", new string[] { PropertyDefinition.ResolveDirectory(null), Path.Combine(fixtures, "second") });

            SynthesizedDeployment deployment = new SynthesizedDeployment("dev", Path.Combine(directory, "deployment.json"));
            Assert.Equal(DeploymentTopologyEnum.PerProperty, deployment.Stacks.Topology);
            Assert.Null(deployment.Stacks.Shared);
            Assert.Equal(2, deployment.Stacks.PerProperty.Count);
            // Both full sets plus the one account-level GitHub OIDC stack they share (same placeholder account).
            Assert.Equal(14 + 15 + 1, deployment.Stacks.All.Count);
            Assert.Single(deployment.Stacks.GitHubOidc);
            Assert.Same(deployment.Stacks.PerProperty[0].GitHubOidc, deployment.Stacks.PerProperty[1].GitHubOidc);
            foreach (AppStacks set in deployment.Stacks.PerProperty)
            {
                Assert.Equal(DeploymentTopologyEnum.PerProperty, set.Config.Topology);
                Assert.NotNull(set.Storage!.WebsiteBucket);
                Assert.Equal("SingleProperty", TemplateReader.ServerEnvironment(deployment, set.Api)[ServerEnvironmentKeys.DeploymentMode]);
            }
        }

        /// <summary>
        /// Deployment files with colliding ids or domains, a deployment id equal to a property id, no properties, or an
        /// unknown topology are rejected, as is combining a deployment file with propertyDir.
        /// </summary>
        [Fact]
        public void InvalidDeploymentsAreRejected()
        {
            string sample = PropertyDefinition.ResolveDirectory(null);

            string duplicate = TemporaryDirectory();
            string copy = Path.Combine(duplicate, "copy");
            Directory.CreateDirectory(copy);
            File.Copy(Path.Combine(sample, PropertyDefinition.PropertyFileName), Path.Combine(copy, PropertyDefinition.PropertyFileName));
            WriteDeployment(duplicate, "Shared", "dup", new string[] { sample, copy });
            Assert.Throws<ArgumentException>(() => new SynthesizedDeployment("dev", Path.Combine(duplicate, "deployment.json")));

            string sameId = TemporaryDirectory();
            WriteDeployment(sameId, "Shared", PropertyDefinition.Load(sample).Slug, new string[] { sample });
            Assert.Throws<ArgumentException>(() => new SynthesizedDeployment("dev", Path.Combine(sameId, "deployment.json")));

            string empty = TemporaryDirectory();
            WriteDeployment(empty, "Shared", "empty", new string[0]);
            Assert.Throws<ArgumentException>(() => DeploymentDefinition.Load(Path.Combine(empty, "deployment.json")));

            string unknown = TemporaryDirectory();
            WriteDeployment(unknown, "Pooled", "unknown", new string[] { sample });
            Assert.Throws<ArgumentException>(() => DeploymentDefinition.Load(Path.Combine(unknown, "deployment.json")));

            App both = new App(new AppProps
            {
                Context = new Dictionary<string, object>
                {
                    [EnvironmentConfigFactory.EnvironmentContextKey] = "dev",
                    [EnvironmentConfigFactory.DeploymentContextKey] = Environments.SharedDeploymentFile,
                    [EnvironmentConfigFactory.PropertyDirContextKey] = sample
                }
            });
            Assert.Throws<ArgumentException>(() => EnvironmentConfigFactory.DeploymentFromContext(both));
        }

        private static string TemporaryDirectory()
        {
            string directory = Path.Combine(Path.GetTempPath(), "trelliskit-infra-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void WriteDeployment(string directory, string topology, string id, string[] properties)
        {
            JsonArray list = new JsonArray();
            foreach (string property in properties) list.Add(property);
            JsonObject deployment = new JsonObject
            {
                ["id"] = id,
                ["topology"] = topology,
                ["properties"] = list
            };
            File.WriteAllText(Path.Combine(directory, "deployment.json"), deployment.ToJsonString());
        }
    }
}
