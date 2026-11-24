namespace TrellisKit.Infra.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Amazon.CDK;
    using TrellisKit.Infra.Config;
    using Xunit;

    /// <summary>
    /// Typed configuration and context handling.
    /// </summary>
    public class ConfigTests
    {
        /// <summary>
        /// Production is larger and more protected than development.
        /// </summary>
        [Fact]
        public void ProdIsSizedAboveDev()
        {
            EnvironmentConfig dev = EnvironmentConfigFactory.Dev();
            EnvironmentConfig prod = EnvironmentConfigFactory.Prod();
            Assert.False(dev.Database.MultiAz);
            Assert.True(prod.Database.MultiAz);
            Assert.True(prod.Api.MinTasks >= 2);
            Assert.True(prod.Network.NatGateways > dev.Network.NatGateways);
            Assert.Equal(2, prod.Network.AvailabilityZones.Count);
            Assert.Equal(RemovalPolicy.RETAIN, prod.StatefulRemovalPolicy);
        }

        /// <summary>
        /// The pool size times the maximum task count stays below a conservative max_connections budget.
        /// </summary>
        [Fact]
        public void ConnectionPoolsFitTheDatabase()
        {
            Assert.True(EnvironmentConfigFactory.Dev().Database.MaxPoolSize * EnvironmentConfigFactory.Dev().Api.MaxTasks <= 100);
            Assert.True(EnvironmentConfigFactory.Prod().Database.MaxPoolSize * EnvironmentConfigFactory.Prod().Api.MaxTasks <= 200);
        }

        /// <summary>
        /// Out-of-range values are rejected.
        /// </summary>
        [Fact]
        public void RangesAreValidated()
        {
            EnvironmentConfig config = EnvironmentConfigFactory.Dev();
            Assert.Throws<ArgumentOutOfRangeException>(() => config.Network.NatGateways = 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => config.Api.AccountExportRetentionHours = 169);
            Assert.Null(config.Api.PushEnabled);
            Assert.Null(config.Api.AccountExportRetentionHours);
            config.Api.AccountExportRetentionHours = null;
            Assert.Throws<ArgumentOutOfRangeException>(() => config.Media.HlsRenditions = new List<HlsRendition>());
            Assert.Throws<ArgumentOutOfRangeException>(() => new HlsRendition(100, 1000000));
            Assert.Throws<ArgumentOutOfRangeException>(() => config.Media.PosterWidth = 100);
            Assert.Throws<ArgumentOutOfRangeException>(() => config.Api.IdleTimeoutSeconds = 5000);
            Assert.Throws<ArgumentOutOfRangeException>(() => config.Database.BackupRetentionDays = 36);
            Assert.Throws<ArgumentException>(() => config.Account = "12345");
            Assert.Throws<ArgumentOutOfRangeException>(() => config.Email.DmarcPolicy = "allow");
        }

        /// <summary>
        /// The env context value is required and must be known.
        /// </summary>
        [Fact]
        public void EnvironmentContextIsRequired()
        {
            App missing = new App();
            Assert.Throws<ArgumentException>(() => EnvironmentConfigFactory.FromContext(missing));

            App unknown = new App(new AppProps { Context = new Dictionary<string, object> { ["env"] = "staging" } });
            Assert.Throws<ArgumentException>(() => EnvironmentConfigFactory.FromContext(unknown));
        }

        /// <summary>
        /// Context overrides the account, image tag, and alarm email.
        /// </summary>
        [Fact]
        public void ContextOverridesApply()
        {
            App app = new App(new AppProps
            {
                Context = new Dictionary<string, object>
                {
                    ["env"] = "prod",
                    ["account"] = "333333333333",
                    ["imageTag"] = "abc1234",
                    ["alarmEmail"] = "ops@example.com"
                }
            });

            EnvironmentConfig config = EnvironmentConfigFactory.FromContext(app);
            Assert.Equal("333333333333", config.Account);
            Assert.Equal("abc1234", config.Api.ImageTag);
            Assert.Equal("ops@example.com", config.Observability.AlarmEmail);
            Assert.Equal(new List<string> { "ops@example.com" }, config.Budget.NotificationEmails);
        }

        /// <summary>
        /// The default property is the engine's neutral sample, and names derive from its id.
        /// </summary>
        [Fact]
        public void DefaultPropertyIsTheSample()
        {
            EnvironmentConfig prod = EnvironmentConfigFactory.Prod();
            Assert.EndsWith(Path.Combine("samples", "example"), prod.Property.Directory);
            Assert.Equal(prod.Property.Slug + "-prod", prod.ResourcePrefix);
            Assert.Equal(prod.Property.Slug + "-prod-Api", prod.StackName("Api"));
            Assert.Equal(prod.Property.Slug + "/prod/server", prod.ResourcePath("server"));
            Assert.Equal(prod.Property.ApiDomain, prod.Domains.ApiHost);
            Assert.Equal(prod.Property.MediaDomain, prod.Domains.MediaHost);
            Assert.Equal(prod.Property.SupportEmail, prod.Observability.AlarmEmail);
        }

        /// <summary>
        /// A missing or invalid property is rejected with a message naming the problem.
        /// </summary>
        [Fact]
        public void InvalidPropertiesAreRejected()
        {
            string directory = Path.Combine(Path.GetTempPath(), "trelliskit-infra-bad-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            Assert.Throws<ArgumentException>(() => PropertyDefinition.Load(directory));

            File.WriteAllText(Path.Combine(directory, "property.json"),
                "{\"id\":\"Bad Id\",\"name\":\"X\",\"domains\":{\"web\":\"x.test\",\"api\":\"api.x.test\",\"media\":\"media.x.test\"},\"contact\":{\"supportEmail\":\"s@x.test\",\"emailFromAddress\":\"n@x.test\"}}");
            ArgumentException badId = Assert.Throws<ArgumentException>(() => PropertyDefinition.Load(directory));
            Assert.Contains("id", badId.Message);

            File.WriteAllText(Path.Combine(directory, "property.json"),
                "{\"id\":\"app_abcdef12_Abcdefgh1234\",\"name\":\"X\",\"domains\":{\"web\":\"x.test\",\"api\":\"api.other.test\",\"media\":\"media.x.test\"},\"contact\":{\"supportEmail\":\"s@x.test\",\"emailFromAddress\":\"n@x.test\"}}");
            Assert.Throws<ArgumentException>(() => PropertyDefinition.Load(directory));
        }
    }
}
