namespace TrellisKit.Infra.Tests
{
    using System.Collections.Generic;
    using System.Text.Json;
    using Amazon.CDK.Assertions;
    using Xunit;

    /// <summary>
    /// VPC and RDS assertions.
    /// </summary>
    public class NetworkAndDataTests
    {
        /// <summary>
        /// NAT gateways follow the environment (one in dev, two in prod) across two AZs.
        /// </summary>
        /// <param name="name">Environment.</param>
        /// <param name="natGateways">Expected NAT gateways.</param>
        [Theory]
        [InlineData("dev", 1)]
        [InlineData("prod", 2)]
        public void NatGatewaysFollowTheEnvironment(string name, int natGateways)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            Template template = env.Template(env.Stacks.Network);
            template.ResourceCountIs("AWS::EC2::NatGateway", natGateways);
            template.ResourceCountIs("AWS::EC2::Subnet", 6);
            template.ResourceCountIs("AWS::EC2::FlowLog", 1);
        }

        /// <summary>
        /// Multi-AZ and deletion protection are enabled only in production.
        /// </summary>
        /// <param name="name">Environment.</param>
        /// <param name="production">Whether the environment is production.</param>
        [Theory]
        [InlineData("dev", false)]
        [InlineData("prod", true)]
        public void MultiAzAndDeletionProtectionOnlyInProd(string name, bool production)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            env.Template(env.Stacks.Data).HasResourceProperties("AWS::RDS::DBInstance", new Dictionary<string, object>
            {
                ["MultiAZ"] = production,
                ["DeletionProtection"] = production
            });
        }

        /// <summary>
        /// The database is PostgreSQL 17, encrypted, private, backed up, and uses generated credentials.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void DatabaseIsEncryptedPrivateAndBackedUp(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            Template template = env.Template(env.Stacks.Data);
            template.HasResourceProperties("AWS::RDS::DBInstance", new Dictionary<string, object>
            {
                ["Engine"] = "postgres",
                ["EngineVersion"] = "17",
                ["StorageEncrypted"] = true,
                ["PubliclyAccessible"] = false,
                ["BackupRetentionPeriod"] = env.Stacks.Config.Database.BackupRetentionDays,
                ["EnableCloudwatchLogsExports"] = new string[] { "postgresql", "upgrade" }
            });
            template.HasResource("AWS::RDS::DBInstance", new Dictionary<string, object>
            {
                ["DeletionPolicy"] = "Snapshot"
            });
            template.HasResourceProperties("AWS::RDS::DBParameterGroup", new Dictionary<string, object>
            {
                ["Family"] = "postgres17",
                ["Parameters"] = Match.ObjectLike(new Dictionary<string, object> { ["rds.force_ssl"] = "1" })
            });
            template.HasResourceProperties("AWS::SecretsManager::Secret", new Dictionary<string, object>
            {
                ["Name"] = env.Stacks.Config.ResourcePath("database"),
                ["GenerateSecretString"] = Match.ObjectLike(new Dictionary<string, object> { ["GenerateStringKey"] = "password" })
            });
        }

        /// <summary>
        /// Only the API task security group can reach the database port.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void DatabaseIsReachableOnlyFromTasks(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            int fromSecurityGroup = 0;
            foreach (JsonElement ingress in env.Resources(env.Stacks.Network, "AWS::EC2::SecurityGroupIngress"))
            {
                JsonElement properties = ingress.GetProperty("Properties");
                if (properties.GetProperty("FromPort").GetInt32() == env.Stacks.Config.Database.Port)
                {
                    Assert.True(properties.TryGetProperty("SourceSecurityGroupId", out JsonElement _));
                    Assert.False(properties.TryGetProperty("CidrIp", out JsonElement _));
                    fromSecurityGroup++;
                }
            }

            Assert.Equal(1, fromSecurityGroup);
        }
    }
}
