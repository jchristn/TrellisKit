namespace TrellisKit.Infra.Tests
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using Amazon.CDK.Assertions;
    using Xunit;

    /// <summary>
    /// DNS, email, observability, budget, and CI deploy role assertions.
    /// </summary>
    public class PlatformTests
    {
        /// <summary>
        /// The deploy role trusts only GitHub OIDC tokens of the repository's deployment environment.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        [InlineData("prod-redirect")]
        public void DeployRoleTrustsOnlyTheRepository(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            Assert.Equal(env.Stacks.Config.GitHub.Subject, "repo:" + env.Stacks.Config.GitHub.Owner + "/" + env.Stacks.Config.GitHub.Repository + ":environment:" + env.Stacks.Config.Name);
            // The provider is an account singleton in its own stack, never in the environment's deploy stack.
            env.Template(env.Stacks.GitHubOidc).HasResourceProperties("AWS::IAM::OIDCProvider", new Dictionary<string, object>
            {
                ["Url"] = "https://token.actions.githubusercontent.com",
                ["ClientIdList"] = new string[] { "sts.amazonaws.com" }
            });
            Template template = env.Template(env.Stacks.GitHubDeploy);
            template.ResourceCountIs("AWS::IAM::OIDCProvider", 0);
            template.HasResourceProperties("AWS::IAM::Role", new Dictionary<string, object>
            {
                ["AssumeRolePolicyDocument"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["Statement"] = new object[]
                    {
                        Match.ObjectLike(new Dictionary<string, object>
                        {
                            ["Action"] = "sts:AssumeRoleWithWebIdentity",
                            ["Principal"] = new Dictionary<string, object>
                            {
                                ["Federated"] = "arn:aws:iam::" + env.Stacks.Config.Account + ":oidc-provider/token.actions.githubusercontent.com"
                            },
                            ["Condition"] = new Dictionary<string, object>
                            {
                                ["StringEquals"] = new Dictionary<string, object>
                                {
                                    ["token.actions.githubusercontent.com:aud"] = "sts.amazonaws.com",
                                    ["token.actions.githubusercontent.com:sub"] = env.Stacks.Config.GitHub.Subject
                                }
                            }
                        })
                    }
                })
            });
        }

        /// <summary>
        /// The deploy role can only push the image, roll the service, sync the website, and invalidate it.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void DeployRoleIsLeastPrivilege(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            JsonElement policy = env.Resources(env.Stacks.GitHubDeploy, "AWS::IAM::Policy").Single();
            List<string> actions = new List<string>();
            foreach (JsonElement statement in policy.GetProperty("Properties").GetProperty("PolicyDocument").GetProperty("Statement").EnumerateArray())
            {
                JsonElement action = statement.GetProperty("Action");
                if (action.ValueKind == JsonValueKind.Array) actions.AddRange(action.EnumerateArray().Select(a => a.GetString() ?? string.Empty));
                else actions.Add(action.GetString() ?? string.Empty);
            }

            Assert.All(actions, a => Assert.DoesNotContain("*", a.Replace("GenerateDataKey*", string.Empty)));
            Assert.All(actions, a => Assert.True(a.StartsWith("ecr:") || a.StartsWith("ecs:") || a.StartsWith("s3:") || a.StartsWith("cloudfront:"), a));
            Assert.Contains("ecr:PutImage", actions);
            Assert.Contains("ecs:UpdateService", actions);
            Assert.Contains("s3:PutObject", actions);
            Assert.Contains("cloudfront:CreateInvalidation", actions);
            Assert.DoesNotContain("iam:PassRole", actions);
        }

        /// <summary>
        /// Both domains get hosted zones; the ALB certificate is regional and CloudFront certificates are in us-east-1.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        [InlineData("dev-redirect")]
        [InlineData("prod-redirect")]
        public void ZonesAndCertificatesAreInTheRightRegions(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            Template dns = env.Template(env.Stacks.Dns);
            int redirects = env.Stacks.Config.Domains.RedirectDomains.Count;
            dns.ResourceCountIs("AWS::Route53::HostedZone", 1 + redirects);
            dns.HasResourceProperties("AWS::Route53::HostedZone", new Dictionary<string, object> { ["Name"] = env.Stacks.Config.Domains.PrimaryDomain + "." });
            foreach (string redirect in env.Stacks.Config.Domains.RedirectDomains)
            {
                dns.HasResourceProperties("AWS::Route53::HostedZone", new Dictionary<string, object> { ["Name"] = redirect + "." });
            }

            dns.HasResourceProperties("AWS::CertificateManager::Certificate", new Dictionary<string, object> { ["DomainName"] = env.Stacks.Config.Domains.ApiHost });
            env.Template(env.Stacks.EdgeCertificates).ResourceCountIs("AWS::CertificateManager::Certificate", redirects > 0 ? 3 : 2);
        }

        /// <summary>
        /// SES identity has DKIM, a MAIL FROM domain, and a DMARC record.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void EmailHasDkimMailFromAndDmarc(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            Template template = env.Template(env.Stacks.Email);
            string domain = env.Stacks.Config.Domains.PrimaryDomain;
            template.HasResourceProperties("AWS::SES::EmailIdentity", new Dictionary<string, object>
            {
                ["EmailIdentity"] = domain,
                ["DkimAttributes"] = new Dictionary<string, object> { ["SigningEnabled"] = true },
                ["MailFromAttributes"] = Match.ObjectLike(new Dictionary<string, object> { ["MailFromDomain"] = "mail." + domain })
            });
            template.HasResourceProperties("AWS::Route53::RecordSet", new Dictionary<string, object>
            {
                ["Name"] = "_dmarc." + domain + ".",
                ["Type"] = "TXT"
            });
            // Three DKIM CNAMEs, MAIL FROM MX and SPF TXT, DMARC TXT.
            template.ResourceCountIs("AWS::Route53::RecordSet", 6);
        }

        /// <summary>
        /// Observability: Prometheus workspace, log retention, five alarms on an encrypted topic with an email subscription.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void ObservabilityAndAlarmsAreWired(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            Template observability = env.Template(env.Stacks.Observability);
            observability.ResourceCountIs("AWS::APS::Workspace", 1);
            observability.HasResourceProperties("AWS::SNS::Subscription", new Dictionary<string, object>
            {
                ["Protocol"] = "email",
                ["Endpoint"] = env.Stacks.Config.Observability.AlarmEmail
            });
            observability.HasResourceProperties("AWS::SNS::Topic", new Dictionary<string, object> { ["KmsMasterKeyId"] = Match.AnyValue() });
            foreach (JsonElement logGroup in env.Resources(env.Stacks.Observability, "AWS::Logs::LogGroup"))
            {
                Assert.True(logGroup.GetProperty("Properties").TryGetProperty("RetentionInDays", out JsonElement _));
            }

            List<JsonElement> alarms = env.Resources(env.Stacks.Alarms, "AWS::CloudWatch::Alarm");
            Assert.Equal(6, alarms.Count);
            foreach (JsonElement alarm in alarms)
            {
                Assert.Equal(1, alarm.GetProperty("Properties").GetProperty("AlarmActions").GetArrayLength());
            }

            string metrics = string.Join(" ", alarms.Select(a => a.GetRawText()));
            Assert.Contains("HTTPCode_Target_5XX_Count", metrics);
            Assert.Contains("UnHealthyHostCount", metrics);
            Assert.Contains("CPUUtilization", metrics);
            Assert.Contains("FreeStorageSpace", metrics);
        }

        /// <summary>
        /// The monthly budget amount comes from configuration.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void BudgetComesFromConfig(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            env.Template(env.Stacks.Budget).HasResourceProperties("AWS::Budgets::Budget", new Dictionary<string, object>
            {
                ["Budget"] = Match.ObjectLike(new Dictionary<string, object>
                {
                    ["TimeUnit"] = "MONTHLY",
                    ["BudgetLimit"] = new Dictionary<string, object> { ["Amount"] = env.Stacks.Config.Budget.MonthlyLimitUsd, ["Unit"] = "USD" }
                })
            });
        }
    }
}
