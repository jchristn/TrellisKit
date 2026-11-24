namespace TrellisKit.Infra.Tests
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using Amazon.CDK.Assertions;
    using TrellisKit.Infra.Stacks;
    using Xunit;

    /// <summary>
    /// ECS, ALB, and container wiring assertions.
    /// </summary>
    public class ApiTests
    {
        private static readonly string[] _SecretVariables = new string[]
        {
            "TK_DB_PASSWORD", "TK_DB_USERNAME", "TK_ADMIN_TOKEN", "TK_TOKEN_ENCRYPTION_KEY",
            "TK_STRIPE_SECRET_KEY", "TK_STRIPE_WEBHOOK_SECRET", "TK_APPSTORE_PRIVATE_KEY",
            "TK_GOOGLEPLAY_SERVICE_ACCOUNT_JSON", "TK_FACEBOOK_APP_SECRET", "TK_SMTP_PASSWORD",
            "TK_MEDIA_SIGNING_PRIVATE_KEY"
        };

        /// <summary>
        /// HTTP on port 80 permanently redirects to HTTPS on 443.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void HttpListenerRedirectsToHttps(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            Template template = env.Template(env.Stacks.Api);
            template.HasResourceProperties("AWS::ElasticLoadBalancingV2::Listener", new Dictionary<string, object>
            {
                ["Port"] = 80,
                ["Protocol"] = "HTTP",
                ["DefaultActions"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["Type"] = "redirect",
                        ["RedirectConfig"] = new Dictionary<string, object> { ["Protocol"] = "HTTPS", ["Port"] = "443", ["StatusCode"] = "HTTP_301" }
                    }
                }
            });
            template.HasResourceProperties("AWS::ElasticLoadBalancingV2::Listener", new Dictionary<string, object>
            {
                ["Port"] = 443,
                ["Protocol"] = "HTTPS",
                ["Certificates"] = Match.AnyValue()
            });
        }

        /// <summary>
        /// The load balancer keeps idle WebSocket connections and health checks readiness.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void LoadBalancerIsWebSocketFriendlyAndChecksReadiness(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            Template template = env.Template(env.Stacks.Api);
            template.HasResourceProperties("AWS::ElasticLoadBalancingV2::LoadBalancer", new Dictionary<string, object>
            {
                ["LoadBalancerAttributes"] = Match.ArrayWith(new object[]
                {
                    new Dictionary<string, object> { ["Key"] = "idle_timeout.timeout_seconds", ["Value"] = "120" }
                })
            });
            template.HasResourceProperties("AWS::ElasticLoadBalancingV2::TargetGroup", new Dictionary<string, object>
            {
                ["HealthCheckPath"] = "/api/v1/health/ready",
                ["TargetType"] = "ip",
                ["Port"] = 8000
            });
        }

        /// <summary>
        /// Autoscaling bounds follow the environment (at least two tasks in production).
        /// </summary>
        /// <param name="name">Environment.</param>
        /// <param name="min">Minimum tasks.</param>
        /// <param name="max">Maximum tasks.</param>
        [Theory]
        [InlineData("dev", 1, 2)]
        [InlineData("prod", 2, 6)]
        public void AutoscalingFollowsTheEnvironment(string name, int min, int max)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            Template template = env.Template(env.Stacks.Api);
            template.HasResourceProperties("AWS::ApplicationAutoScaling::ScalableTarget", new Dictionary<string, object>
            {
                ["MinCapacity"] = min,
                ["MaxCapacity"] = max
            });
            template.ResourceCountIs("AWS::ApplicationAutoScaling::ScalingPolicy", 2);
            template.HasResourceProperties("AWS::ECS::Service", new Dictionary<string, object>
            {
                ["DesiredCount"] = min,
                ["DeploymentConfiguration"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["DeploymentCircuitBreaker"] = new Dictionary<string, object> { ["Enable"] = true, ["Rollback"] = true }
                })
            });
        }

        /// <summary>
        /// Secrets reach the server only through ECS secrets, never as plaintext environment variables.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void SecretsAreNotPlaintextEnvironmentVariables(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            JsonElement server = ServerContainer(env);

            List<string> environment = server.GetProperty("Environment").EnumerateArray()
                .Select(e => e.GetProperty("Name").GetString() ?? string.Empty).ToList();
            List<string> secrets = server.GetProperty("Secrets").EnumerateArray()
                .Select(e => e.GetProperty("Name").GetString() ?? string.Empty).ToList();

            foreach (string variable in _SecretVariables)
            {
                Assert.DoesNotContain(variable, environment);
                Assert.Contains(variable, secrets);
            }

            foreach (string key in ServerSecretKeys.AllKeys)
            {
                Assert.Contains(key, secrets);
            }

            foreach (JsonElement secret in server.GetProperty("Secrets").EnumerateArray())
            {
                Assert.Contains("ValueFrom", secret.GetRawText());
            }

            string environmentText = server.GetProperty("Environment").GetRawText();
            Assert.DoesNotContain("PRIVATE KEY", environmentText);
        }

        /// <summary>
        /// The server receives its non-secret configuration and exports OTLP to the ADOT sidecar.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void ServerIsConfiguredAndExportsToTheSidecar(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            JsonElement server = ServerContainer(env);
            Dictionary<string, string> environment = new Dictionary<string, string>();
            foreach (JsonElement item in server.GetProperty("Environment").EnumerateArray())
            {
                JsonElement value = item.GetProperty("Value");
                environment[item.GetProperty("Name").GetString() ?? string.Empty] = value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();
            }

            Assert.Equal("http://127.0.0.1:4317", environment["TK_OTLP_ENDPOINT"]);
            Assert.Equal("Grpc", environment["TK_OTLP_PROTOCOL"]);
            Assert.Equal("Postgresql", environment["TK_DB_TYPE"]);
            Assert.Equal("0.0.0.0", environment["TK_REST_HOSTNAME"]);
            Assert.Equal(env.Stacks.Config.Domains.WebsiteUrl, environment["TK_WEB_PUBLIC_BASE_URL"]);
            Assert.Contains(env.Stacks.Config.Domains.WebsiteUrl, environment["TK_CORS_ORIGINS"]);
            Assert.True(environment.ContainsKey("TK_DB_HOSTNAME"));
            Assert.True(environment.ContainsKey("TK_DB_PORT"));
            Assert.True(environment.ContainsKey("TK_DB_DATABASE"));
            Assert.Equal("/app/property", environment["TK_PROPERTY_DIR"]);
            Assert.Equal(env.Stacks.Config.Property.EmailFromLocalPart + "@" + env.Stacks.Config.Domains.PrimaryDomain, environment["TK_SMTP_FROM_ADDRESS"]);
            foreach (string bucket in new string[] { "SOURCE", "VIDEO_SOURCE", "IMAGES", "TRANSCODED", "RETAINED" })
            {
                Assert.True(environment.ContainsKey("TK_MEDIA_" + bucket + "_BUCKET"), bucket);
            }

            if (!string.IsNullOrEmpty(env.Stacks.Config.Property.BundleId))
            {
                Assert.Equal(env.Stacks.Config.Property.BundleId, environment["TK_APPSTORE_BUNDLE_ID"]);
            }

            JsonElement collector = Containers(env).Single(c => c.GetProperty("Name").GetString() == ApiStack.CollectorContainerName);
            Assert.Contains("aws-otel-collector", collector.GetProperty("Image").GetString());
            string collectorConfig = collector.GetProperty("Environment").GetRawText();
            Assert.Contains("prometheusremotewrite", collectorConfig);
            Assert.Contains("awsxray", collectorConfig);
            Assert.Contains("sigv4auth", collectorConfig);
        }

        /// <summary>
        /// Container Insights is enabled on the cluster.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void ContainerInsightsIsEnabled(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            env.Template(env.Stacks.Api).HasResourceProperties("AWS::ECS::Cluster", new Dictionary<string, object>
            {
                ["ClusterSettings"] = new object[] { new Dictionary<string, object> { ["Name"] = "containerInsights", ["Value"] = "enabled" } }
            });
        }

        private static List<JsonElement> Containers(SynthesizedEnvironment env)
        {
            JsonElement taskDefinition = env.Resources(env.Stacks.Api, "AWS::ECS::TaskDefinition").Single();
            return taskDefinition.GetProperty("Properties").GetProperty("ContainerDefinitions").EnumerateArray().ToList();
        }

        private static JsonElement ServerContainer(SynthesizedEnvironment env)
        {
            return Containers(env).Single(c => c.GetProperty("Name").GetString() == ApiStack.ServerContainerName);
        }
    }
}
