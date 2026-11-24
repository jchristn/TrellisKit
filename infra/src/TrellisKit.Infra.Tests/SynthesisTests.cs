namespace TrellisKit.Infra.Tests
{
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using Amazon.CDK;
    using Amazon.CDK.Assertions;
    using Xunit;
    using Annotations = Amazon.CDK.Assertions.Annotations;

    /// <summary>
    /// Both environments synthesize without errors, without unsuppressed cdk-nag findings, and without lookups.
    /// </summary>
    public class SynthesisTests
    {
        /// <summary>
        /// Every stack is created, in the expected regions.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void AllStacksAreCreatedInTheExpectedRegions(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            Assert.Equal(15, env.Stacks.All.Count);
            Assert.Null(env.Stacks.Redirect);
            Assert.Equal("TrellisKit-GitHubOidc", env.Stacks.GitHubOidc!.StackName);
            foreach (Stack stack in env.Stacks.All.Where(s => s != env.Stacks.GitHubOidc))
            {
                Assert.StartsWith(env.Stacks.Config.Property.Slug + "-" + name + "-", stack.StackName);
            }
            Assert.Equal("us-east-1", env.Stacks.EdgeCertificates!.Region);
            Assert.Equal("us-east-1", env.Stacks.Budget!.Region);
            foreach (Stack stack in env.Stacks.All.Where(s => s != env.Stacks.EdgeCertificates && s != env.Stacks.Budget))
            {
                Assert.Equal("us-west-2", stack.Region);
            }
        }

        /// <summary>
        /// No stack carries an error annotation (cdk-nag findings are errors unless suppressed).
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        [InlineData("dev-redirect")]
        [InlineData("prod-redirect")]
        public void NoStackHasErrors(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            foreach (Stack stack in env.Stacks.All)
            {
                Annotations.FromStack(stack).HasNoError("*", Match.AnyValue());
            }
        }

        /// <summary>
        /// No stack carries an unsuppressed cdk-nag warning.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        [InlineData("dev-redirect")]
        [InlineData("prod-redirect")]
        public void NoStackHasNagWarnings(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            Assert.Empty(env.NagFindings());
        }

        /// <summary>
        /// Synthesis records no missing context, which would mean a live AWS lookup.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void SynthesisNeedsNoLookups(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            env.App.Synth();
            using (JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(env.App.Outdir, "manifest.json"))))
            {
                Assert.False(manifest.RootElement.TryGetProperty("missing", out JsonElement missing), "Missing context: " + missing.ToString());
            }
        }

        /// <summary>
        /// The two environments never share physical names.
        /// </summary>
        [Fact]
        public void EnvironmentsUseDistinctNames()
        {
            Assert.NotEqual(Environments.Dev.Stacks.Config.ResourcePrefix, Environments.Prod.Stacks.Config.ResourcePrefix);
            Assert.NotEqual(Environments.Dev.Stacks.Config.Domains.PrimaryDomain, Environments.Prod.Stacks.Config.Domains.PrimaryDomain);
            Assert.Equal(Environments.Prod.Stacks.Config.Property.WebDomain, Environments.Prod.Stacks.Config.Domains.PrimaryDomain);
            Assert.Equal("dev." + Environments.Dev.Stacks.Config.Property.WebDomain, Environments.Dev.Stacks.Config.Domains.PrimaryDomain);
        }

        /// <summary>
        /// A property with a vanity domain gets the redirect stack, and its deploy/infra.json supplies the account
        /// and GitHub repository.
        /// </summary>
        [Fact]
        public void RedirectPropertyAddsTheRedirectStackAndDeploySettings()
        {
            SynthesizedEnvironment env = Environments.Get("prod-redirect");
            // Redirect added; Email absent because the fixture's mail.provider is Mailgun.
            Assert.Equal(15, env.Stacks.All.Count);
            Assert.NotNull(env.Stacks.Redirect);
            Assert.Null(env.Stacks.Email);
            Assert.Equal(Environments.FixtureProdAccount, env.Stacks.Config.Account);
            Assert.Equal(Environments.FixtureGitHubOwner, env.Stacks.Config.GitHub.Owner);
            Assert.Equal(Environments.FixtureGitHubRepository, env.Stacks.Config.GitHub.Repository);
            Assert.Equal("alarms@example.test", env.Stacks.Config.Observability.AlarmEmail);
            Assert.Equal("111111111111", Environments.Get("dev-redirect").Stacks.Config.Account);
        }
    }
}
