namespace TrellisKit.Infra.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using Amazon.CDK;
    using Amazon.CDK.Assertions;
    using TrellisKit.Infra.Config;
    using TrellisKit.Infra.Stacks;
    using Xunit;
    using Annotations = Amazon.CDK.Assertions.Annotations;

    /// <summary>
    /// AI infrastructure: the opt-in RecallDB service (nothing is created when it is off), its database and role in
    /// db-bootstrap, the server's RecallDB settings, and the frame-ancestors policy of the assistant embed page.
    /// </summary>
    public class AiTests
    {
        private static readonly Dictionary<string, ConfiguredEnvironment> _Enabled = new Dictionary<string, ConfiguredEnvironment>();

        /// <summary>
        /// With ai.recallDb off (the default) there is no RecallDB task, service, namespace, secret, security group, or
        /// server setting, so AI infrastructure costs nothing.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void RecallDbIsOffByDefaultAndCreatesNothing(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            Assert.False(env.Stacks.Config.Ai.RecallDbEnabled);
            Assert.Null(env.Stacks.Api!.RecallDb);
            Assert.Null(env.Stacks.Network!.RecallDbSecurityGroup);
            Assert.Single(env.Resources(env.Stacks.Api, "AWS::ECS::TaskDefinition"));
            Assert.Single(env.Resources(env.Stacks.Api, "AWS::ECS::Service"));
            Assert.Empty(env.Resources(env.Stacks.Api, "AWS::ServiceDiscovery::PrivateDnsNamespace"));
            string secrets = String.Join("\n", env.Resources(env.Stacks.Api, "AWS::SecretsManager::Secret").Select(s => s.GetRawText()));
            Assert.DoesNotContain("recalldb", secrets);

            Dictionary<string, string> environment = TemplateReader.ServerEnvironment(env, env.Stacks.Api);
            Assert.DoesNotContain(ServerEnvironmentKeys.RecallDbEndpoint, environment.Keys);
            Assert.DoesNotContain(ServerEnvironmentKeys.RecallDbTenant, environment.Keys);
            Assert.DoesNotContain(ServerEnvironmentKeys.RecallDbToken, TemplateReader.ServerSecrets(env, env.Stacks.Api));
            Assert.DoesNotContain("TK_AI_ENABLED", environment.Keys);
            Assert.DoesNotContain("rd_user", BootstrapEnvironment(env)["BOOTSTRAP_SQL"]);
        }

        /// <summary>
        /// With ai.recallDb on, RecallDB runs as a Fargate service pinned to X86_64 with a pinned image, in private
        /// subnets without a public IP, registered in a private DNS namespace, after the API service.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void RecallDbRunsPrivatelyOnAmd64(string name)
        {
            ConfiguredEnvironment env = Enabled(name);
            AiSettings ai = env.Stacks.Config.Ai;
            JsonElement task = RecallDbTask(env);
            JsonElement properties = task.GetProperty("Properties");
            Assert.Equal("X86_64", properties.GetProperty("RuntimePlatform").GetProperty("CpuArchitecture").GetString());
            Assert.Equal("LINUX", properties.GetProperty("RuntimePlatform").GetProperty("OperatingSystemFamily").GetString());

            JsonElement container = properties.GetProperty("ContainerDefinitions").EnumerateArray().Single();
            Assert.Equal(ai.RecallDbImage, container.GetProperty("Image").GetString());
            Assert.Matches(":v[0-9]", container.GetProperty("Image").GetString() ?? string.Empty);
            Dictionary<string, string> environment = EnvironmentOf(container);
            Assert.Equal(RecallDbService.ReadStartScript(), environment["RECALLDB_START_SCRIPT"]);
            Assert.Equal("true", environment["RECALLDB_DB_REQUIRE_ENCRYPTION"]);
            Assert.Equal(ai.RecallDbDatabaseName, environment["RECALLDB_DB_NAME"]);
            Assert.Equal(ai.RecallDbTenant, environment["RECALLDB_TENANT"]);
            List<string> secretNames = container.GetProperty("Secrets").EnumerateArray().Select(s => s.GetProperty("Name").GetString() ?? string.Empty).ToList();
            foreach (string secret in new string[] { "RECALLDB_DB_USER", "RECALLDB_DB_PASS", "RECALLDB_ADMIN_API_KEY", "RECALLDB_SERVER_TOKEN" })
            {
                Assert.Contains(secret, secretNames);
                Assert.DoesNotContain(secret, environment.Keys);
            }

            JsonElement service = env.Resources(env.Stacks.Api, "AWS::ECS::Service")
                .Single(s => s.GetProperty("Properties").GetProperty("ServiceName").GetRawText().Contains("recalldb"));
            JsonElement network = service.GetProperty("Properties").GetProperty("NetworkConfiguration").GetProperty("AwsvpcConfiguration");
            Assert.Equal("DISABLED", network.GetProperty("AssignPublicIp").GetString());
            Assert.Contains("RecallDbSecurityGroup", network.GetProperty("SecurityGroups").GetRawText());
            Assert.Contains("ServiceRegistries", service.GetProperty("Properties").GetRawText());
            Assert.Contains("\"" + ServerServiceLogicalId(env) + "\"", service.GetProperty("DependsOn").GetRawText());

            env.Template(env.Stacks.Api).HasResourceProperties("AWS::ServiceDiscovery::PrivateDnsNamespace", new Dictionary<string, object>
            {
                ["Name"] = env.Stacks.Config.ResourcePrefix + ".internal"
            });
        }

        /// <summary>
        /// Only the API tasks reach RecallDB, and RecallDB reaches the database.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void RecallDbIsReachableOnlyFromTheApi(string name)
        {
            ConfiguredEnvironment env = Enabled(name);
            int port = env.Stacks.Config.Ai.RecallDbPort;
            List<JsonElement> ingress = env.Resources(env.Stacks.Network, "AWS::EC2::SecurityGroupIngress");
            JsonElement fromApi = ingress.Single(r => r.GetProperty("Properties").GetProperty("GroupId").GetRawText().Contains("RecallDbSecurityGroup"));
            Assert.Equal(port, fromApi.GetProperty("Properties").GetProperty("FromPort").GetInt32());
            Assert.Contains("ServiceSecurityGroup", fromApi.GetProperty("Properties").GetProperty("SourceSecurityGroupId").GetRawText());
            Assert.Contains(ingress, r => r.GetProperty("Properties").GetProperty("GroupId").GetRawText().Contains("DatabaseSecurityGroup")
                && r.GetProperty("Properties").GetProperty("SourceSecurityGroupId").GetRawText().Contains("RecallDbSecurityGroup"));
        }

        /// <summary>
        /// The server gets the RecallDB endpoint and tenant as plain variables and the token only from Secrets Manager.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void ServerReceivesRecallDbEndpointTenantAndToken(string name)
        {
            ConfiguredEnvironment env = Enabled(name);
            EnvironmentConfig config = env.Stacks.Config;
            Dictionary<string, string> environment = TemplateReader.ServerEnvironment(env, env.Stacks.Api);
            Assert.Equal("http://recalldb." + config.ResourcePrefix + ".internal:8600", environment[ServerEnvironmentKeys.RecallDbEndpoint]);
            Assert.Equal("default", environment[ServerEnvironmentKeys.RecallDbTenant]);
            Assert.DoesNotContain(ServerEnvironmentKeys.RecallDbToken, environment.Keys);
            Assert.Contains(ServerEnvironmentKeys.RecallDbToken, TemplateReader.ServerSecrets(env, env.Stacks.Api));
            foreach (string suffix in new string[] { "recalldb-database", "recalldb-admin-key", "recalldb-token" })
            {
                env.Template(env.Stacks.Api).HasResourceProperties("AWS::SecretsManager::Secret", new Dictionary<string, object>
                {
                    ["Name"] = config.ResourcePath(suffix)
                });
            }
        }

        /// <summary>
        /// db-bootstrap creates RecallDB's role and database (least privilege, extensions pre-created) after the
        /// TrellisKit grants, with the password from its secret.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void BootstrapCreatesTheRecallDbDatabaseAndRole(string name)
        {
            ConfiguredEnvironment env = Enabled(name);
            Dictionary<string, string> environment = BootstrapEnvironment(env);
            string sql = environment["BOOTSTRAP_SQL"];
            Assert.StartsWith(ApiStack.BootstrapSql, sql);
            Assert.EndsWith(ApiStack.RecallDbBootstrapSql, sql);
            Assert.Contains("CREATE DATABASE %I", sql);
            Assert.Contains("GRANT CONNECT, TEMPORARY ON DATABASE :\"rd_db\" TO :\"rd_user\"", sql);
            Assert.Contains("CREATE EXTENSION IF NOT EXISTS pg_trgm", sql);
            Assert.DoesNotContain("OWNER", ApiStack.RecallDbBootstrapSql);
            Assert.Equal(env.Stacks.Config.Ai.RecallDbDatabaseName, environment["RECALLDB_DB_NAME"]);

            JsonElement bootstrap = TemplateReader.ApiContainer(env, env.Stacks.Api, ApiStack.BootstrapContainerName);
            List<string> secrets = bootstrap.GetProperty("Secrets").EnumerateArray().Select(s => s.GetProperty("Name").GetString() ?? string.Empty).ToList();
            Assert.Contains("RECALLDB_DB_USERNAME", secrets);
            Assert.Contains("RECALLDB_DB_PASSWORD", secrets);
            Assert.Contains("rd_password=\"$RECALLDB_DB_PASSWORD\"", bootstrap.GetProperty("Command").GetRawText().Replace("\\u0022", "\"").Replace("\\\"", "\""));
        }

        /// <summary>
        /// Enabling RecallDB keeps every stack free of errors and unsuppressed cdk-nag findings.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void RecallDbIsNagClean(string name)
        {
            ConfiguredEnvironment env = Enabled(name);
            foreach (Stack stack in env.Stacks.All)
            {
                Annotations.FromStack(stack).HasNoError("*", Match.AnyValue());
            }

            Assert.Empty(env.NagFindings());
        }

        /// <summary>
        /// The embed page path is the only website path without X-Frame-Options: it sends frame-ancestors 'self' by
        /// default, while every other website response keeps X-Frame-Options DENY.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void EmbedPageSendsFrameAncestorsInsteadOfFrameOptions(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            JsonElement distribution = TemplateReader.DistributionConfig(env, env.Stacks.Web);
            JsonElement embed = TemplateReader.Behavior(distribution, env.Stacks.Config.Web.EmbedPathPattern);
            Assert.Equal(distribution.GetProperty("DefaultCacheBehavior").GetProperty("TargetOriginId").GetString(), embed.GetProperty("TargetOriginId").GetString());
            Assert.Contains("RoutingFunction", embed.GetProperty("FunctionAssociations").GetRawText());

            JsonElement embedHeaders = HeadersPolicy(env, embed);
            Assert.False(embedHeaders.TryGetProperty("FrameOptions", out JsonElement _));
            Assert.Equal("frame-ancestors 'self'", embedHeaders.GetProperty("ContentSecurityPolicy").GetProperty("ContentSecurityPolicy").GetString());

            JsonElement siteHeaders = HeadersPolicy(env, distribution.GetProperty("DefaultCacheBehavior"));
            Assert.Equal("DENY", siteHeaders.GetProperty("FrameOptions").GetProperty("FrameOption").GetString());
        }

        /// <summary>
        /// Configured embedding origins are appended to 'self', and a website CSP is kept in front of frame-ancestors.
        /// </summary>
        [Fact]
        public void ConfiguredEmbedOriginsReachTheEmbedPolicy()
        {
            ConfiguredEnvironment env = new ConfiguredEnvironment("dev", config =>
            {
                config.Ai.EmbedFrameAncestors = new List<string> { "https://Partner.example.test/", "https://blog.example.test:8443" };
                config.Web.ContentSecurityPolicy = "default-src 'self';";
            });
            JsonElement distribution = TemplateReader.DistributionConfig(env, env.Stacks.Web);
            JsonElement embedHeaders = HeadersPolicy(env, TemplateReader.Behavior(distribution, env.Stacks.Config.Web.EmbedPathPattern));
            Assert.Equal("default-src 'self'; frame-ancestors 'self' https://partner.example.test https://blog.example.test:8443",
                embedHeaders.GetProperty("ContentSecurityPolicy").GetProperty("ContentSecurityPolicy").GetString());
        }

        /// <summary>
        /// The ai section of deploy/infra.json is parsed and validated, and the context values override it.
        /// </summary>
        [Fact]
        public void AiSectionAndContextAreReadAndValidated()
        {
            using (JsonDocument document = JsonDocument.Parse("{\"ai\":{\"recallDb\":true,\"recallDbCpu\":512,\"recallDbTenant\":\"knowledge\",\"embedFrameAncestors\":[\"https://a.example.test\"]}}"))
            {
                AiSettings ai = AiSettings.FromJson(document.RootElement, "infra.json");
                Assert.True(ai.RecallDbEnabled);
                Assert.Equal(512, ai.RecallDbCpu);
                Assert.Equal("knowledge", ai.RecallDbTenant);
                Assert.Equal("'self' https://a.example.test", ai.FrameAncestorsSources);
            }

            using (JsonDocument empty = JsonDocument.Parse("{}"))
            {
                AiSettings defaults = AiSettings.FromJson(empty.RootElement, "infra.json");
                Assert.False(defaults.RecallDbEnabled);
                Assert.Equal("'self'", defaults.FrameAncestorsSources);
            }

            foreach (string invalid in new string[] { "{\"ai\":{\"recallDb\":\"yes\"}}", "{\"ai\":{\"embedFrameAncestors\":[\"http://a.example.test\"]}}", "{\"ai\":{\"recallDbImage\":\"recalldb:latest\"}}" })
            {
                using (JsonDocument document = JsonDocument.Parse(invalid))
                {
                    JsonElement root = document.RootElement;
                    Assert.Throws<ArgumentException>(() => AiSettings.FromJson(root, "infra.json"));
                }
            }

            App app = new App(new AppProps
            {
                Context = new Dictionary<string, object>
                {
                    [EnvironmentConfigFactory.EnvironmentContextKey] = "dev",
                    [EnvironmentConfigFactory.RecallDbContextKey] = "true",
                    [EnvironmentConfigFactory.EmbedFrameAncestorsContextKey] = "https://a.example.test, https://b.example.test"
                }
            });
            EnvironmentConfig config = EnvironmentConfigFactory.FromContext(app);
            Assert.True(config.Ai.RecallDbEnabled);
            Assert.Equal(new string[] { "https://a.example.test", "https://b.example.test" }, config.Ai.EmbedFrameAncestors.ToArray());

            App invalidApp = new App(new AppProps
            {
                Context = new Dictionary<string, object>
                {
                    [EnvironmentConfigFactory.EnvironmentContextKey] = "dev",
                    [EnvironmentConfigFactory.RecallDbContextKey] = "maybe"
                }
            });
            Assert.Throws<ArgumentException>(() => EnvironmentConfigFactory.FromContext(invalidApp));
        }

        /// <summary>
        /// The Docker stack and the ECS task share one start script, which binds RecallDB to every interface and never
        /// keeps the image's well-known admin key or seeded credential.
        /// </summary>
        [Fact]
        public void StartScriptReplacesTheImageDefaults()
        {
            string script = RecallDbService.ReadStartScript();
            Assert.Contains("\"Hostname\": \"*\"", script);
            Assert.Contains("\"AdminApiKeys\": [ \"$RECALLDB_ADMIN_API_KEY\" ]", script);
            Assert.Contains("credentials/default", script);
            Assert.DoesNotContain("recalldbadmin\"", script.Replace("well-known admin API key recalldbadmin", string.Empty));
            Assert.True(File.Exists(Path.Combine(PropertyDefinition.EngineRoot(), RecallDbService.StartScriptPath)));
        }

        private static ConfiguredEnvironment Enabled(string name)
        {
            if (!_Enabled.TryGetValue(name, out ConfiguredEnvironment? env))
            {
                env = new ConfiguredEnvironment(name, config => config.Ai.RecallDbEnabled = true);
                _Enabled[name] = env;
            }

            return env;
        }

        private static JsonElement RecallDbTask(SynthesizedApp app)
        {
            Stack? api = app is ConfiguredEnvironment configured ? configured.Stacks.Api : null;
            return app.Resources(api, "AWS::ECS::TaskDefinition")
                .Single(t => t.GetProperty("Properties").GetProperty("ContainerDefinitions").EnumerateArray().Any(c => c.GetProperty("Name").GetString() == RecallDbService.ContainerName));
        }

        private static string ServerServiceLogicalId(ConfiguredEnvironment env)
        {
            return env.Json(env.Stacks.Api).GetProperty("Resources").EnumerateObject()
                .Single(r => r.Value.GetProperty("Type").GetString() == "AWS::ECS::Service" && !r.Value.GetProperty("Properties").GetProperty("ServiceName").GetRawText().Contains("recalldb"))
                .Name;
        }

        private static Dictionary<string, string> BootstrapEnvironment(SynthesizedApp app)
        {
            Stack? api = app is ConfiguredEnvironment configured ? configured.Stacks.Api : ((SynthesizedEnvironment)app).Stacks.Api;
            return EnvironmentOf(TemplateReader.ApiContainer(app, api, ApiStack.BootstrapContainerName));
        }

        private static Dictionary<string, string> EnvironmentOf(JsonElement container)
        {
            Dictionary<string, string> environment = new Dictionary<string, string>();
            foreach (JsonElement item in container.GetProperty("Environment").EnumerateArray())
            {
                JsonElement value = item.GetProperty("Value");
                environment[item.GetProperty("Name").GetString() ?? string.Empty] = value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();
            }

            return environment;
        }

        private static JsonElement HeadersPolicy(SynthesizedApp app, JsonElement behavior)
        {
            Stack? web = app is ConfiguredEnvironment configured ? configured.Stacks.Web : ((SynthesizedEnvironment)app).Stacks.Web;
            string id = behavior.GetProperty("ResponseHeadersPolicyId").GetProperty("Ref").GetString() ?? string.Empty;
            return app.Resource(web, id).GetProperty("Properties").GetProperty("ResponseHeadersPolicyConfig").GetProperty("SecurityHeadersConfig");
        }
    }
}
