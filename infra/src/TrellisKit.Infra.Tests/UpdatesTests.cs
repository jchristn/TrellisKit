namespace TrellisKit.Infra.Tests
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using Amazon.CDK;
    using TrellisKit.Infra.Config;
    using TrellisKit.Infra.Stacks;
    using Xunit;

    /// <summary>
    /// Live updates: the updates bucket, its CloudFront paths and caching rules, the manifest behaviors of the website
    /// distribution, the signing key secrets, and the server's access and environment. Also covers the media storage variables the backend reads.
    /// </summary>
    public class UpdatesTests
    {
        private static readonly string _CachingDisabledPolicyId = "4135ea2d-6df8-44a3-9df3-4b5a84be39ad";
        private static readonly string _AllViewerExceptHostHeaderPolicyId = "b689b0a8-53d0-40ab-baf2-68738e2966ac";

        /// <summary>
        /// Hashed assets are cached for a year as immutable, everything else under the updates path is never cached,
        /// the assets path is evaluated first, and neither requires signed cookies.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void UpdatesPathCachesHashedAssetsImmutablyAndNeverCachesManifests(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            AssertUpdatesBehaviors(env, env.Stacks.Media, env.Stacks.Config);
        }

        /// <summary>
        /// Each property's media distribution in the shared topology carries the same updates rules.
        /// </summary>
        /// <param name="name">Shared deployment.</param>
        [Theory]
        [InlineData("shared-dev")]
        [InlineData("shared-prod")]
        public void SharedPropertyMediaDistributionsServeUpdates(string name)
        {
            SynthesizedDeployment deployment = Environments.GetShared(name);
            foreach (PropertyStacks property in deployment.Stacks.Properties)
            {
                AssertUpdatesBehaviors(deployment, property.Media, property.Config);
            }
        }

        /// <summary>
        /// The server receives the updates bucket and public URL, reads the signing key only from Secrets Manager,
        /// and can write and delete update objects.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void ServerReceivesUpdatesBucketAndSigningKey(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            Dictionary<string, string> environment = TemplateReader.ServerEnvironment(env, env.Stacks.Api);
            Assert.Equal("S3", environment[ServerEnvironmentKeys.UpdatesStorageMode]);
            Assert.Equal(env.Stacks.Config.Region, environment[ServerEnvironmentKeys.UpdatesS3Region]);
            Assert.Contains("UpdatesBucket", environment[ServerEnvironmentKeys.UpdatesBucket]);
            Assert.Equal("https://" + env.Stacks.Config.Domains.MediaHost + "/updates", environment[ServerEnvironmentKeys.UpdatesPublicBaseUrl]);
            Assert.DoesNotContain(ServerEnvironmentKeys.UpdatesSigningKey, environment.Keys);
            Assert.Contains(ServerEnvironmentKeys.UpdatesSigningKey, TemplateReader.ServerSecrets(env, env.Stacks.Api));

            env.Template(env.Stacks.Api).HasResourceProperties("AWS::SecretsManager::Secret", new Dictionary<string, object>
            {
                ["Name"] = env.Stacks.Config.ResourcePath("updates-signing-key")
            });
            string policies = string.Join("\n", env.Resources(env.Stacks.Api, "AWS::IAM::Policy").Select(p => p.GetRawText()));
            Assert.Contains("\"UpdatesObjects\"", policies);
            Assert.Contains("\"UpdatesBucketList\"", policies);
        }

        /// <summary>
        /// The media variables use the backend settings registry names, and the server runs media in S3 mode with MediaConvert.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void MediaVariablesMatchTheBackendSettings(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            Dictionary<string, string> environment = TemplateReader.ServerEnvironment(env, env.Stacks.Api);
            Assert.Equal("S3", environment[ServerEnvironmentKeys.MediaStorageMode]);
            Assert.Equal("MediaConvert", environment[ServerEnvironmentKeys.MediaVideoProcessor]);
            Assert.Equal(env.Stacks.Config.Region, environment[ServerEnvironmentKeys.MediaS3Region]);
            Assert.Equal("https://" + env.Stacks.Config.Domains.MediaHost, environment[ServerEnvironmentKeys.MediaPublicBaseUrl]);
            Assert.Equal(env.Stacks.Config.Domains.PrimaryDomain, environment[ServerEnvironmentKeys.MediaCookieDomain]);
            Assert.Contains("JobEventsTopic", environment[ServerEnvironmentKeys.MediaSnsTopicArn]);
            Assert.Equal("SingleProperty", environment[ServerEnvironmentKeys.DeploymentMode]);
            Assert.DoesNotContain("TK_MEDIA_CDN_BASE_URL", environment.Keys);

            Dictionary<string, string> buckets = new Dictionary<string, string>
            {
                [ServerEnvironmentKeys.MediaSourceBucket] = "MediaSourceBucket",
                [ServerEnvironmentKeys.MediaVideoSourceBucket] = "MediaVideoSourceBucket",
                [ServerEnvironmentKeys.MediaImagesBucket] = "MediaImagesBucket",
                [ServerEnvironmentKeys.MediaTranscodedBucket] = "MediaTranscodedBucket",
                [ServerEnvironmentKeys.MediaRetainedBucket] = "MediaRetainedBucket"
            };
            foreach (KeyValuePair<string, string> bucket in buckets)
            {
                Assert.Contains(bucket.Value, environment[bucket.Key]);
            }
        }

        /// <summary>
        /// The website distribution forwards both manifest paths to the API uncached, ahead of the API patterns, with the
        /// managed policy that forwards every viewer header except Host plus the CloudFront viewer headers.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void ManifestPathsAreUncachedAndForwardEveryClientHeader(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            WebSettings web = env.Stacks.Config.Web;
            Assert.Equal(new string[] { "/api/v1/updates/manifest", "/api/v1/app/manifest" }, web.ManifestPathPatterns.ToArray());

            JsonElement distribution = TemplateReader.DistributionConfig(env, env.Stacks.Web);
            List<string> patterns = distribution.GetProperty("CacheBehaviors").EnumerateArray().Select(b => b.GetProperty("PathPattern").GetString() ?? string.Empty).ToList();
            JsonElement api = TemplateReader.Behavior(distribution, "/api/*");
            foreach (string pattern in web.ManifestPathPatterns)
            {
                Assert.True(patterns.IndexOf(pattern) >= 0);
                Assert.True(patterns.IndexOf(pattern) < patterns.IndexOf("/api/*"));
                JsonElement behavior = TemplateReader.Behavior(distribution, pattern);
                Assert.Equal(_CachingDisabledPolicyId, behavior.GetProperty("CachePolicyId").GetString());
                Assert.Equal(_AllViewerExceptHostHeaderPolicyId, behavior.GetProperty("OriginRequestPolicyId").GetString());
                Assert.Equal(api.GetProperty("TargetOriginId").GetString(), behavior.GetProperty("TargetOriginId").GetString());
                Assert.Equal("https-only", behavior.GetProperty("ViewerProtocolPolicy").GetString());
                Assert.Contains("ApiForwardingFunction", behavior.GetProperty("FunctionAssociations").GetRawText());
            }
        }

        /// <summary>
        /// CloudFront may read only the served updates/ prefix of the updates bucket, so source maps under
        /// private/sourcemaps/ are never served, and no media behavior points at that prefix.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void SourceMapsAreNeverReadableByCloudFront(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            JsonElement policy = env.Resources(env.Stacks.Storage, "AWS::S3::BucketPolicy")
                .Single(p => p.GetProperty("Properties").GetProperty("Bucket").GetRawText().Contains("UpdatesBucket"));
            List<JsonElement> cloudFront = policy.GetProperty("Properties").GetProperty("PolicyDocument").GetProperty("Statement").EnumerateArray()
                .Where(s => s.GetProperty("Principal").GetRawText().Contains("cloudfront.amazonaws.com"))
                .ToList();
            JsonElement statement = Assert.Single(cloudFront);
            string resource = statement.GetProperty("Resource").GetRawText();
            Assert.Contains("/updates/*", resource);
            Assert.DoesNotContain("\"/*\"", resource);

            JsonElement distribution = TemplateReader.DistributionConfig(env, env.Stacks.Media);
            foreach (JsonElement behavior in distribution.GetProperty("CacheBehaviors").EnumerateArray())
            {
                Assert.DoesNotContain("private", behavior.GetProperty("PathPattern").GetString() ?? string.Empty);
            }
        }

        /// <summary>
        /// The standby signing key is a separate placeholder secret (an empty JSON key, filled out of band) injected as
        /// TK_UPDATES_STANDBY_SIGNING_KEY; neither key is a plain variable.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void StandbySigningKeyIsAnEmptyPlaceholderSecret(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            env.Template(env.Stacks.Api).HasResourceProperties("AWS::SecretsManager::Secret", new Dictionary<string, object>
            {
                ["Name"] = env.Stacks.Config.ResourcePath("updates-standby-signing-key"),
                ["SecretString"] = "{\"" + ServerEnvironmentKeys.UpdatesStandbySigningKey + "\":\"\"}"
            });
            Dictionary<string, string> environment = TemplateReader.ServerEnvironment(env, env.Stacks.Api);
            List<string> secrets = TemplateReader.ServerSecrets(env, env.Stacks.Api);
            Assert.Contains(ServerEnvironmentKeys.UpdatesStandbySigningKey, secrets);
            Assert.Contains(ServerEnvironmentKeys.UpdatesSigningKey, secrets);
            Assert.DoesNotContain(ServerEnvironmentKeys.UpdatesStandbySigningKey, environment.Keys);
            Assert.DoesNotContain("TK_UPDATES_SIGNING_KEY_FILE", environment.Keys);
            Assert.DoesNotContain("TK_UPDATES_STANDBY_SIGNING_KEY_FILE", environment.Keys);
            JsonElement server = TemplateReader.ApiContainer(env, env.Stacks.Api, ApiStack.ServerContainerName);
            string valueFrom = server.GetProperty("Secrets").EnumerateArray()
                .Single(e => e.GetProperty("Name").GetString() == ServerEnvironmentKeys.UpdatesStandbySigningKey)
                .GetProperty("ValueFrom").GetRawText();
            Assert.Contains("UpdatesStandbySigningKey", valueFrom);
            Assert.Contains(":" + ServerEnvironmentKeys.UpdatesStandbySigningKey + "::", valueFrom);
        }

        private static void AssertUpdatesBehaviors(SynthesizedApp app, Stack? media, EnvironmentConfig config)
        {
            UpdatesSettings updates = config.Updates;
            JsonElement distribution = TemplateReader.DistributionConfig(app, media);
            List<string> patterns = distribution.GetProperty("CacheBehaviors").EnumerateArray().Select(b => b.GetProperty("PathPattern").GetString() ?? string.Empty).ToList();
            Assert.True(patterns.IndexOf(updates.AssetsPathPattern) >= 0);
            Assert.True(patterns.IndexOf(updates.AssetsPathPattern) < patterns.IndexOf(updates.PathPattern));

            JsonElement assets = TemplateReader.Behavior(distribution, updates.AssetsPathPattern);
            JsonElement rest = TemplateReader.Behavior(distribution, updates.PathPattern);
            foreach (JsonElement behavior in new JsonElement[] { assets, rest })
            {
                Assert.False(behavior.TryGetProperty("TrustedKeyGroups", out JsonElement _));
                Assert.Equal("https-only", behavior.GetProperty("ViewerProtocolPolicy").GetString());
                Assert.Equal(new string[] { "GET", "HEAD" }, behavior.GetProperty("AllowedMethods").EnumerateArray().Select(m => m.GetString()).ToArray());
            }

            // Both paths read the updates bucket, which is neither the images (default) nor the transcoded origin.
            string originId = assets.GetProperty("TargetOriginId").GetString() ?? string.Empty;
            Assert.Equal(originId, rest.GetProperty("TargetOriginId").GetString());
            Assert.NotEqual(originId, distribution.GetProperty("DefaultCacheBehavior").GetProperty("TargetOriginId").GetString());
            JsonElement origin = distribution.GetProperty("Origins").EnumerateArray().Single(o => o.GetProperty("Id").GetString() == originId);
            Assert.Contains("UpdatesBucket", origin.GetProperty("DomainName").GetRawText());

            string immutable = updates.ImmutableMaxAgeSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            JsonElement cachePolicy = app.Resource(media, assets.GetProperty("CachePolicyId").GetProperty("Ref").GetString() ?? string.Empty);
            JsonElement cacheConfig = cachePolicy.GetProperty("Properties").GetProperty("CachePolicyConfig");
            Assert.Equal(updates.ImmutableMaxAgeSeconds, cacheConfig.GetProperty("DefaultTTL").GetInt32());
            Assert.Equal(updates.ImmutableMaxAgeSeconds, cacheConfig.GetProperty("MinTTL").GetInt32());
            Assert.Equal(updates.ImmutableMaxAgeSeconds, cacheConfig.GetProperty("MaxTTL").GetInt32());
            string assetsHeaders = app.Resource(media, assets.GetProperty("ResponseHeadersPolicyId").GetProperty("Ref").GetString() ?? string.Empty).GetRawText();
            Assert.Contains("public, max-age=" + immutable + ", immutable", assetsHeaders);

            Assert.Equal(_CachingDisabledPolicyId, rest.GetProperty("CachePolicyId").GetString());
            string restHeaders = app.Resource(media, rest.GetProperty("ResponseHeadersPolicyId").GetProperty("Ref").GetString() ?? string.Empty).GetRawText();
            Assert.Contains("no-cache, no-store, must-revalidate", restHeaders);
        }
    }
}
