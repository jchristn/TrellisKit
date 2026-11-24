namespace TrellisKit.Infra.Stacks
{
    using System;
    using System.Collections.Generic;
    using Amazon.CDK;
    using Amazon.CDK.AWS.CloudFront;
    using Amazon.CDK.AWS.Events;
    using Amazon.CDK.AWS.Events.Targets;
    using Amazon.CDK.AWS.IAM;
    using Amazon.CDK.AWS.KMS;
    using Amazon.CDK.AWS.MediaConvert;
    using Amazon.CDK.AWS.SecretsManager;
    using Amazon.CDK.AWS.SNS;
    using Amazon.CDK.AWS.SNS.Subscriptions;
    using TrellisKit.Infra.Config;
    using TrellisKit.Infra.Nag;

    /// <summary>
    /// The parts of the media pipeline the API uses, independent of any property's media host: the CloudFront
    /// signing key group and its private key secret, the MediaConvert service role, an on-demand MediaConvert queue,
    /// the HLS and frame capture job template (<see cref="MediaConvertJobTemplate"/>), and the EventBridge rule that
    /// forwards MediaConvert job state changes to an encrypted SNS topic with an HTTPS subscription to the API webhook.
    /// Created inside <see cref="MediaStack"/> (per-property topology) or <see cref="MediaPipelineStack"/> (shared topology).
    /// </summary>
    public class MediaPipeline
    {
        /// <summary>
        /// CloudFront public key that validates signed cookies.
        /// </summary>
        public PublicKey SigningPublicKey { get; }

        /// <summary>
        /// Key group trusted by the protected media behaviors.
        /// </summary>
        public KeyGroup KeyGroup { get; }

        /// <summary>
        /// Secret that holds the private half of the signing key pair (filled manually).
        /// </summary>
        public Secret SigningPrivateKeySecret { get; }

        /// <summary>
        /// Role MediaConvert assumes to read originals and write renditions.
        /// </summary>
        public Role MediaConvertRole { get; }

        /// <summary>
        /// Topic that receives MediaConvert job state changes.
        /// </summary>
        public Topic JobEventsTopic { get; }

        /// <summary>
        /// On-demand queue the backend submits transcode jobs to (TK_MEDIACONVERT_QUEUE_ARN).
        /// </summary>
        public CfnQueue Queue { get; }

        /// <summary>
        /// Job template the backend submits jobs from (TK_MEDIACONVERT_JOB_TEMPLATE).
        /// </summary>
        public CfnJobTemplate JobTemplate { get; }

        /// <summary>
        /// Name of <see cref="JobTemplate"/>, unique per account and region (resource prefix plus
        /// <see cref="MediaSettings.JobTemplateSuffix"/>).
        /// </summary>
        public string JobTemplateName { get; }

        /// <summary>
        /// ARN pattern of the jobs this account creates in the stack's region, for IAM scoping.
        /// </summary>
        public string JobArnPattern { get; }

        /// <summary>
        /// Create the pipeline resources directly in a stack (construct ids are stable across topologies).
        /// </summary>
        /// <param name="scope">Stack that owns the resources.</param>
        /// <param name="config">Configuration of that stack.</param>
        /// <param name="storage">Buckets.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public MediaPipeline(Stack scope, EnvironmentConfig config, StorageStack storage)
        {
            ArgumentNullException.ThrowIfNull(scope);
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(storage);

            SigningPublicKey = new PublicKey(scope, "SigningPublicKey", new PublicKeyProps
            {
                PublicKeyName = config.ResourceName("media-signing"),
                Comment = "Validates signed cookies for paywalled media",
                EncodedKey = config.Media.SigningPublicKeyPem
            });

            KeyGroup = new KeyGroup(scope, "SigningKeyGroup", new KeyGroupProps
            {
                KeyGroupName = config.ResourceName("media-signing"),
                Items = new IPublicKey[] { SigningPublicKey }
            });

            SigningPrivateKeySecret = new Secret(scope, "SigningPrivateKey", new SecretProps
            {
                SecretName = config.ResourcePath("media-signing-private-key"),
                Description = "PEM private key matching the CloudFront media signing public key. Replace the generated placeholder.",
                GenerateSecretString = new SecretStringGenerator { PasswordLength = 32, ExcludePunctuation = true },
                RemovalPolicy = config.StatefulRemovalPolicy
            });
            NagHelper.Suppress(SigningPrivateKeySecret, "AwsSolutions-SMG4",
                "Key pair rotation requires a new CloudFront public key; it is a planned manual procedure (see infra/README.md).");

            MediaConvertRole = new Role(scope, "MediaConvertRole", new RoleProps
            {
                RoleName = config.ResourceName("mediaconvert"),
                Description = "Assumed by MediaConvert jobs to read video originals and write HLS renditions and poster frames",
                AssumedBy = new ServicePrincipal("mediaconvert.amazonaws.com")
            });
            MediaConvertRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Sid = "ReadOriginals",
                Actions = new string[] { "s3:GetObject" },
                Resources = new string[] { storage.MediaVideoSourceBucket.ArnForObjects("*") }
            }));
            MediaConvertRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Sid = "WriteRenditions",
                Actions = new string[] { "s3:PutObject" },
                Resources = new string[] { storage.MediaTranscodedBucket.ArnForObjects("*"), storage.MediaImagesBucket.ArnForObjects("*") }
            }));
            NagHelper.SuppressWildcards(MediaConvertRole,
                "MediaConvert reads any video original and writes renditions and poster frames under keys chosen per job, so object-level wildcards on the video source, transcoded, and images buckets are required.",
                NagHelper.ResourcePattern("MediaVideoSourceBucket", "Arn", "\\/\\*"),
                NagHelper.ResourcePattern("MediaTranscodedBucket", "Arn", "\\/\\*"),
                NagHelper.ResourcePattern("MediaImagesBucket", "Arn", "\\/\\*"));

            // Queue and template share one name; they are different resource types.
            JobTemplateName = config.ResourceName(config.Media.JobTemplateSuffix);
            Queue = new CfnQueue(scope, "TranscodeQueue", new CfnQueueProps
            {
                Name = JobTemplateName,
                Description = config.DisplayName + " video transcode jobs",
                PricingPlan = "ON_DEMAND",
                Status = "ACTIVE"
            });

            JobTemplate = new CfnJobTemplate(scope, "TranscodeJobTemplate", new CfnJobTemplateProps
            {
                Name = JobTemplateName,
                Description = "HLS ladder (output group 0) and poster frame capture (output group 1). The backend overrides only these two destinations, by position.",
                Category = "media",
                Queue = Queue.AttrArn,
                StatusUpdateInterval = "SECONDS_60",
                SettingsJson = MediaConvertJobTemplate.BuildSettings(config.Media)
            });

            JobArnPattern = "arn:aws:mediaconvert:" + config.Region + ":" + config.Account + ":jobs/*";

            Key topicKey = new Key(scope, "JobEventsKey", new KeyProps
            {
                Alias = "alias/" + config.ResourceName("mediaconvert-events"),
                Description = "Encrypts the MediaConvert job events topic",
                EnableKeyRotation = true,
                RemovalPolicy = config.StatefulRemovalPolicy
            });
            topicKey.AddToResourcePolicy(new PolicyStatement(new PolicyStatementProps
            {
                Sid = "AllowEventBridgeToPublish",
                Principals = new IPrincipal[] { new ServicePrincipal("events.amazonaws.com") },
                Actions = new string[] { "kms:Decrypt", "kms:GenerateDataKey*" },
                Resources = new string[] { "*" }
            }));

            JobEventsTopic = new Topic(scope, "JobEventsTopic", new TopicProps
            {
                TopicName = config.ResourceName("mediaconvert-events"),
                DisplayName = "MediaConvert job state changes",
                MasterKey = topicKey,
                EnforceSSL = true
            });
            // In the shared topology this is the primary (first) property's API host; every property's API host
            // reaches the same service.
            JobEventsTopic.AddSubscription(new UrlSubscription(config.Domains.ApiUrl + config.Media.WebhookPath, new UrlSubscriptionProps
            {
                Protocol = SubscriptionProtocol.HTTPS,
                RawMessageDelivery = false
            }));

            Rule rule = new Rule(scope, "JobStateChangeRule", new RuleProps
            {
                RuleName = config.ResourceName("mediaconvert-job-state"),
                Description = "Forward MediaConvert job state changes to the API webhook",
                EventPattern = new EventPattern
                {
                    Source = new string[] { "aws.mediaconvert" },
                    DetailType = new string[] { "MediaConvert Job State Change" },
                    Detail = new Dictionary<string, object> { ["status"] = config.Media.JobStatuses.ToArray() }
                }
            });
            rule.AddTarget(new SnsTopic(JobEventsTopic));
        }
    }
}
