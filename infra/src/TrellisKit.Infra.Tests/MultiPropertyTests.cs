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

    /// <summary>
    /// Multi-property backend support: opaque property ids with cosmetic slugs in every AWS name, the PostgreSQL
    /// bootstrap (pgvector and a non-superuser application role), deployment and proxy environment variables, header
    /// forwarding through CloudFront and the load balancer, and platform hosts of the shared topology.
    /// </summary>
    public class MultiPropertyTests
    {
        private static readonly string _CachingDisabledPolicyId = "4135ea2d-6df8-44a3-9df3-4b5a84be39ad";
        private static readonly string _AllViewerExceptHostHeaderPolicyId = "b689b0a8-53d0-40ab-baf2-68738e2966ac";
        private static readonly string _SampleIdForTests = "app_abcdef12_Abcdefgh1234";

        /// <summary>
        /// The sample property has an opaque id (underscores and uppercase) and every stack and physical name uses its
        /// slug instead; the opaque id is kept as the PropertyId tag.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void NamesUseTheSlugAndNeverTheOpaqueId(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            PropertyDefinition property = env.Stacks.Config.Property;
            Assert.StartsWith("app_", property.Id);
            Assert.Matches("^[a-z0-9][a-z0-9-]*$", property.Slug);
            Assert.Equal(property.Slug + "-" + name, env.Stacks.Config.ResourcePrefix);
            Assert.Equal(property.Slug, env.Stacks.Config.DeploymentId);

            foreach (Stack stack in env.Stacks.All.Where(s => s != env.Stacks.GitHubOidc))
            {
                Assert.StartsWith(property.Slug + "-" + name + "-", stack.StackName);
                Assert.DoesNotContain(property.Id, stack.StackName);
            }

            foreach (JsonElement bucket in env.Resources(env.Stacks.Storage, "AWS::S3::Bucket"))
            {
                string bucketName = bucket.GetProperty("Properties").GetProperty("BucketName").GetString() ?? string.Empty;
                Assert.StartsWith(property.Slug + "-" + name + "-", bucketName);
            }

            env.Template(env.Stacks.Registry).HasResourceProperties("AWS::ECR::Repository", new Dictionary<string, object>
            {
                ["Tags"] = Match.ArrayWith(new object[]
                {
                    new Dictionary<string, object> { ["Key"] = "PropertyId", ["Value"] = property.Id }
                })
            });
        }

        /// <summary>
        /// A missing slug is derived from shortName (else name) like the backend's EffectiveSlug.
        /// </summary>
        [Fact]
        public void SlugIsDerivedLikeTheBackend()
        {
            Assert.Equal("example-community", PropertyDefinition.DeriveSlug("Example Community!", "ignored"));
            Assert.Equal("second-example", PropertyDefinition.DeriveSlug(null, "  Second -- Example  "));
            Assert.Equal("property", PropertyDefinition.DeriveSlug(String.Empty, "***"));
            Assert.Equal(63, PropertyDefinition.DeriveSlug(new string('a', 80), null).Length);

            string directory = WriteProperty(_SampleIdForTests, null, "Short Name 2", "Long Display Name");
            PropertyDefinition property = PropertyDefinition.Load(directory);
            Assert.Equal(_SampleIdForTests, property.Id);
            Assert.Equal("short-name-2", property.Slug);
        }

        /// <summary>
        /// Legacy readable ids, reserved slugs, malformed slugs, and slugs too long for AWS names are rejected.
        /// </summary>
        [Fact]
        public void InvalidIdsAndSlugsAreRejected()
        {
            ArgumentException legacy = Assert.Throws<ArgumentException>(() => PropertyDefinition.Load(WriteProperty("example", "example", null, "X")));
            Assert.Contains("generated property id", legacy.Message);
            Assert.Throws<ArgumentException>(() => PropertyDefinition.Load(WriteProperty(_SampleIdForTests, "platform", null, "X")));
            Assert.Throws<ArgumentException>(() => PropertyDefinition.Load(WriteProperty(_SampleIdForTests, "Bad_Slug", null, "X")));
            ArgumentException tooLong = Assert.Throws<ArgumentException>(() => PropertyDefinition.Load(WriteProperty(_SampleIdForTests, null, null, "A Display Name Longer Than Twenty")));
            Assert.Contains("slug", tooLong.Message);
        }

        /// <summary>
        /// The server connects as the application role from its own secret; the master credentials reach only the
        /// non-essential bootstrap container, which creates pgvector and the role before the server starts.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void ServerUsesTheApplicationRoleAndBootstrapRunsFirst(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            env.Template(env.Stacks.Data).HasResourceProperties("AWS::SecretsManager::Secret", new Dictionary<string, object>
            {
                ["Name"] = env.Stacks.Config.ResourcePath("database-app"),
                ["GenerateSecretString"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["GenerateStringKey"] = "password",
                    ["SecretStringTemplate"] = Match.StringLikeRegexp("trelliskit_app")
                })
            });

            List<JsonElement> containers = Containers(env, env.Stacks.Api);
            JsonElement server = containers.Single(c => c.GetProperty("Name").GetString() == ApiStack.ServerContainerName);
            JsonElement bootstrap = containers.Single(c => c.GetProperty("Name").GetString() == ApiStack.BootstrapContainerName);

            Assert.Contains("ApplicationSecret", SecretValueFrom(server, "TK_DB_USERNAME"));
            Assert.Contains("ApplicationSecret", SecretValueFrom(server, "TK_DB_PASSWORD"));
            Assert.DoesNotContain("ApplicationSecret", SecretValueFrom(bootstrap, "PGUSER"));
            Assert.Contains("ApplicationSecret", SecretValueFrom(bootstrap, "APP_DB_PASSWORD"));

            Assert.False(bootstrap.GetProperty("Essential").GetBoolean());
            Assert.Contains("postgres:17", bootstrap.GetProperty("Image").GetString());
            string dependsOn = server.GetProperty("DependsOn").GetRawText();
            Assert.Contains(ApiStack.BootstrapContainerName, dependsOn);
            Assert.Contains("SUCCESS", dependsOn);

            Dictionary<string, string> environment = new Dictionary<string, string>();
            foreach (JsonElement item in bootstrap.GetProperty("Environment").EnumerateArray())
            {
                JsonElement value = item.GetProperty("Value");
                environment[item.GetProperty("Name").GetString() ?? string.Empty] = value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();
            }

            Assert.Equal("require", environment["PGSSLMODE"]);
            Assert.Equal(env.Stacks.Config.Database.DatabaseName, environment["PGDATABASE"]);
            string sql = environment["BOOTSTRAP_SQL"];
            Assert.Contains("CREATE EXTENSION IF NOT EXISTS vector", sql);
            Assert.Contains("NOSUPERUSER NOCREATEDB NOCREATEROLE", sql);
            Assert.Contains("GRANT USAGE, CREATE ON SCHEMA public", sql);
            Assert.DoesNotContain("SUPERUSER;", sql);
            List<string> bootstrapEnvironment = bootstrap.GetProperty("Environment").EnumerateArray().Select(e => e.GetProperty("Name").GetString() ?? string.Empty).ToList();
            Assert.DoesNotContain("PGPASSWORD", bootstrapEnvironment);
            Assert.DoesNotContain("APP_DB_PASSWORD", bootstrapEnvironment);
        }

        /// <summary>
        /// The server gets its deployment name, forwarded-host trust, and the VPC as its only trusted proxy; per-property
        /// deployments set no platform values.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void ServerTrustsOnlyTheLoadBalancer(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            Dictionary<string, string> environment = TemplateReader.ServerEnvironment(env, env.Stacks.Api);
            Assert.Equal("SingleProperty", environment[ServerEnvironmentKeys.DeploymentMode]);
            Assert.Equal(env.Stacks.Config.Property.Slug, environment[ServerEnvironmentKeys.DeploymentName]);
            Assert.Equal("true", environment[ServerEnvironmentKeys.TrustForwardedHost]);
            Assert.Equal(env.Stacks.Config.Network.CidrBlock, environment[ServerEnvironmentKeys.TrustedProxies]);
            Assert.DoesNotContain(ServerEnvironmentKeys.PlatformHosts, environment.Keys);
            Assert.DoesNotContain(ServerEnvironmentKeys.PlatformBootstrapAdminEmail, environment.Keys);
            Assert.DoesNotContain("TK_DB_FILENAME", environment.Keys);
            Assert.DoesNotContain("TK_DB_FILENAME", ServerSecretKeys.AllKeys);
            Assert.DoesNotContain(ServerSecretKeys.PlatformBootstrapAdminPassword, TemplateReader.ServerSecrets(env, env.Stacks.Api));
            Assert.Null(env.Stacks.Api!.PlatformSecret);

            env.Template(env.Stacks.Api).HasResourceProperties("AWS::ElasticLoadBalancingV2::LoadBalancer", new Dictionary<string, object>
            {
                ["LoadBalancerAttributes"] = Match.ArrayWith(new object[]
                {
                    new Dictionary<string, object> { ["Key"] = "routing.http.drop_invalid_header_fields.enabled", ["Value"] = "true" },
                    new Dictionary<string, object> { ["Key"] = "routing.http.preserve_host_header.enabled", ["Value"] = "true" },
                    new Dictionary<string, object> { ["Key"] = "routing.http.xff_header_processing.mode", ["Value"] = "append" }
                })
            });
        }

        /// <summary>
        /// Every API behavior of the website is uncached, forwards every viewer header except Host (X-TK-Property,
        /// X-TK-App, X-Forwarded-For), and sets X-Forwarded-Host from the viewer host in a viewer-request function.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void WebsiteForwardsPropertyHeadersToTheApi(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            JsonElement config = TemplateReader.DistributionConfig(env, env.Stacks.Web);
            string functionArn = string.Empty;
            foreach (string pattern in env.Stacks.Config.Web.ApiPathPatterns)
            {
                JsonElement behavior = TemplateReader.Behavior(config, pattern);
                Assert.Equal(_CachingDisabledPolicyId, behavior.GetProperty("CachePolicyId").GetString());
                Assert.Equal(_AllViewerExceptHostHeaderPolicyId, behavior.GetProperty("OriginRequestPolicyId").GetString());
                JsonElement association = behavior.GetProperty("FunctionAssociations").EnumerateArray().Single();
                Assert.Equal("viewer-request", association.GetProperty("EventType").GetString());
                functionArn = association.GetProperty("FunctionARN").GetRawText();
            }

            Assert.Contains("ApiForwardingFunction", functionArn);
            JsonElement function = env.Resources(env.Stacks.Web, "AWS::CloudFront::Function")
                .Single(f => (f.GetProperty("Properties").GetProperty("FunctionCode").GetString() ?? string.Empty).Contains("x-forwarded-host"));
            string code = function.GetProperty("Properties").GetProperty("FunctionCode").GetString() ?? string.Empty;
            Assert.Contains("request.headers['x-forwarded-host'] = { value: request.headers.host.value }", code);
            Assert.Contains("delete request.headers['x-forwarded-host']", code);
        }

        /// <summary>
        /// The shared API runs MultiProperty with its deployment name, the platform hosts of the environment, the
        /// bootstrap administrator email, and the bootstrap password from the platform secret only; each platform host
        /// gets a certificate and an alias record in its property's zone.
        /// </summary>
        /// <param name="name">Shared deployment.</param>
        [Theory]
        [InlineData("shared-dev")]
        [InlineData("shared-prod")]
        public void SharedApiServesThePlatformHosts(string name)
        {
            SynthesizedDeployment deployment = Environments.GetShared(name);
            SharedStacks shared = deployment.Stacks.Shared!;
            string expectedHost = name == "shared-dev" ? "platform.dev.example.test" : "platform.example.test";
            Assert.Equal(new List<string> { expectedHost }, shared.Config.Platform.Hosts);

            Dictionary<string, string> environment = TemplateReader.ServerEnvironment(deployment, shared.Api);
            Assert.Equal("MultiProperty", environment[ServerEnvironmentKeys.DeploymentMode]);
            Assert.Equal("fixture-shared", environment[ServerEnvironmentKeys.DeploymentName]);
            Assert.Equal(expectedHost, environment[ServerEnvironmentKeys.PlatformHosts]);
            Assert.Equal("platform-admin@example.test", environment[ServerEnvironmentKeys.PlatformBootstrapAdminEmail]);
            Assert.DoesNotContain(ServerSecretKeys.PlatformBootstrapAdminPassword, environment.Keys);
            Assert.Contains(ServerSecretKeys.PlatformBootstrapAdminPassword, TemplateReader.ServerSecrets(deployment, shared.Api));
            Assert.Equal(shared.Config.Network.CidrBlock, environment[ServerEnvironmentKeys.TrustedProxies]);

            Template api = deployment.Template(shared.Api);
            api.HasResourceProperties("AWS::SecretsManager::Secret", new Dictionary<string, object>
            {
                ["Name"] = shared.Config.ResourcePath("platform")
            });
            api.HasResourceProperties("AWS::CertificateManager::Certificate", new Dictionary<string, object>
            {
                ["DomainName"] = expectedHost
            });
            Assert.Single(deployment.Resources(shared.Api, "AWS::Route53::RecordSet"),
                r => r.GetProperty("Properties").GetProperty("Name").GetString() == expectedHost + ".");

            // Each property's stacks carry that property's opaque id; shared stacks carry none.
            foreach (PropertyStacks property in deployment.Stacks.Properties)
            {
                Assert.StartsWith(property.Config.Property.Slug + "-", property.Web!.StackName);
                Assert.Contains("\"" + property.Config.Property.Id + "\"", deployment.Json(property.Web).GetRawText());
            }
        }

        /// <summary>
        /// A shared deployment needs a platform host inside a listed property's web domain, distinct from its hosts,
        /// and two properties may not share a slug.
        /// </summary>
        [Fact]
        public void InvalidSharedPlatformsAreRejected()
        {
            string sample = PropertyDefinition.ResolveDirectory(null);
            string second = Path.Combine(PropertyDefinition.EngineRoot(), "infra", "fixtures", "shared", "second");

            ArgumentException missing = Assert.Throws<ArgumentException>(() => DeploymentDefinition.Load(WriteDeployment(null, sample)));
            Assert.Contains("platform.hosts", missing.Message);

            Assert.Throws<ArgumentException>(() => new SynthesizedDeployment("dev", WriteDeployment("platform.elsewhere.test", sample)));
            Assert.Throws<ArgumentException>(() => new SynthesizedDeployment("dev", WriteDeployment("api.example.test", sample)));
            Assert.Throws<ArgumentException>(() => DeploymentDefinition.Load(WriteDeployment("Not A Host", sample)));

            // Same slug as the sample, different id and domains.
            string clash = Path.Combine(Path.GetTempPath(), "trelliskit-infra-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(clash);
            JsonNode root = JsonNode.Parse(File.ReadAllText(Path.Combine(second, PropertyDefinition.PropertyFileName)))!;
            root["slug"] = PropertyDefinition.Load(sample).Slug;
            File.WriteAllText(Path.Combine(clash, PropertyDefinition.PropertyFileName), root.ToJsonString());
            ArgumentException slug = Assert.Throws<ArgumentException>(() => new SynthesizedDeployment("dev", WriteDeployment("platform.example.test", sample, clash)));
            Assert.Contains("slug", slug.Message);
        }

        private static string WriteProperty(string id, string? slug, string? shortName, string name)
        {
            string directory = Path.Combine(Path.GetTempPath(), "trelliskit-infra-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            JsonObject root = new JsonObject
            {
                ["id"] = id,
                ["name"] = name,
                ["domains"] = new JsonObject { ["web"] = "x.test", ["api"] = "api.x.test", ["media"] = "media.x.test" },
                ["contact"] = new JsonObject { ["supportEmail"] = "s@x.test", ["emailFromAddress"] = "n@x.test" }
            };
            if (slug != null) root["slug"] = slug;
            if (shortName != null) root["shortName"] = shortName;
            File.WriteAllText(Path.Combine(directory, PropertyDefinition.PropertyFileName), root.ToJsonString());
            return directory;
        }

        private static string WriteDeployment(string? platformHost, params string[] properties)
        {
            string directory = Path.Combine(Path.GetTempPath(), "trelliskit-infra-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            JsonArray list = new JsonArray();
            foreach (string property in properties) list.Add(property);
            JsonObject deployment = new JsonObject
            {
                ["id"] = "platform-test",
                ["topology"] = "Shared",
                ["properties"] = list
            };
            if (platformHost != null) deployment["platform"] = new JsonObject { ["hosts"] = new JsonArray(platformHost) };
            string file = Path.Combine(directory, "deployment.json");
            File.WriteAllText(file, deployment.ToJsonString());
            return file;
        }

        private static List<JsonElement> Containers(SynthesizedApp app, Stack? api)
        {
            JsonElement taskDefinition = app.Resources(api, "AWS::ECS::TaskDefinition").Single();
            return taskDefinition.GetProperty("Properties").GetProperty("ContainerDefinitions").EnumerateArray().ToList();
        }

        private static string SecretValueFrom(JsonElement container, string name)
        {
            return container.GetProperty("Secrets").EnumerateArray()
                .Single(s => s.GetProperty("Name").GetString() == name)
                .GetProperty("ValueFrom").GetRawText();
        }
    }
}
