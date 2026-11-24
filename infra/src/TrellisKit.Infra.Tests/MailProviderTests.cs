namespace TrellisKit.Infra.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using Amazon.CDK;
    using TrellisKit.Infra.Config;
    using Xunit;

    /// <summary>
    /// mail.provider decides whether the Amazon SES identity exists: Mailgun and None create no Email stack, no SES DNS
    /// records, and no SES SMTP endpoint; every other value and the default keep SES.
    /// </summary>
    public class MailProviderTests
    {
        /// <summary>
        /// Provider values map to the backend provider and the SES decision.
        /// </summary>
        [Fact]
        public void ProvidersMapToTheBackendAndTheSesIdentity()
        {
            MailSettings mail = new MailSettings();
            Assert.True(mail.CreatesSesIdentity);
            Assert.Equal(String.Empty, mail.ServerProvider);

            mail.Provider = "Ses";
            Assert.True(mail.CreatesSesIdentity);
            Assert.Equal("Smtp", mail.ServerProvider);
            Assert.False(mail.UsesMailgun);

            mail.Provider = "None";
            Assert.False(mail.CreatesSesIdentity);
            Assert.Equal(String.Empty, mail.ServerProvider);
            Assert.False(mail.UsesMailgun);

            mail.Provider = "Mailgun";
            Assert.False(mail.CreatesSesIdentity);
            Assert.Equal("Mailgun", mail.ServerProvider);
            Assert.True(mail.UsesMailgun);

            foreach (string provider in MailSettings.BackendProviders.Where(p => p != "Mailgun"))
            {
                mail.Provider = provider;
                Assert.True(mail.CreatesSesIdentity);
                Assert.Equal(provider, mail.ServerProvider);
            }

            using (JsonDocument document = JsonDocument.Parse("{\"mail\":{\"provider\":\"None\"}}"))
            {
                Assert.False(MailSettings.FromJson(document.RootElement, "infra.json").CreatesSesIdentity);
            }
        }

        /// <summary>
        /// The engine default (no mail section) keeps the SES identity and its SMTP endpoint.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void DefaultKeepsSes(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            Assert.NotNull(env.Stacks.Email);
            env.Template(env.Stacks.Email).ResourceCountIs("AWS::SES::EmailIdentity", 1);
            Dictionary<string, string> environment = TemplateReader.ServerEnvironment(env, env.Stacks.Api);
            Assert.Equal("email-smtp." + env.Stacks.Config.Region + ".amazonaws.com", environment["TK_SMTP_HOST"]);
            Assert.False(env.Json(env.Stacks.Api).GetProperty("Outputs").TryGetProperty("MailDnsRecords", out JsonElement _));
        }

        /// <summary>
        /// Mailgun (the redirect fixture): no Email stack, no SES resource or SMTP endpoint anywhere, the Mailgun secret,
        /// and an output naming Mailgun's records.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev-redirect")]
        [InlineData("prod-redirect")]
        public void MailgunCreatesNoSesIdentity(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            Assert.Null(env.Stacks.Email);
            AssertNoSes(env, env.Stacks.All);

            Dictionary<string, string> environment = TemplateReader.ServerEnvironment(env, env.Stacks.Api);
            Assert.Equal("Mailgun", environment["TK_EMAIL_PROVIDER"]);
            Assert.DoesNotContain("TK_SMTP_HOST", environment.Keys);
            Assert.DoesNotContain("TK_SMTP_PORT", environment.Keys);
            Assert.DoesNotContain("TK_SMTP_USE_TLS", environment.Keys);
            Assert.NotNull(env.Stacks.Api!.MailgunSecret);

            string records = env.Json(env.Stacks.Api).GetProperty("Outputs").GetProperty("MailDnsRecords").GetProperty("Value").GetString() ?? String.Empty;
            Assert.Contains("Mailgun dashboard", records);
            Assert.Contains(Environments.FixtureMailgunDomain, records);
        }

        /// <summary>
        /// Mailgun with external DNS: no SES record outputs, and the Mailgun records are named instead.
        /// </summary>
        [Fact]
        public void ExternalDnsWithMailgunListsNoSesRecords()
        {
            ConfiguredEnvironment env = new ConfiguredEnvironment("prod", config =>
            {
                config.Dns.Mode = DnsModeEnum.External;
                config.Mail.Provider = "Mailgun";
            });
            Assert.Null(env.Stacks.Email);
            AssertNoSes(env, env.Stacks.All);
            foreach (Stack stack in env.Stacks.All)
            {
                if (!env.Json(stack).TryGetProperty("Outputs", out JsonElement outputs)) continue;
                foreach (JsonProperty output in outputs.EnumerateObject())
                {
                    Assert.False(output.Name.StartsWith("DnsRecordSes", StringComparison.Ordinal), output.Name);
                    Assert.False(output.Name.StartsWith("DnsRecordDmarc", StringComparison.Ordinal), output.Name);
                }
            }

            Assert.True(env.Json(env.Stacks.Api).GetProperty("Outputs").TryGetProperty("MailDnsRecords", out JsonElement _));
        }

        /// <summary>
        /// None: no SES identity, no Mailgun secret, and no provider passed (the backend default and console apply).
        /// </summary>
        [Fact]
        public void NoneCreatesNoMailInfrastructure()
        {
            ConfiguredEnvironment env = new ConfiguredEnvironment("dev", config => config.Mail.Provider = "None");
            Assert.Null(env.Stacks.Email);
            AssertNoSes(env, env.Stacks.All);
            Assert.Null(env.Stacks.Api!.MailgunSecret);
            Dictionary<string, string> environment = TemplateReader.ServerEnvironment(env, env.Stacks.Api);
            Assert.DoesNotContain("TK_EMAIL_PROVIDER", environment.Keys);
            Assert.DoesNotContain("TK_SMTP_HOST", environment.Keys);
            Assert.False(env.Json(env.Stacks.Api).GetProperty("Outputs").TryGetProperty("MailDnsRecords", out JsonElement _));
        }

        /// <summary>
        /// Ses keeps the identity and passes Smtp as the backend provider.
        /// </summary>
        [Fact]
        public void SesPassesSmtp()
        {
            ConfiguredEnvironment env = new ConfiguredEnvironment("dev", config => config.Mail.Provider = "Ses");
            Assert.NotNull(env.Stacks.Email);
            Dictionary<string, string> environment = TemplateReader.ServerEnvironment(env, env.Stacks.Api);
            Assert.Equal("Smtp", environment["TK_EMAIL_PROVIDER"]);
            Assert.Contains("TK_SMTP_HOST", environment.Keys);
        }

        /// <summary>
        /// In the shared topology the deployment file's mail.provider Mailgun removes every property's Email stack,
        /// while the default fixture keeps one per property.
        /// </summary>
        [Fact]
        public void SharedDeploymentMailgunRemovesEveryPropertyEmailStack()
        {
            Assert.All(Environments.GetShared("shared-prod").Stacks.Properties, p => Assert.NotNull(p.Email));

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
            root["mail"] = new JsonObject { ["provider"] = "Mailgun" };
            string directory = Path.Combine(Path.GetTempPath(), "trelliskit-infra-tests-mailgun-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, "deployment.json");
            File.WriteAllText(file, root.ToJsonString());

            SynthesizedDeployment deployment = new SynthesizedDeployment("prod", file);
            Assert.All(deployment.Stacks.Properties, p => Assert.Null(p.Email));
            foreach (PropertyStacks property in deployment.Stacks.Properties) AssertNoSes(deployment, property.All);
            Assert.DoesNotContain("TK_SMTP_HOST", TemplateReader.ServerEnvironment(deployment, deployment.Stacks.Shared!.Api).Keys);
        }

        private static void AssertNoSes(SynthesizedApp app, List<Stack> stacks)
        {
            foreach (Stack stack in stacks)
            {
                foreach (JsonProperty resource in app.Json(stack).GetProperty("Resources").EnumerateObject())
                {
                    string type = resource.Value.GetProperty("Type").GetString() ?? String.Empty;
                    Assert.False(type.StartsWith("AWS::SES::", StringComparison.Ordinal), stack.StackName + " has " + type);
                    Assert.DoesNotContain("amazonses.com", resource.Value.GetRawText());
                }
            }
        }
    }
}
