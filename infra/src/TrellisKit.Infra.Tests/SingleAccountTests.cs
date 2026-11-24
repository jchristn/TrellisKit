namespace TrellisKit.Infra.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using Amazon.CDK;
    using Amazon.CDK.Assertions;
    using TrellisKit.Infra.Config;
    using TrellisKit.Infra.Stacks;
    using Xunit;

    /// <summary>
    /// Dev and prod deployed into one AWS account and region: no stack, physical name, or account singleton collides.
    /// </summary>
    public class SingleAccountTests
    {
        // Physical names that must be unique in an account (and region for regional types). Inline IAM policy names
        // (AWS::IAM::Policy PolicyName) are scoped to their role and are not listed; Family counts only for ECS task
        // definitions (an RDS parameter group's Family is the engine version).
        private static readonly HashSet<string> _NameKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "Alias", "AliasName", "Aliases", "AlarmName", "BucketName", "BudgetName", "ClusterName", "ConfigurationSetName",
            "DBInstanceIdentifier", "EmailIdentity", "FunctionName", "KeyGroupName", "LoadBalancerName", "LogGroupName",
            "Name", "RepositoryName", "RoleName", "RuleName", "ServiceName", "TopicName", "Url", "ZoneName"
        };

        private static readonly string[] _GlobalPrefixes = new string[] { "AWS::IAM::", "AWS::CloudFront::", "AWS::S3::", "AWS::Budgets::", "AWS::Route53::" };

        /// <summary>
        /// Synthesizing dev and prod of one property with the same account id yields no duplicate stack (except the
        /// account-level GitHub OIDC stack, whose two templates are identical), no duplicate physical name, and exactly
        /// one GitHub OIDC provider.
        /// </summary>
        [Fact]
        public void PerPropertyDevAndProdInOneAccountNeverCollide()
        {
            SynthesizedEnvironment dev = Environments.Get("dev-single");
            SynthesizedEnvironment prod = Environments.Get("prod-single");
            Assert.Equal(Environments.SingleAccount, dev.Stacks.Config.Account);
            Assert.Equal(Environments.SingleAccount, prod.Stacks.Config.Account);
            Assert.Equal(dev.Stacks.Config.Region, prod.Stacks.Config.Region);

            List<KeyValuePair<SynthesizedApp, Stack>> stacks = new List<KeyValuePair<SynthesizedApp, Stack>>();
            foreach (Stack stack in dev.Stacks.All) stacks.Add(new KeyValuePair<SynthesizedApp, Stack>(dev, stack));
            foreach (Stack stack in prod.Stacks.All) stacks.Add(new KeyValuePair<SynthesizedApp, Stack>(prod, stack));
            AssertNoCollisions(stacks, "TrellisKit-GitHubOidc");
        }

        /// <summary>
        /// The same holds for both environments of a shared deployment in one account.
        /// </summary>
        [Fact]
        public void SharedDevAndProdInOneAccountNeverCollide()
        {
            SynthesizedDeployment dev = Environments.GetShared("shared-external-dev");
            SynthesizedDeployment prod = Environments.GetShared("shared-external-prod");

            List<KeyValuePair<SynthesizedApp, Stack>> stacks = new List<KeyValuePair<SynthesizedApp, Stack>>();
            foreach (Stack stack in dev.Stacks.All) stacks.Add(new KeyValuePair<SynthesizedApp, Stack>(dev, stack));
            foreach (Stack stack in prod.Stacks.All) stacks.Add(new KeyValuePair<SynthesizedApp, Stack>(prod, stack));
            AssertNoCollisions(stacks, "TrellisKit-GitHubOidc");
        }

        /// <summary>
        /// Both deploy roles trust the one provider of the account, which neither GitHubDeploy stack creates.
        /// </summary>
        [Fact]
        public void BothDeployRolesTrustTheAccountProvider()
        {
            string arn = "arn:aws:iam::" + Environments.SingleAccount + ":oidc-provider/token.actions.githubusercontent.com";
            foreach (string name in new string[] { "dev-single", "prod-single" })
            {
                SynthesizedEnvironment env = Environments.Get(name);
                Assert.Equal(arn, env.Stacks.GitHubDeploy!.OidcProviderArn);
                env.Template(env.Stacks.GitHubDeploy).ResourceCountIs("AWS::IAM::OIDCProvider", 0);
                Assert.Contains(env.Stacks.GitHubOidc!, env.Stacks.GitHubDeploy.Dependencies);
                JsonElement provider = env.Resources(env.Stacks.GitHubOidc, "AWS::IAM::OIDCProvider").Single();
                Assert.Equal("Retain", provider.GetProperty("DeletionPolicy").GetString());
            }

            Assert.Equal(
                Environments.Get("dev-single").Json(Environments.Get("dev-single").Stacks.GitHubOidc).GetRawText(),
                Environments.Get("prod-single").Json(Environments.Get("prod-single").Stacks.GitHubOidc).GetRawText());
        }

        /// <summary>
        /// With a shared account each budget counts only its own environment (cost allocation tag); with separate
        /// accounts (the sample's placeholders) the budget covers the account as before.
        /// </summary>
        [Fact]
        public void SharedAccountScopesEachBudgetToItsEnvironment()
        {
            foreach (string name in new string[] { "dev-single", "prod-single" })
            {
                SynthesizedEnvironment env = Environments.Get(name);
                Assert.True(env.Stacks.Config.SharedAccount);
                env.Template(env.Stacks.Budget).HasResourceProperties("AWS::Budgets::Budget", new Dictionary<string, object>
                {
                    ["Budget"] = Match.ObjectLike(new Dictionary<string, object>
                    {
                        ["CostFilters"] = new Dictionary<string, object>
                        {
                            ["TagKeyValue"] = new string[] { "user:" + BudgetSettings.CostTagKey + "$" + env.Stacks.Config.StackPrefix }
                        }
                    })
                });
                env.Template(env.Stacks.Network).HasResourceProperties("AWS::EC2::VPC", new Dictionary<string, object>
                {
                    ["Tags"] = Match.ArrayWith(new object[]
                    {
                        new Dictionary<string, object> { ["Key"] = BudgetSettings.CostTagKey, ["Value"] = env.Stacks.Config.StackPrefix }
                    })
                });
            }

            foreach (string name in new string[] { "dev", "prod" })
            {
                SynthesizedEnvironment env = Environments.Get(name);
                Assert.False(env.Stacks.Config.SharedAccount);
                JsonElement budget = env.Resources(env.Stacks.Budget, "AWS::Budgets::Budget").Single();
                Assert.False(budget.GetProperty("Properties").GetProperty("Budget").TryGetProperty("CostFilters", out JsonElement _));
            }

            SynthesizedDeployment shared = Environments.GetShared("shared-external-prod");
            Assert.True(shared.Stacks.Shared!.Config.SharedAccount);
            shared.Template(shared.Stacks.Properties[0].Web).HasResourceProperties("AWS::CloudFront::Distribution", new Dictionary<string, object>
            {
                ["Tags"] = Match.ArrayWith(new object[]
                {
                    new Dictionary<string, object> { ["Key"] = BudgetSettings.CostTagKey, ["Value"] = shared.Stacks.Shared.Config.StackPrefix }
                })
            });
        }

        /// <summary>
        /// The account of the other environment is read from the account:&lt;env&gt; context value and deploy/infra.json.
        /// </summary>
        [Fact]
        public void SharedAccountIsDetectedFromContext()
        {
            App app = new App(new AppProps
            {
                Context = new Dictionary<string, object>
                {
                    [EnvironmentConfigFactory.EnvironmentContextKey] = "dev",
                    [EnvironmentConfigFactory.AccountMapContextPrefix + "dev"] = "555555555555",
                    [EnvironmentConfigFactory.AccountMapContextPrefix + "prod"] = "555555555555"
                }
            });
            Assert.True(EnvironmentConfigFactory.FromContext(app).SharedAccount);
            Assert.False(EnvironmentConfigFactory.Dev().SharedAccount);
        }

        private static void AssertNoCollisions(List<KeyValuePair<SynthesizedApp, Stack>> stacks, string accountStackName)
        {
            Dictionary<string, string> templates = new Dictionary<string, string>(StringComparer.Ordinal);
            List<KeyValuePair<SynthesizedApp, Stack>> distinct = new List<KeyValuePair<SynthesizedApp, Stack>>();
            foreach (KeyValuePair<SynthesizedApp, Stack> item in stacks)
            {
                string key = item.Value.Account + "|" + item.Value.Region + "|" + item.Value.StackName;
                string template = item.Key.Json(item.Value).GetRawText();
                if (templates.TryGetValue(key, out string? existing))
                {
                    Assert.True(item.Value.StackName == accountStackName, "Stack " + key + " is defined by both environments.");
                    Assert.True(existing == template, "Both environments define the account stack " + key + " with different templates.");
                    continue;
                }

                templates[key] = template;
                distinct.Add(item);
            }

            Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.Ordinal);
            int providers = 0;
            foreach (KeyValuePair<SynthesizedApp, Stack> item in distinct)
            {
                foreach (JsonProperty resource in item.Key.Json(item.Value).GetProperty("Resources").EnumerateObject())
                {
                    string type = resource.Value.GetProperty("Type").GetString() ?? String.Empty;
                    if (type == "AWS::IAM::OIDCProvider") providers++;
                    if (!resource.Value.TryGetProperty("Properties", out JsonElement properties)) continue;
                    string scope = _GlobalPrefixes.Any(p => type.StartsWith(p, StringComparison.Ordinal)) ? "global" : item.Value.Region;
                    List<string> physical = PhysicalNames(properties, String.Empty);
                    if (type == "AWS::ECS::TaskDefinition" && properties.TryGetProperty("Family", out JsonElement family)) physical.Add("Family=" + family.GetString());
                    foreach (string name in physical)
                    {
                        string key = type + "|" + scope + "|" + name;
                        string where = item.Value.StackName + "/" + resource.Name;
                        Assert.False(names.ContainsKey(key), "Physical name " + key + " is used by " + where + " and " + (names.ContainsKey(key) ? names[key] : String.Empty));
                        names[key] = where;
                    }
                }
            }

            Assert.Equal(1, providers);
            Assert.NotEmpty(names);
        }

        private static List<string> PhysicalNames(JsonElement properties, string path)
        {
            List<string> names = new List<string>();
            foreach (JsonProperty property in properties.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String && _NameKeys.Contains(property.Name))
                {
                    names.Add(path + property.Name + "=" + property.Value.GetString());
                }
                else if (property.Value.ValueKind == JsonValueKind.Array && _NameKeys.Contains(property.Name))
                {
                    foreach (JsonElement item in property.Value.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String) names.Add(path + property.Name + "=" + item.GetString());
                    }
                }
                else if (property.Value.ValueKind == JsonValueKind.Object && path.Length == 0 && property.Name != "Tags")
                {
                    names.AddRange(PhysicalNames(property.Value, property.Name + "."));
                }
            }

            return names;
        }
    }
}
