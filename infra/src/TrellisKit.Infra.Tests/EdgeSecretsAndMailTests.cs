namespace TrellisKit.Infra.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Text.RegularExpressions;
    using Amazon.CDK;
    using Amazon.CDK.Assertions;
    using TrellisKit.Infra.Config;
    using TrellisKit.Infra.Stacks;
    using Xunit;
    using Match = Amazon.CDK.Assertions.Match;

    /// <summary>
    /// Multi-property part 2 wiring: the engine secrets key, the CloudFront viewer address path and its origin listener,
    /// trusted proxy ranges, deployment mail, outbound HTTPS, and the Mailgun webhook route.
    /// </summary>
    public class EdgeSecretsAndMailTests
    {
        private static readonly string _AllViewerExceptHostHeaderPolicyId = "b689b0a8-53d0-40ab-baf2-68738e2966ac";
        private static readonly string _MailgunWebhookPath = "/api/v1/webhooks/mailgun/app_mwd0f1x7_SecondFixture0001";
        private static readonly string[] _MailLiveKeys = new string[] { "TK_EMAIL_PROVIDER", "TK_MAILGUN_DOMAIN", "TK_MAILGUN_BASE_URL" };

        /// <summary>
        /// The secrets key lives in its own secret with a pinned template value, is filled once by a custom resource
        /// before the service starts, and reaches the server only as ECS secrets.
        /// </summary>
        /// <param name="name">Environment.</param>
        /// <param name="retained">Whether the secret survives stack deletion.</param>
        [Theory]
        [InlineData("dev", false)]
        [InlineData("prod", true)]
        public void SecretsKeyIsGeneratedOnceAndInjectedAsSecret(string name, bool retained)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            Template template = env.Template(env.Stacks.Api);

            // Pinned: a changed template value would make CloudFormation overwrite the generated key.
            Assert.Equal("{\"TK_SECRETS_ENCRYPTION_KEY\":\"\",\"TK_SECRETS_PREVIOUS_ENCRYPTION_KEYS\":\"\"}", SecretsKeySecret.TemplateValue);
            template.HasResource("AWS::SecretsManager::Secret", new Dictionary<string, object>
            {
                ["Properties"] = new Dictionary<string, object>
                {
                    ["Name"] = env.Stacks.Config.ResourcePath("secrets-key"),
                    ["SecretString"] = SecretsKeySecret.TemplateValue
                },
                ["DeletionPolicy"] = retained ? "Retain" : "Delete"
            });

            KeyValuePair<string, JsonElement> initializer = Single(env, env.Stacks.Api, "Custom::SecretsKeyInitializer");
            Assert.True(initializer.Value.GetProperty("Properties").TryGetProperty("SecretArn", out JsonElement _));
            KeyValuePair<string, JsonElement> service = Single(env, env.Stacks.Api, "AWS::ECS::Service");
            List<string> dependsOn = service.Value.GetProperty("DependsOn").EnumerateArray().Select(d => d.GetString() ?? String.Empty).ToList();
            Assert.Contains(initializer.Key, dependsOn);

            template.HasResourceProperties("AWS::Lambda::Function", new Dictionary<string, object> { ["Runtime"] = "nodejs26.x" });

            List<string> secrets = TemplateReader.ServerSecrets(env, env.Stacks.Api);
            Dictionary<string, string> environment = TemplateReader.ServerEnvironment(env, env.Stacks.Api);
            foreach (string key in new string[] { ServerEnvironmentKeys.SecretsEncryptionKey, ServerEnvironmentKeys.SecretsPreviousEncryptionKeys })
            {
                Assert.Contains(key, secrets);
                Assert.DoesNotContain(key, environment.Keys);
            }
        }

        /// <summary>
        /// The initializer writes base64 of 32 random bytes only into an empty key, never replaces a set key, never
        /// logs or returns the key, and does nothing on delete.
        /// </summary>
        [Fact]
        public void SecretsKeyInitializerOnlyFillsAnEmptyKey()
        {
            string handler = SecretsKeySecret.BuildHandler();
            Assert.Contains("crypto.randomBytes(32).toString('base64')", handler);
            Assert.Contains("if (event.RequestType !== 'Delete')", handler);
            Assert.Contains("current[KEY].trim().length > 0", handler);
            Assert.Single(Regex.Matches(handler, "PutSecretValueCommand\\(\\{"));
            Assert.Contains("Data: {}", handler);
            foreach (string line in handler.Split('\n').Where(l => l.Contains("console.log", StringComparison.Ordinal)))
            {
                Assert.DoesNotContain("current", line);
            }

            Assert.True(handler.Length < 4096, "Inline Lambda code must stay under the 4 KB ZipFile limit.");
        }

        /// <summary>
        /// Every website API behavior forwards the viewer headers plus CloudFront-Viewer-Address (the managed
        /// AllViewerExceptHostHeader policy includes CloudFront's viewer location headers) to the CloudFront origin
        /// listener port.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void WebsiteApiBehaviorsForwardTheViewerAddressToTheCloudFrontListener(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            JsonElement config = TemplateReader.DistributionConfig(env, env.Stacks.Web);
            foreach (string pattern in env.Stacks.Config.Web.ApiPathPatterns)
            {
                JsonElement behavior = TemplateReader.Behavior(config, pattern);
                Assert.Equal(_AllViewerExceptHostHeaderPolicyId, behavior.GetProperty("OriginRequestPolicyId").GetString());
                string originId = behavior.GetProperty("TargetOriginId").GetString() ?? String.Empty;
                JsonElement origin = config.GetProperty("Origins").EnumerateArray().Single(o => o.GetProperty("Id").GetString() == originId);
                Assert.Equal(env.Stacks.Config.Api.CloudFrontOriginHttpsPort, origin.GetProperty("CustomOriginConfig").GetProperty("HTTPSPort").GetInt32());
            }
        }

        /// <summary>
        /// The CloudFront origin listener port is open only to the CloudFront origin-facing prefix list, while the public
        /// HTTPS port stays open for the apps.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void CloudFrontListenerAdmitsOnlyCloudFront(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            ApiSettings api = env.Stacks.Config.Api;
            List<JsonElement> ingress = Ingress(env, env.Stacks.Network);

            List<JsonElement> cloudFront = ingress.Where(r => r.GetProperty("FromPort").GetInt32() == api.CloudFrontOriginHttpsPort).ToList();
            JsonElement rule = Assert.Single(cloudFront);
            Assert.Equal(env.Stacks.Config.Network.CloudFrontOriginFacingPrefixListId, rule.GetProperty("SourcePrefixListId").GetString());
            Assert.False(rule.TryGetProperty("CidrIp", out JsonElement _));

            // Mobile apps call the load balancer directly on the public port.
            Assert.Contains(ingress, r => r.GetProperty("FromPort").GetInt32() == api.HttpsPort
                && r.TryGetProperty("CidrIp", out JsonElement cidr) && cidr.GetString() == "0.0.0.0/0");

            JsonElement loadBalancer = Single(env, env.Stacks.Api, "AWS::ElasticLoadBalancingV2::LoadBalancer").Value;
            Assert.Equal(2, loadBalancer.GetProperty("Properties").GetProperty("SecurityGroups").GetArrayLength());

            env.Template(env.Stacks.Api).HasResourceProperties("AWS::ElasticLoadBalancingV2::Listener", new Dictionary<string, object>
            {
                ["Port"] = api.CloudFrontOriginHttpsPort,
                ["Protocol"] = "HTTPS",
                ["Certificates"] = Match.AnyValue(),
                ["DefaultActions"] = new object[] { Match.ObjectLike(new Dictionary<string, object> { ["Type"] = "forward" }) }
            });
        }

        /// <summary>
        /// A request on the public listener that carries the viewer address header is refused, so a direct caller
        /// cannot choose the address the server records. It is the only rule, and it matches no path.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void PublicListenerRejectsAViewerAddressHeader(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            JsonElement rule = All(env, env.Stacks.Api, "AWS::ElasticLoadBalancingV2::ListenerRule")
                .Select(r => r.Value.GetProperty("Properties"))
                .Single(r => r.GetProperty("Conditions").EnumerateArray().Any(c => c.GetProperty("Field").GetString() == "http-header"));
            Assert.Equal(PublicListenerId(env, env.Stacks.Api), rule.GetProperty("ListenerArn").GetProperty("Ref").GetString());

            JsonElement condition = Assert.Single(rule.GetProperty("Conditions").EnumerateArray());
            Assert.Equal("http-header", condition.GetProperty("Field").GetString());
            JsonElement header = condition.GetProperty("HttpHeaderConfig");
            Assert.Equal("CloudFront-Viewer-Address", header.GetProperty("HttpHeaderName").GetString());
            Assert.Equal("*", Assert.Single(header.GetProperty("Values").EnumerateArray()).GetString());

            JsonElement action = Assert.Single(rule.GetProperty("Actions").EnumerateArray());
            Assert.Equal("fixed-response", action.GetProperty("Type").GetString());
            Assert.Equal("400", action.GetProperty("FixedResponseConfig").GetProperty("StatusCode").GetString());
        }

        /// <summary>
        /// Mailgun event webhooks reach the server: the public listener forwards every path to the service, and the
        /// website forwards /api/* to the API.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void MailgunWebhookIsRoutedToTheServer(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            string targetGroup = Single(env, env.Stacks.Api, "AWS::ElasticLoadBalancingV2::TargetGroup").Key;
            foreach (KeyValuePair<string, JsonElement> listener in All(env, env.Stacks.Api, "AWS::ElasticLoadBalancingV2::Listener"))
            {
                JsonElement properties = listener.Value.GetProperty("Properties");
                if (properties.GetProperty("Protocol").GetString() != "HTTPS") continue;
                JsonElement action = Assert.Single(properties.GetProperty("DefaultActions").EnumerateArray());
                Assert.Equal("forward", action.GetProperty("Type").GetString());
                Assert.Equal(targetGroup, action.GetProperty("TargetGroupArn").GetProperty("Ref").GetString());
            }

            // The only path rule is the realtime route, which forwards to the same target group, so nothing diverts the
            // webhook from the default action.
            foreach (KeyValuePair<string, JsonElement> rule in All(env, env.Stacks.Api, "AWS::ElasticLoadBalancingV2::ListenerRule"))
            {
                foreach (JsonElement condition in rule.Value.GetProperty("Properties").GetProperty("Conditions").EnumerateArray())
                {
                    if (condition.GetProperty("Field").GetString() != "path-pattern") continue;
                    Assert.Equal("/api/v1/ws", Assert.Single(condition.GetProperty("PathPatternConfig").GetProperty("Values").EnumerateArray()).GetString());
                    JsonElement forward = Assert.Single(rule.Value.GetProperty("Properties").GetProperty("Actions").EnumerateArray());
                    Assert.Equal(targetGroup, forward.GetProperty("TargetGroupArn").GetProperty("Ref").GetString());
                }
            }

            JsonElement config = TemplateReader.DistributionConfig(env, env.Stacks.Web);
            string? pattern = env.Stacks.Config.Web.ApiPathPatterns.FirstOrDefault(p => Matches(p, _MailgunWebhookPath));
            Assert.Equal("/api/*", pattern);
            JsonElement behavior = TemplateReader.Behavior(config, pattern!);
            List<string> methods = behavior.GetProperty("AllowedMethods").EnumerateArray().Select(m => m.GetString() ?? String.Empty).ToList();
            Assert.Contains("POST", methods);
        }

        /// <summary>
        /// Tasks reach Mailgun and the DNS-over-HTTPS resolver: all outbound traffic is allowed and leaves through the NAT
        /// gateways, with no network ACL or firewall in the way.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void TasksHaveOutboundHttps(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            Template template = env.Template(env.Stacks.Network);
            template.HasResourceProperties("AWS::EC2::SecurityGroup", new Dictionary<string, object>
            {
                ["GroupDescription"] = "API Fargate tasks",
                ["SecurityGroupEgress"] = Match.ArrayWith(new object[]
                {
                    Match.ObjectLike(new Dictionary<string, object> { ["CidrIp"] = "0.0.0.0/0", ["IpProtocol"] = "-1" })
                })
            });
            template.HasResourceProperties("AWS::EC2::Route", new Dictionary<string, object>
            {
                ["DestinationCidrBlock"] = "0.0.0.0/0",
                ["NatGatewayId"] = Match.AnyValue()
            });
            template.ResourceCountIs("AWS::EC2::NetworkAcl", 0);
            template.ResourceCountIs("AWS::NetworkFirewall::Firewall", 0);
        }

        /// <summary>
        /// Without configuration, no live proxy, resolver, or mail setting is passed, no Mailgun secret exists, and
        /// TK_MAILGUN_API_KEY is not injected.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void UnconfiguredLiveSettingsAreNotEmitted(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            Dictionary<string, string> environment = TemplateReader.ServerEnvironment(env, env.Stacks.Api);
            foreach (string key in new string[] { "TK_ADDITIONAL_TRUSTED_PROXIES", "TK_DOMAIN_VERIFICATION_RESOLVER_URL", "TK_VIEWER_ADDRESS_HEADER" }.Concat(_MailLiveKeys))
            {
                Assert.DoesNotContain(key, environment.Keys);
            }

            foreach (string key in new string[] { "TK_ADDITIONAL_TRUSTED_PROXIES", "TK_DOMAIN_VERIFICATION_RESOLVER_URL" }.Concat(_MailLiveKeys))
            {
                Assert.Contains(key, ServerEnvironmentKeys.OptionalLiveKeys);
            }

            Assert.DoesNotContain(ServerEnvironmentKeys.MailgunApiKey, TemplateReader.ServerSecrets(env, env.Stacks.Api));
            Assert.Null(env.Stacks.Api!.MailgunSecret);
            Assert.DoesNotContain(env.Resources(env.Stacks.Api, "AWS::SecretsManager::Secret"),
                s => s.GetProperty("Properties").GetProperty("Name").GetString() == env.Stacks.Config.ResourcePath("mailgun"));
        }

        /// <summary>
        /// Configured trusted proxy ranges and resolver URL are passed to the server.
        /// </summary>
        [Fact]
        public void ConfiguredProxiesAndResolverAreEmitted()
        {
            ConfiguredEnvironment env = new ConfiguredEnvironment("prod", config =>
            {
                config.Network.AdditionalTrustedProxies = new List<string> { "203.0.113.0/24", " 198.51.100.0/22 ", "203.0.113.0/24" };
                config.Api.DomainVerificationResolverUrl = "https://dns.google/resolve";
            });
            Dictionary<string, string> environment = TemplateReader.ServerEnvironment(env, env.Stacks.Api);
            Assert.Equal("203.0.113.0/24,198.51.100.0/22", environment["TK_ADDITIONAL_TRUSTED_PROXIES"]);
            Assert.Equal("https://dns.google/resolve", environment["TK_DOMAIN_VERIFICATION_RESOLVER_URL"]);
            Assert.Equal(env.Stacks.Config.Network.CidrBlock, environment["TK_TRUSTED_PROXIES"]);
        }

        /// <summary>
        /// The additionalTrustedProxies and cloudFrontPrefixListId context values configure every environment; bad
        /// values stop synthesis.
        /// </summary>
        [Fact]
        public void ProxyContextValuesAreReadAndValidated()
        {
            App app = new App(new AppProps
            {
                Context = new Dictionary<string, object>
                {
                    [EnvironmentConfigFactory.EnvironmentContextKey] = "dev",
                    [EnvironmentConfigFactory.AdditionalTrustedProxiesContextKey] = "130.176.0.0/17, 2600:9000:2000::/36,15.158.0.0/16",
                    [EnvironmentConfigFactory.CloudFrontPrefixListContextKey] = "pl-3b927c52"
                }
            });
            EnvironmentConfig config = EnvironmentConfigFactory.FromContext(app);
            Assert.Equal(new List<string> { "130.176.0.0/17", "2600:9000:2000::/36", "15.158.0.0/16" }, config.Network.AdditionalTrustedProxies);
            Assert.Equal("pl-3b927c52", config.Network.CloudFrontOriginFacingPrefixListId);

            NetworkSettings network = new NetworkSettings();
            Assert.Equal("pl-82a045eb", network.CloudFrontOriginFacingPrefixListId);
            Assert.Throws<ArgumentException>(() => network.AdditionalTrustedProxies = new List<string> { "cloudfront" });
            Assert.Throws<ArgumentException>(() => network.CloudFrontOriginFacingPrefixListId = "sg-12345678");
            Assert.Throws<ArgumentException>(() => new ApiSettings().DomainVerificationResolverUrl = "http://cloudflare-dns.com/dns-query");
        }

        /// <summary>
        /// A property's deploy/infra.json mail section sets the provider, domain, and region, and the Mailgun key comes
        /// only from the {prefix}/mailgun secret.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev-redirect")]
        [InlineData("prod-redirect")]
        public void PropertyMailIsEmittedAndTheKeyIsASecret(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            Dictionary<string, string> environment = TemplateReader.ServerEnvironment(env, env.Stacks.Api);
            Assert.Equal("Mailgun", environment["TK_EMAIL_PROVIDER"]);
            Assert.Equal(Environments.FixtureMailgunDomain, environment["TK_MAILGUN_DOMAIN"]);
            Assert.Equal(Environments.FixtureMailgunBaseUrl, environment["TK_MAILGUN_BASE_URL"]);
            AssertMailgunSecret(env, env.Stacks.Api, env.Stacks.Config);
        }

        /// <summary>
        /// The deployment file's mail section configures the shared API.
        /// </summary>
        [Fact]
        public void SharedDeploymentMailIsEmitted()
        {
            string file = WriteSharedDeploymentWithMail();
            SynthesizedDeployment deployment = new SynthesizedDeployment("prod", file);
            SharedStacks shared = deployment.Stacks.Shared!;
            Dictionary<string, string> environment = TemplateReader.ServerEnvironment(deployment, shared.Api);
            Assert.DoesNotContain("TK_EMAIL_PROVIDER", environment.Keys);
            Assert.Equal("mg.platform.example.test", environment["TK_MAILGUN_DOMAIN"]);
            Assert.DoesNotContain("TK_MAILGUN_BASE_URL", environment.Keys);
            AssertMailgunSecret(deployment, shared.Api, shared.Config);

            // The CloudFront origin listener, the rejection rule, and the realtime rules exist on the shared load balancer too.
            Assert.Equal(3, deployment.Resources(shared.Api, "AWS::ElasticLoadBalancingV2::ListenerRule").Count);
            Assert.Contains(deployment.Resources(shared.Api, "AWS::ElasticLoadBalancingV2::Listener"),
                l => l.GetProperty("Properties").GetProperty("Port").GetInt32() == shared.Config.Api.CloudFrontOriginHttpsPort);
            foreach (PropertyStacks property in deployment.Stacks.Properties)
            {
                JsonElement config = TemplateReader.DistributionConfig(deployment, property.Web);
                Assert.Contains(config.GetProperty("Origins").EnumerateArray(), o => o.TryGetProperty("CustomOriginConfig", out JsonElement custom)
                    && custom.GetProperty("HTTPSPort").GetInt32() == shared.Config.Api.CloudFrontOriginHttpsPort);
            }
        }

        /// <summary>
        /// Mail values are validated when the file is read.
        /// </summary>
        [Fact]
        public void MailSettingsAreValidated()
        {
            MailSettings mail = new MailSettings();
            Assert.False(mail.UsesMailgun);
            mail.Provider = "Smtp";
            Assert.False(mail.UsesMailgun);
            mail.Provider = "Mailgun";
            Assert.True(mail.UsesMailgun);
            Assert.Throws<ArgumentException>(() => mail.Provider = "Sendgrid");
            Assert.Throws<ArgumentException>(() => mail.MailgunDomain = "not a domain");
            Assert.Throws<ArgumentException>(() => mail.MailgunBaseUrl = "http://api.mailgun.net/v3/");

            using (JsonDocument document = JsonDocument.Parse("{\"mail\":{\"mailgunDomain\":\"MG.Example.Test\"}}"))
            {
                MailSettings parsed = MailSettings.FromJson(document.RootElement, "infra.json");
                Assert.Equal("mg.example.test", parsed.MailgunDomain);
                Assert.True(parsed.UsesMailgun);
                Assert.Equal(String.Empty, parsed.Provider);
            }

            using (JsonDocument document = JsonDocument.Parse("{\"mail\":{\"provider\":\"Postmark\"}}"))
            {
                ArgumentException error = Assert.Throws<ArgumentException>(() => MailSettings.FromJson(document.RootElement, "infra.json"));
                Assert.Contains("infra.json", error.Message);
            }
        }

        private static void AssertMailgunSecret(SynthesizedApp app, Stack? api, EnvironmentConfig config)
        {
            app.Template(api).HasResourceProperties("AWS::SecretsManager::Secret", new Dictionary<string, object>
            {
                ["Name"] = config.ResourcePath("mailgun"),
                ["SecretString"] = "{\"TK_MAILGUN_API_KEY\":\"\"}"
            });
            Assert.Contains(ServerEnvironmentKeys.MailgunApiKey, TemplateReader.ServerSecrets(app, api));
            Assert.DoesNotContain(ServerEnvironmentKeys.MailgunApiKey, TemplateReader.ServerEnvironment(app, api).Keys);
        }

        private static string WriteSharedDeploymentWithMail()
        {
            string engine = PropertyDefinition.EngineRoot();
            string fixture = Path.Combine(engine, Environments.SharedDeploymentFile);
            JsonNode root = JsonNode.Parse(File.ReadAllText(fixture))!;
            string folder = Path.GetDirectoryName(fixture)!;
            JsonArray properties = new JsonArray();
            foreach (JsonNode? item in root["properties"]!.AsArray())
            {
                properties.Add(Path.GetFullPath(Path.Combine(folder, item!.GetValue<string>())));
            }

            root["properties"] = properties;
            root["mail"] = new JsonObject { ["mailgunDomain"] = "mg.platform.example.test" };
            string directory = Path.Combine(Path.GetTempPath(), "trelliskit-infra-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, "deployment.json");
            File.WriteAllText(file, root.ToJsonString());
            return file;
        }

        private static List<JsonElement> Ingress(SynthesizedApp app, Stack? network)
        {
            List<JsonElement> rules = new List<JsonElement>();
            foreach (JsonElement group in app.Resources(network, "AWS::EC2::SecurityGroup"))
            {
                if (group.GetProperty("Properties").TryGetProperty("SecurityGroupIngress", out JsonElement inline))
                {
                    rules.AddRange(inline.EnumerateArray());
                }
            }

            foreach (JsonElement ingress in app.Resources(network, "AWS::EC2::SecurityGroupIngress")) rules.Add(ingress.GetProperty("Properties"));
            return rules;
        }

        private static string PublicListenerId(SynthesizedApp app, Stack? api)
        {
            return All(app, api, "AWS::ElasticLoadBalancingV2::Listener")
                .Single(l => l.Value.GetProperty("Properties").GetProperty("Port").GetInt32() == 443).Key;
        }

        private static KeyValuePair<string, JsonElement> Single(SynthesizedApp app, Stack? stack, string type)
        {
            return Assert.Single(All(app, stack, type));
        }

        private static List<KeyValuePair<string, JsonElement>> All(SynthesizedApp app, Stack? stack, string type)
        {
            List<KeyValuePair<string, JsonElement>> resources = new List<KeyValuePair<string, JsonElement>>();
            foreach (JsonProperty resource in app.Json(stack).GetProperty("Resources").EnumerateObject())
            {
                if (resource.Value.GetProperty("Type").GetString() == type) resources.Add(new KeyValuePair<string, JsonElement>(resource.Name, resource.Value));
            }

            return resources;
        }

        private static bool Matches(string pattern, string path)
        {
            string regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(path, regex);
        }
    }
}
