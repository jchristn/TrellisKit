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
    /// External DNS mode (dns.mode External): no Route 53 resources, certificates requested without waiting, and every
    /// record the operator must create listed in stack outputs.
    /// </summary>
    public class ExternalDnsTests
    {
        /// <summary>
        /// No stack creates a hosted zone, a record, or a native (waiting) certificate.
        /// </summary>
        /// <param name="name">Environment or shared deployment.</param>
        [Theory]
        [InlineData("dev-single")]
        [InlineData("prod-single")]
        [InlineData("shared-external-dev")]
        [InlineData("shared-external-prod")]
        public void NoRoute53ResourcesOrWaitingCertificates(string name)
        {
            SynthesizedApp app = App(name, out List<Stack> stacks);
            foreach (Stack stack in stacks)
            {
                foreach (JsonProperty resource in app.Json(stack).GetProperty("Resources").EnumerateObject())
                {
                    string type = resource.Value.GetProperty("Type").GetString() ?? String.Empty;
                    Assert.False(type.StartsWith("AWS::Route53::", StringComparison.Ordinal), stack.StackName + " has " + type);
                    Assert.NotEqual("AWS::CertificateManager::Certificate", type);
                }

                Annotations.FromStack(stack).HasNoError("*", Match.AnyValue());
            }

            Assert.Empty(app.NagFindings());
        }

        /// <summary>
        /// The certificate stacks list one validation CNAME per host name, including the CloudFront certificates in
        /// us-east-1, and each certificate ARN.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev-single")]
        [InlineData("prod-single")]
        public void ValidationRecordsAreOutputs(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            Assert.True(env.Stacks.Config.Dns.IsExternal);
            Assert.Null(env.Stacks.Dns!.PrimaryZone);

            Dictionary<string, string> dns = Outputs(env, env.Stacks.Dns);
            Assert.Contains("ApiCertificateArn", dns.Keys);
            Assert.Contains("ValidationName0", dns["DnsValidationApi0"]);
            Assert.Contains(" CNAME ", dns["DnsValidationApi0"]);
            env.Template(env.Stacks.Dns).ResourceCountIs(CertificateRequester.ResourceType, 1);
            env.Template(env.Stacks.Dns).HasResourceProperties(CertificateRequester.ResourceType, new Dictionary<string, object>
            {
                ["DomainNames"] = new string[] { env.Stacks.Config.Domains.ApiHost }
            });

            Assert.Equal("us-east-1", env.Stacks.EdgeCertificates!.Region);
            Dictionary<string, string> edge = Outputs(env, env.Stacks.EdgeCertificates);
            foreach (string key in new string[] { "DnsValidationWeb0", "DnsValidationWeb1", "DnsValidationMedia0", "DnsValidationRedirect0", "DnsValidationRedirect1" })
            {
                Assert.Contains(key, edge.Keys);
            }

            env.Template(env.Stacks.EdgeCertificates).ResourceCountIs(CertificateRequester.ResourceType, 3);
            env.Template(env.Stacks.EdgeCertificates).HasResourceProperties(CertificateRequester.ResourceType, new Dictionary<string, object>
            {
                ["DomainNames"] = new string[] { env.Stacks.Config.Domains.PrimaryDomain, env.Stacks.Config.Domains.WwwHost }
            });
        }

        /// <summary>
        /// Every record the operator creates is an output: website apex and www, API load balancer, media and updates,
        /// the vanity redirect, and SES (DKIM, MAIL FROM, DMARC). The production apex reads ALIAS; dev hosts are CNAMEs.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev-single")]
        [InlineData("prod-single")]
        public void EveryOperatorRecordIsAnOutput(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            DomainSettings domains = env.Stacks.Config.Domains;
            bool prod = name.StartsWith("prod", StringComparison.Ordinal);

            Dictionary<string, string> web = Outputs(env, env.Stacks.Web);
            Assert.StartsWith(domains.PrimaryDomain + (prod ? " ALIAS " : " CNAME "), web["DnsRecordWebApex"]);
            Assert.StartsWith(domains.WwwHost + " CNAME ", web["DnsRecordWebWww"]);
            Assert.Contains("WebDistribution", web["DnsRecordWebApex"]);

            Dictionary<string, string> api = Outputs(env, env.Stacks.Api);
            Assert.StartsWith(domains.ApiHost + " CNAME ", api["DnsRecordApi"]);
            Assert.Contains("LoadBalancer", api["DnsRecordApi"]);

            Assert.StartsWith(domains.MediaHost + " CNAME ", Outputs(env, env.Stacks.Media)["DnsRecordMedia"]);

            Dictionary<string, string> redirect = Outputs(env, env.Stacks.Redirect);
            Assert.StartsWith(domains.RedirectDomains[0] + (prod ? " ALIAS " : " CNAME "), redirect["DnsRecordRedirectApex"]);
            Assert.StartsWith(domains.RedirectWwwHost(domains.RedirectDomains[0]) + " CNAME ", redirect["DnsRecordRedirectWww"]);

            Dictionary<string, string> email = Outputs(env, env.Stacks.Email);
            foreach (string key in new string[] { "DnsRecordSesDkim1", "DnsRecordSesDkim2", "DnsRecordSesDkim3" }) Assert.Contains(" CNAME ", email[key]);
            Assert.Equal("mail." + domains.PrimaryDomain + " MX 10 feedback-smtp.us-west-2.amazonses.com", email["DnsRecordSesMailFromMx"]);
            Assert.Equal("mail." + domains.PrimaryDomain + " TXT v=spf1 include:amazonses.com ~all", email["DnsRecordSesMailFromSpf"]);
            Assert.StartsWith("_dmarc." + domains.PrimaryDomain + " TXT v=DMARC1;", email["DnsRecordDmarc"]);
        }

        /// <summary>
        /// The stacks that use a certificate create nothing until it is issued: every other resource depends on the
        /// certificate check, which names the stack whose outputs hold the missing validation records.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev-single")]
        [InlineData("prod-single")]
        public void ConsumersWaitForIssuedCertificates(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            Stack?[] consumers = new Stack?[] { env.Stacks.Api, env.Stacks.Web, env.Stacks.Media, env.Stacks.Redirect };
            foreach (Stack? stack in consumers)
            {
                JsonElement check = env.Resources(stack, CertificateIssuedCheck.ResourceType).Single();
                string hint = check.GetProperty("Properties").GetProperty("Hint").GetString() ?? String.Empty;
                Assert.Contains("DnsValidation", hint);
                Assert.Contains(stack == env.Stacks.Api ? env.Stacks.Dns!.StackName : env.Stacks.EdgeCertificates!.StackName, hint);

                string listenerOrDistribution = stack == env.Stacks.Api ? "AWS::ElasticLoadBalancingV2::Listener" : "AWS::CloudFront::Distribution";
                foreach (JsonElement resource in env.Resources(stack, listenerOrDistribution))
                {
                    string dependsOn = resource.GetProperty("DependsOn").GetRawText();
                    Assert.Contains("CertificateCheck", dependsOn);
                }
            }

            JsonElement service = env.Resources(env.Stacks.Api, "AWS::ECS::Service").Single();
            Assert.Contains("CertificateCheck", service.GetProperty("DependsOn").GetRawText());
            JsonElement secret = env.Resources(env.Stacks.Api, "AWS::SecretsManager::Secret").First();
            Assert.Contains("CertificateCheck", secret.GetProperty("DependsOn").GetRawText());
        }

        /// <summary>
        /// Shared topology: each property's API host and every platform host are records on the shared load balancer,
        /// and a platform host's certificate is requested by its property's DNS stack (deployed before the API).
        /// </summary>
        /// <param name="name">Shared deployment.</param>
        [Theory]
        [InlineData("shared-external-dev")]
        [InlineData("shared-external-prod")]
        public void SharedTopologyListsApiAndPlatformRecords(string name)
        {
            SynthesizedDeployment deployment = Environments.GetShared(name);
            DeploymentStacks stacks = deployment.Stacks;
            Dictionary<string, string> api = Outputs(deployment, stacks.Shared!.Api);
            foreach (PropertyStacks property in stacks.Properties)
            {
                Assert.True(property.Dns!.IsExternal);
                Assert.Contains(api.Values, v => v.StartsWith(property.Config.Domains.ApiHost + " CNAME ", StringComparison.Ordinal));
            }

            string host = stacks.Shared.Config.Platform.Hosts.Single();
            Assert.Contains(api.Values, v => v.StartsWith(host + " CNAME ", StringComparison.Ordinal));

            PropertyStacks owner = stacks.Properties.Single(p => host.EndsWith("." + p.Config.Domains.PrimaryDomain, StringComparison.Ordinal));
            deployment.Template(owner.Dns).HasResourceProperties(CertificateRequester.ResourceType, new Dictionary<string, object>
            {
                ["DomainNames"] = new string[] { host }
            });
            Assert.Contains(Outputs(deployment, owner.Dns).Keys, k => k.StartsWith("DnsValidationPlatform", StringComparison.Ordinal));
            deployment.Template(stacks.Shared.Api).ResourceCountIs(CertificateIssuedCheck.ResourceType, 1);
        }

        /// <summary>
        /// dns.mode is read from deploy/infra.json and the deployment file, the dnsMode context value overrides both,
        /// Route 53 stays the default, and an unknown mode is rejected.
        /// </summary>
        [Fact]
        public void DnsModeIsConfigurable()
        {
            Assert.Equal(DnsModeEnum.Route53, EnvironmentConfigFactory.Dev().Dns.Mode);
            Assert.Equal(DnsModeEnum.External, Environments.Get("dev-single").Stacks.Config.Dns.Mode);
            Assert.Equal(DnsModeEnum.External, Environments.GetShared("shared-external-dev").Stacks.Shared!.Config.Dns.Mode);

            App app = new App(new AppProps
            {
                Context = new Dictionary<string, object>
                {
                    [EnvironmentConfigFactory.EnvironmentContextKey] = "prod",
                    [EnvironmentConfigFactory.DnsModeContextKey] = "external"
                }
            });
            Assert.Equal(DnsModeEnum.External, EnvironmentConfigFactory.FromContext(app).Dns.Mode);

            App back = new App(new AppProps
            {
                Context = new Dictionary<string, object>
                {
                    [EnvironmentConfigFactory.EnvironmentContextKey] = "prod",
                    [EnvironmentConfigFactory.PropertyDirContextKey] = Environments.SingleAccountPropertyDir,
                    [EnvironmentConfigFactory.DnsModeContextKey] = "Route53"
                }
            });
            Assert.Equal(DnsModeEnum.Route53, EnvironmentConfigFactory.FromContext(back).Dns.Mode);

            App bad = new App(new AppProps
            {
                Context = new Dictionary<string, object>
                {
                    [EnvironmentConfigFactory.EnvironmentContextKey] = "prod",
                    [EnvironmentConfigFactory.DnsModeContextKey] = "Cloudflare"
                }
            });
            Assert.Throws<ArgumentException>(() => EnvironmentConfigFactory.FromContext(bad));

            string directory = Path.Combine(Path.GetTempPath(), "trelliskit-infra-dns-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(directory, "deploy"));
            File.Copy(Path.Combine(PropertyDefinition.ResolveDirectory(null), PropertyDefinition.PropertyFileName), Path.Combine(directory, PropertyDefinition.PropertyFileName));
            File.WriteAllText(Path.Combine(directory, PropertyDefinition.DeployFileName), "{\"dns\":{\"mode\":\"Manual\"}}");
            Assert.Throws<ArgumentException>(() => PropertyDefinition.Load(directory));
        }

        /// <summary>
        /// Route 53 mode is unchanged: zones, alias records, and native certificates, and no certificate custom resources.
        /// </summary>
        [Fact]
        public void Route53ModeKeepsZonesAndRecords()
        {
            SynthesizedEnvironment env = Environments.Get("prod-redirect");
            Assert.NotNull(env.Stacks.Dns!.PrimaryZone);
            env.Template(env.Stacks.Dns).ResourceCountIs(CertificateRequester.ResourceType, 0);
            env.Template(env.Stacks.Api).ResourceCountIs(CertificateIssuedCheck.ResourceType, 0);
            env.Template(env.Stacks.Api).ResourceCountIs("AWS::Route53::RecordSet", 1);
            Assert.DoesNotContain(Outputs(env, env.Stacks.Web).Keys, k => k.StartsWith(DnsRecordOutputs.RecordPrefix, StringComparison.Ordinal));
        }

        private static SynthesizedApp App(string name, out List<Stack> stacks)
        {
            if (name.StartsWith("shared", StringComparison.Ordinal))
            {
                SynthesizedDeployment deployment = Environments.GetShared(name);
                stacks = deployment.Stacks.All;
                return deployment;
            }

            SynthesizedEnvironment env = Environments.Get(name);
            stacks = env.Stacks.All;
            return env;
        }

        private static Dictionary<string, string> Outputs(SynthesizedApp app, Stack? stack)
        {
            Dictionary<string, string> outputs = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!app.Json(stack).TryGetProperty("Outputs", out JsonElement all)) return outputs;
            foreach (JsonProperty output in all.EnumerateObject())
            {
                JsonElement value = output.Value.GetProperty("Value");
                outputs[output.Name] = value.ValueKind == JsonValueKind.String ? value.GetString() ?? String.Empty : Flatten(value);
            }

            return outputs;
        }

        private static string Flatten(JsonElement value)
        {
            // Fn::Join parts become one string; references keep their raw JSON so tests can look for logical ids.
            if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("Fn::Join", out JsonElement join))
            {
                JsonElement[] parts = join.EnumerateArray().ToArray();
                string separator = parts[0].GetString() ?? String.Empty;
                return String.Join(separator, parts[1].EnumerateArray().Select(p => p.ValueKind == JsonValueKind.String ? p.GetString() ?? String.Empty : p.GetRawText()));
            }

            return value.GetRawText();
        }
    }
}
