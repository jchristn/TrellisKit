namespace TrellisKit.Infra.Tests
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using Amazon.CDK.Assertions;
    using TrellisKit.Infra.Stacks;
    using Xunit;

    /// <summary>
    /// Phase 2 backend wiring: media, MediaConvert, push, and feature variables on the server task, their secrets,
    /// the property-scoped media path patterns, the job template layout the backend relies on, and the task role's
    /// MediaConvert permissions.
    /// </summary>
    public class MediaWiringTests
    {
        /// <summary>
        /// Every media, MediaConvert, push, and feature variable is set from the stack's own resources.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void ServerReceivesMediaPushAndFeatureVariables(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            Dictionary<string, string> environment = TemplateReader.ServerEnvironment(env, env.Stacks.Api);
            string mediaStack = env.Stacks.Media!.StackName;

            Assert.Equal("S3", environment["TK_MEDIA_STORAGE_MODE"]);
            Assert.Equal(env.Stacks.Config.Region, environment["TK_MEDIA_S3_REGION"]);
            Assert.Equal("MediaConvert", environment["TK_MEDIA_VIDEO_PROCESSOR"]);
            Assert.DoesNotContain("TK_FFMPEG_PATH", environment.Keys);
            Assert.Equal("https://" + env.Stacks.Config.Domains.MediaHost, environment["TK_MEDIA_PUBLIC_BASE_URL"]);
            Assert.Equal(env.Stacks.Config.Domains.PrimaryDomain, environment["TK_MEDIA_COOKIE_DOMAIN"]);
            foreach (string bucket in new string[] { "SOURCE", "VIDEO_SOURCE", "IMAGES", "TRANSCODED", "RETAINED" })
            {
                Assert.Contains(env.Stacks.Storage!.StackName + ":ExportsOutputRefMedia" + Pascal(bucket) + "Bucket", environment["TK_MEDIA_" + bucket + "_BUCKET"]);
            }

            Assert.Equal(env.Stacks.Config.ResourceName("hls"), environment["TK_MEDIACONVERT_JOB_TEMPLATE"]);
            Assert.Contains(mediaStack + ":ExportsOutputFnGetAttTranscodeQueueArn", environment["TK_MEDIACONVERT_QUEUE_ARN"]);
            Assert.Contains(mediaStack + ":ExportsOutputFnGetAttMediaConvertRole", environment["TK_MEDIACONVERT_ROLE_ARN"]);
            Assert.Contains(mediaStack + ":ExportsOutputRefJobEventsTopic", environment["TK_MEDIA_SNS_TOPIC_ARN"]);
            Assert.Contains(mediaStack + ":ExportsOutputRefSigningPublicKey", environment["TK_MEDIA_SIGNING_KEY_PAIR_ID"]);

            // Live settings stay with the backend default and the admin console unless configured.
            foreach (string key in new string[] { "TK_PUSH_ENABLED", "TK_LINK_PREVIEWS_ENABLED", "TK_EMAIL_FEED_POSTS_ENABLED", "TK_ACCOUNT_EXPORT_RETENTION_HOURS" })
            {
                Assert.Contains(key, ServerEnvironmentKeys.OptionalLiveKeys);
                Assert.DoesNotContain(key, environment.Keys);
            }
        }

        /// <summary>
        /// A configured live setting is passed to the server; unconfigured ones stay unset.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void ConfiguredLiveSettingsAreEmitted(string name)
        {
            ConfiguredEnvironment all = new ConfiguredEnvironment(name, config =>
            {
                config.Api.PushEnabled = false;
                config.Api.LinkPreviewsEnabled = true;
                config.Api.EmailFeedPostsEnabled = false;
                config.Api.AccountExportRetentionHours = 48;
            });
            Dictionary<string, string> environment = TemplateReader.ServerEnvironment(all, all.Stacks.Api);
            Assert.Equal("false", environment["TK_PUSH_ENABLED"]);
            Assert.Equal("true", environment["TK_LINK_PREVIEWS_ENABLED"]);
            Assert.Equal("false", environment["TK_EMAIL_FEED_POSTS_ENABLED"]);
            Assert.Equal("48", environment["TK_ACCOUNT_EXPORT_RETENTION_HOURS"]);

            ConfiguredEnvironment one = new ConfiguredEnvironment(name, config => config.Api.AccountExportRetentionHours = 12);
            Dictionary<string, string> partial = TemplateReader.ServerEnvironment(one, one.Stacks.Api);
            Assert.Equal("12", partial["TK_ACCOUNT_EXPORT_RETENTION_HOURS"]);
            Assert.DoesNotContain("TK_PUSH_ENABLED", partial.Keys);
            Assert.DoesNotContain("TK_LINK_PREVIEWS_ENABLED", partial.Keys);
            Assert.DoesNotContain("TK_EMAIL_FEED_POSTS_ENABLED", partial.Keys);
        }

        /// <summary>
        /// The Expo token and the cookie signing key are ECS secrets from Secrets Manager, never plain variables, and
        /// the push secret starts with an empty token for the operator to fill.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void PushAndSigningKeysAreSecrets(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            Dictionary<string, string> environment = TemplateReader.ServerEnvironment(env, env.Stacks.Api);
            List<string> secrets = TemplateReader.ServerSecrets(env, env.Stacks.Api);
            foreach (string key in new string[] { "TK_EXPO_ACCESS_TOKEN", "TK_MEDIA_SIGNING_PRIVATE_KEY" })
            {
                Assert.Contains(key, secrets);
                Assert.DoesNotContain(key, environment.Keys);
            }

            env.Template(env.Stacks.Api).HasResourceProperties("AWS::SecretsManager::Secret", new Dictionary<string, object>
            {
                ["Name"] = env.Stacks.Config.ResourcePath("push"),
                ["SecretString"] = "{\"TK_EXPO_ACCESS_TOKEN\":\"\"}"
            });
            string container = env.Json(env.Stacks.Api).GetRawText();
            Assert.Contains(":TK_EXPO_ACCESS_TOKEN::", container);

            // The signing private key secret holds a generated placeholder, never key material from the repository.
            env.Template(env.Stacks.Media).HasResourceProperties("AWS::SecretsManager::Secret", new Dictionary<string, object>
            {
                ["Name"] = env.Stacks.Config.ResourcePath("media-signing-private-key"),
                ["GenerateSecretString"] = Match.AnyValue()
            });
            Assert.DoesNotContain("PRIVATE KEY", env.Json(env.Stacks.Media).GetRawText());
        }

        /// <summary>
        /// Media paths start with the property id: the video, public, and public video behaviors use a leading wildcard
        /// segment, come after the updates behaviors, and only the video path requires signed cookies.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void MediaPathPatternsArePropertyScoped(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            JsonElement config = TemplateReader.DistributionConfig(env, env.Stacks.Media);
            List<string> patterns = config.GetProperty("CacheBehaviors").EnumerateArray().Select(b => b.GetProperty("PathPattern").GetString() ?? string.Empty).ToList();

            Assert.Equal(new List<string> { "/updates/assets/*", "/updates/*", "/*/public/media/*.pdf", "/*/media/*.pdf", "/*/public/video/*", "/*/video/*", "/*/public/*" }, patterns);
            Assert.True(TemplateReader.Behavior(config, "/*/video/*").TryGetProperty("TrustedKeyGroups", out JsonElement _));
            Assert.False(TemplateReader.Behavior(config, "/*/public/video/*").TryGetProperty("TrustedKeyGroups", out JsonElement _));
            Assert.False(TemplateReader.Behavior(config, "/*/public/*").TryGetProperty("TrustedKeyGroups", out JsonElement _));
            Assert.True(config.GetProperty("DefaultCacheBehavior").TryGetProperty("TrustedKeyGroups", out JsonElement _));
        }

        /// <summary>
        /// The job template keeps the layout the backend overrides by position: output group 0 is HLS and output group
        /// 1 is the poster frame capture, with no destinations of their own, on an on-demand queue.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void JobTemplateMatchesTheBackendLayout(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            env.Template(env.Stacks.Media).HasResourceProperties("AWS::MediaConvert::Queue", new Dictionary<string, object>
            {
                ["Name"] = env.Stacks.Config.ResourceName("hls"),
                ["PricingPlan"] = "ON_DEMAND",
                ["Status"] = "ACTIVE"
            });

            JsonElement template = env.Resources(env.Stacks.Media, "AWS::MediaConvert::JobTemplate").Single().GetProperty("Properties");
            Assert.Equal(env.Stacks.Config.ResourceName("hls"), template.GetProperty("Name").GetString());
            JsonElement settings = template.GetProperty("SettingsJson");
            Assert.DoesNotContain("Destination", settings.GetRawText());

            JsonElement[] groups = settings.GetProperty("OutputGroups").EnumerateArray().ToArray();
            Assert.Equal(2, groups.Length);
            Assert.Equal("HLS_GROUP_SETTINGS", groups[0].GetProperty("OutputGroupSettings").GetProperty("Type").GetString());
            Assert.Equal("FILE_GROUP_SETTINGS", groups[1].GetProperty("OutputGroupSettings").GetProperty("Type").GetString());

            List<JsonElement> renditions = groups[0].GetProperty("Outputs").EnumerateArray().ToList();
            Assert.Equal(env.Stacks.Config.Media.HlsRenditions.Count, renditions.Count);
            foreach (JsonElement rendition in renditions)
            {
                Assert.Equal("M3U8", rendition.GetProperty("ContainerSettings").GetProperty("Container").GetString());
                Assert.Equal(MediaConvertJobTemplate.AudioSelectorName, rendition.GetProperty("AudioDescriptions")[0].GetProperty("AudioSourceName").GetString());
            }

            List<JsonElement> captures = groups[1].GetProperty("Outputs").EnumerateArray().ToList();
            Assert.Equal(2, captures.Count);
            foreach (JsonElement capture in captures)
            {
                Assert.Equal("FRAME_CAPTURE", capture.GetProperty("VideoDescription").GetProperty("CodecSettings").GetProperty("Codec").GetString());
                Assert.Equal("jpg", capture.GetProperty("Extension").GetString());
            }

            Assert.Equal(1280, captures[0].GetProperty("VideoDescription").GetProperty("Width").GetInt32());
            Assert.DoesNotContain("thumb", captures[0].GetProperty("NameModifier").GetString());
            Assert.Equal(320, captures[1].GetProperty("VideoDescription").GetProperty("Width").GetInt32());
            Assert.Contains("thumb", captures[1].GetProperty("NameModifier").GetString());

            Assert.True(settings.GetProperty("Inputs")[0].GetProperty("AudioSelectors").TryGetProperty(MediaConvertJobTemplate.AudioSelectorName, out JsonElement _));
        }

        /// <summary>
        /// The task role may create jobs only on this account's jobs, queue, and template, read only its jobs, pass
        /// only the MediaConvert role (to MediaConvert), and needs no SNS permission.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void TaskRoleHasLeastPrivilegeMediaConvertAccess(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            List<JsonElement> statements = TaskRoleStatements(env);
            string jobs = "arn:aws:mediaconvert:" + env.Stacks.Config.Region + ":" + env.Stacks.Config.Account + ":jobs/*";
            string mediaStack = env.Stacks.Media!.StackName;

            JsonElement create = statements.Single(s => s.GetProperty("Sid").GetString() == "MediaConvertCreateJob");
            Assert.Equal("mediaconvert:CreateJob", create.GetProperty("Action").GetString());
            string createResources = create.GetProperty("Resource").GetRawText();
            Assert.Contains(jobs, createResources);
            Assert.Contains(mediaStack + ":ExportsOutputFnGetAttTranscodeQueueArn", createResources);
            Assert.Contains(mediaStack + ":ExportsOutputFnGetAttTranscodeJobTemplateArn", createResources);
            Assert.DoesNotContain("\"*\"", createResources);

            JsonElement get = statements.Single(s => s.GetProperty("Sid").GetString() == "MediaConvertGetJob");
            Assert.Equal("mediaconvert:GetJob", get.GetProperty("Action").GetString());
            Assert.Equal(jobs, get.GetProperty("Resource").GetString());

            JsonElement pass = statements.Single(s => s.GetRawText().Contains("iam:PassRole"));
            Assert.Contains(mediaStack + ":ExportsOutputFnGetAttMediaConvertRole", pass.GetProperty("Resource").GetRawText());
            Assert.Equal("mediaconvert.amazonaws.com", pass.GetProperty("Condition").GetProperty("StringEquals").GetProperty("iam:PassedToService").GetString());

            string all = string.Join("\n", statements.Select(s => s.GetRawText()));
            Assert.DoesNotContain("mediaconvert:ListJobs", all);
            Assert.DoesNotContain("mediaconvert:CancelJob", all);
            Assert.DoesNotContain("mediaconvert:*", all);
            Assert.DoesNotContain("\"sns:", all);
            foreach (JsonElement statement in statements.Where(s => s.GetRawText().Contains("mediaconvert:")))
            {
                bool wildcard = statement.GetProperty("Resource").ValueKind == JsonValueKind.String && statement.GetProperty("Resource").GetString() == "*";
                if (wildcard) Assert.Equal("mediaconvert:DescribeEndpoints", statement.GetProperty("Action").GetString());
            }
        }

        /// <summary>
        /// The MediaConvert role reads only the video source bucket and writes only the transcoded and images buckets,
        /// and job state changes reach the backend webhook through the topic.
        /// </summary>
        /// <param name="name">Environment.</param>
        [Theory]
        [InlineData("dev")]
        [InlineData("prod")]
        public void MediaConvertRoleAndJobEventsAreScoped(string name)
        {
            SynthesizedEnvironment env = Environments.Get(name);
            JsonElement policy = env.Resources(env.Stacks.Media, "AWS::IAM::Policy").Single(p => p.GetRawText().Contains("ReadOriginals"));
            List<JsonElement> statements = policy.GetProperty("Properties").GetProperty("PolicyDocument").GetProperty("Statement").EnumerateArray().ToList();
            Assert.Equal(2, statements.Count);
            string read = statements.Single(s => s.GetProperty("Sid").GetString() == "ReadOriginals").GetRawText();
            Assert.Contains("MediaVideoSourceBucket", read);
            Assert.Contains("s3:GetObject", read);
            string write = statements.Single(s => s.GetProperty("Sid").GetString() == "WriteRenditions").GetRawText();
            Assert.Contains("MediaTranscodedBucket", write);
            Assert.Contains("MediaImagesBucket", write);
            Assert.DoesNotContain("MediaSourceBucket", write);
            Assert.DoesNotContain("MediaRetainedBucket", write);

            Template template = env.Template(env.Stacks.Media);
            template.HasResourceProperties("AWS::SNS::Subscription", new Dictionary<string, object>
            {
                ["Protocol"] = "https",
                ["Endpoint"] = env.Stacks.Config.Domains.ApiUrl + "/api/v1/webhooks/mediaconvert",
                ["RawMessageDelivery"] = false
            });
            template.HasResourceProperties("AWS::Events::Rule", new Dictionary<string, object>
            {
                ["Targets"] = Match.ArrayWith(new object[]
                {
                    Match.ObjectLike(new Dictionary<string, object> { ["Arn"] = new Dictionary<string, object> { ["Ref"] = Match.StringLikeRegexp("JobEventsTopic") } })
                })
            });
        }

        /// <summary>
        /// The shared API receives the shared pipeline's queue, template, and push secret.
        /// </summary>
        /// <param name="name">Shared deployment.</param>
        [Theory]
        [InlineData("shared-dev")]
        [InlineData("shared-prod")]
        public void SharedApiUsesTheSharedPipeline(string name)
        {
            SynthesizedDeployment deployment = Environments.GetShared(name);
            SharedStacks shared = deployment.Stacks.Shared!;
            Dictionary<string, string> environment = TemplateReader.ServerEnvironment(deployment, shared.Api);
            Assert.Equal(shared.Config.ResourceName("hls"), environment["TK_MEDIACONVERT_JOB_TEMPLATE"]);
            Assert.Contains(shared.MediaPipeline!.StackName + ":ExportsOutputFnGetAttTranscodeQueueArn", environment["TK_MEDIACONVERT_QUEUE_ARN"]);
            foreach (string key in ServerEnvironmentKeys.OptionalLiveKeys) Assert.DoesNotContain(key, environment.Keys);
            Assert.Contains("TK_EXPO_ACCESS_TOKEN", TemplateReader.ServerSecrets(deployment, shared.Api));
            deployment.Template(shared.MediaPipeline).ResourceCountIs("AWS::MediaConvert::JobTemplate", 1);
            foreach (PropertyStacks property in deployment.Stacks.Properties)
            {
                deployment.Template(property.Media).ResourceCountIs("AWS::MediaConvert::JobTemplate", 0);
                JsonElement config = TemplateReader.DistributionConfig(deployment, property.Media);
                Assert.True(TemplateReader.Behavior(config, "/*/video/*").TryGetProperty("TrustedKeyGroups", out JsonElement _));
            }
        }

        private static List<JsonElement> TaskRoleStatements(SynthesizedEnvironment env)
        {
            JsonElement policy = env.Resources(env.Stacks.Api, "AWS::IAM::Policy").Single(p => p.GetRawText().Contains("MediaConvertCreateJob"));
            return policy.GetProperty("Properties").GetProperty("PolicyDocument").GetProperty("Statement").EnumerateArray().ToList();
        }

        private static string Pascal(string bucket)
        {
            return string.Concat(bucket.Split('_').Select(p => p.Substring(0, 1) + p.Substring(1).ToLowerInvariant()));
        }
    }
}
