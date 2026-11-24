namespace TrellisKit.Infra.Tests
{
    using System.Collections.Generic;
    using System.Text.Json;
    using Amazon.CDK.Assertions;
    using Xunit;

    /// <summary>
    /// S3 assertions.
    /// </summary>
    public class StorageTests
    {
        /// <summary>
        /// Every bucket blocks all public access and is encrypted.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void EveryBucketBlocksPublicAccessAndIsEncrypted(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            List<JsonElement> buckets = env.Resources(env.Stacks.Storage, "AWS::S3::Bucket");
            Assert.Equal(8, buckets.Count);
            foreach (JsonElement bucket in buckets)
            {
                JsonElement properties = bucket.GetProperty("Properties");
                JsonElement block = properties.GetProperty("PublicAccessBlockConfiguration");
                Assert.True(block.GetProperty("BlockPublicAcls").GetBoolean());
                Assert.True(block.GetProperty("BlockPublicPolicy").GetBoolean());
                Assert.True(block.GetProperty("IgnorePublicAcls").GetBoolean());
                Assert.True(block.GetProperty("RestrictPublicBuckets").GetBoolean());
                Assert.True(properties.TryGetProperty("BucketEncryption", out JsonElement _));
                Assert.True(properties.TryGetProperty("LifecycleConfiguration", out JsonElement _));
            }
        }

        /// <summary>
        /// Every bucket policy denies requests without TLS.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void EveryBucketEnforcesTls(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            List<JsonElement> policies = env.Resources(env.Stacks.Storage, "AWS::S3::BucketPolicy");
            Assert.Equal(8, policies.Count);
            foreach (JsonElement policy in policies)
            {
                string text = policy.GetRawText();
                Assert.Contains("\"aws:SecureTransport\":\"false\"", text.Replace(" ", string.Empty));
            }
        }

        /// <summary>
        /// The website, images, transcoded, and updates buckets are readable only by CloudFront of this account (OAC).
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void CloudFrontReadsThroughOriginAccessControl(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            int grants = 0;
            foreach (JsonElement policy in env.Resources(env.Stacks.Storage, "AWS::S3::BucketPolicy"))
            {
                string text = policy.GetRawText();
                if (text.Contains("cloudfront.amazonaws.com"))
                {
                    Assert.Contains("AWS:SourceAccount", text);
                    Assert.Contains("arn:aws:cloudfront::" + env.Stacks.Config.Account + ":distribution/*", text);
                    grants++;
                }
            }

            Assert.Equal(4, grants);
            env.Template(env.Stacks.Web).ResourceCountIs("AWS::CloudFront::OriginAccessControl", 1);
            env.Template(env.Stacks.Media).ResourceCountIs("AWS::CloudFront::OriginAccessControl", 1);
        }

        /// <summary>
        /// The website build bucket is versioned so a bad sync can be rolled back.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void WebsiteBucketIsVersioned(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            env.Template(env.Stacks.Storage).HasResourceProperties("AWS::S3::Bucket", new Dictionary<string, object>
            {
                ["BucketName"] = env.Stacks.Config.BucketName("website"),
                ["VersioningConfiguration"] = new Dictionary<string, object> { ["Status"] = "Enabled" }
            });
        }

        /// <summary>
        /// Original uploads are never expired, and moderator-removed content expires after the retention period.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void OriginalsAreKeptAndRetainedContentExpires(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            foreach (JsonElement bucket in env.Resources(env.Stacks.Storage, "AWS::S3::Bucket"))
            {
                JsonElement properties = bucket.GetProperty("Properties");
                string bucketName = properties.GetProperty("BucketName").GetString() ?? string.Empty;
                string rules = properties.GetProperty("LifecycleConfiguration").GetRawText();
                if (bucketName == env.Stacks.Config.BucketName("media-source") || bucketName == env.Stacks.Config.BucketName("media-video-source"))
                {
                    Assert.DoesNotContain("ExpirationInDays", rules);
                    Assert.Contains("GLACIER_IR", rules);
                }
                else if (bucketName == env.Stacks.Config.BucketName("media-retained"))
                {
                    Assert.Contains("\"ExpirationInDays\":" + env.Stacks.Config.Storage.RetainedExpirationDays, rules.Replace(" ", string.Empty));
                }
            }
        }
    }
}
