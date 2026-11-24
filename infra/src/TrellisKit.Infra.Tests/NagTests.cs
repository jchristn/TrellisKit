namespace TrellisKit.Infra.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using Amazon.CDK;
    using Amazon.CDK.AWS.IAM;
    using Amazon.CDK.AWS.S3;
    using TrellisKit.Infra.Nag;
    using Xunit;

    /// <summary>
    /// The cdk-nag 3 adapter: findings are detected, rule-wide, exact, and pattern suppressions apply, synthesis fails
    /// on an unsuppressed error, and suppressions reach the template metadata.
    /// </summary>
    public class NagTests
    {
        /// <summary>
        /// An unsuppressed finding is reported, so the clean-app assertions are not vacuous.
        /// </summary>
        [Fact]
        public void UnsuppressedFindingIsReported()
        {
            App app = new App();
            Stack stack = new Stack(app, "NagProbe");
            new Bucket(stack, "Plain");

            List<string> findings = new AwsSolutionsNagPlugin(app).FindUnsuppressed(app);
            Assert.Contains(findings, f => f.StartsWith("AwsSolutions-S1 at NagProbe/Plain", StringComparison.Ordinal));
        }

        /// <summary>
        /// A rule-wide suppression on a parent covers the rule on its children.
        /// </summary>
        [Fact]
        public void RuleWideSuppressionCoversChildren()
        {
            App app = new App();
            Stack stack = new Stack(app, "NagProbe");
            new Bucket(stack, "Plain");
            NagHelper.Suppress(stack, "AwsSolutions-S1", "Probe bucket used only by this test.");

            List<string> findings = new AwsSolutionsNagPlugin(app).FindUnsuppressed(app);
            Assert.DoesNotContain(findings, f => f.StartsWith("AwsSolutions-S1 ", StringComparison.Ordinal));
        }

        /// <summary>
        /// Wildcard patterns suppress only the IAM5 findings they match; an exact qualifier suppresses its finding.
        /// </summary>
        [Fact]
        public void WildcardPatternsSuppressOnlyMatchingFindings()
        {
            App app = new App();
            Stack stack = new Stack(app, "NagProbe");
            Role role = new Role(stack, "ProbeRole", new RoleProps { AssumedBy = new ServicePrincipal("ecs-tasks.amazonaws.com") });
            role.AddToPolicy(new PolicyStatement(new PolicyStatementProps { Actions = new string[] { "xray:PutTraceSegments" }, Resources = new string[] { "*" } }));
            role.AddToPolicy(new PolicyStatement(new PolicyStatementProps { Actions = new string[] { "s3:Get*" }, Resources = new string[] { "arn:aws:s3:::probe/data" } }));

            List<string> before = new AwsSolutionsNagPlugin(app).FindUnsuppressed(app);
            Assert.Contains(before, f => f.StartsWith("AwsSolutions-IAM5[Resource::*]", StringComparison.Ordinal));
            Assert.Contains(before, f => f.StartsWith("AwsSolutions-IAM5[Action::s3:Get*]", StringComparison.Ordinal));

            NagHelper.SuppressWildcards(role, "X-Ray has no resource-level permissions.", NagHelper.AnyResourcePattern);
            List<string> partial = new AwsSolutionsNagPlugin(app).FindUnsuppressed(app);
            Assert.DoesNotContain(partial, f => f.StartsWith("AwsSolutions-IAM5[Resource::*]", StringComparison.Ordinal));
            Assert.Contains(partial, f => f.StartsWith("AwsSolutions-IAM5[Action::s3:Get*]", StringComparison.Ordinal));

            NagHelper.Suppress(role, "AwsSolutions-IAM5", "Probe role reads one object only.", "Action::s3:Get*");
            List<string> after = new AwsSolutionsNagPlugin(app).FindUnsuppressed(app);
            Assert.DoesNotContain(after, f => f.StartsWith("AwsSolutions-IAM5", StringComparison.Ordinal));
        }

        /// <summary>
        /// Synthesis fails while an error-level finding is unsuppressed, and succeeds once it is suppressed.
        /// </summary>
        [Fact]
        public void SynthesisFailsOnUnsuppressedError()
        {
            App failing = new App();
            Validations.Of(failing).AddPlugins(new AwsSolutionsNagPlugin(failing));
            Stack bad = new Stack(failing, "NagProbe");
            new Bucket(bad, "Plain", new BucketProps { EnforceSSL = true });
            Exception error = Assert.ThrowsAny<Exception>(() => failing.Synth());
            Assert.Contains("Validation failed", error.Message, StringComparison.Ordinal);

            App passing = new App();
            Validations.Of(passing).AddPlugins(new AwsSolutionsNagPlugin(passing));
            Stack good = new Stack(passing, "NagProbe");
            new Bucket(good, "Plain", new BucketProps { EnforceSSL = true });
            NagHelper.Suppress(good, "AwsSolutions-S1", "Probe bucket used only by this test.");
            passing.Synth();
        }

        /// <summary>
        /// The task role policy carries its suppressions, wildcard patterns included, in the cdk-nag 2 metadata format.
        /// </summary>
        [Fact]
        public void SuppressionsAreWrittenToTemplateMetadata()
        {
            SynthesizedEnvironment env = Environments.Get("dev");
            JsonElement resources = env.Json(env.Stacks.Api).GetProperty("Resources");
            JsonProperty policy = resources.EnumerateObject().First(r => r.Name.StartsWith("TaskDefinitionTaskRoleDefaultPolicy", StringComparison.Ordinal));
            JsonElement rules = policy.Value.GetProperty("Metadata").GetProperty("cdk_nag").GetProperty("rules_to_suppress");

            List<string> ids = rules.EnumerateArray().Select(r => r.GetProperty("id").GetString() ?? String.Empty).ToList();
            Assert.Contains("AwsSolutions-ECS2", ids);
            JsonElement iam5 = rules.EnumerateArray().First(r => r.GetProperty("id").GetString() == "AwsSolutions-IAM5");
            Assert.False(String.IsNullOrWhiteSpace(iam5.GetProperty("reason").GetString()));
            Assert.Contains(iam5.GetProperty("applies_to").EnumerateArray(), a => a.GetProperty("regex").GetString() == NagHelper.AnyResourcePattern);
        }
    }
}
