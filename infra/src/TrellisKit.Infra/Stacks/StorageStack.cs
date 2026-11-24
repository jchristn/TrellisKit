namespace TrellisKit.Infra.Stacks
{
    using System;
    using System.Collections.Generic;
    using Amazon.CDK;
    using Amazon.CDK.AWS.IAM;
    using Amazon.CDK.AWS.S3;
    using TrellisKit.Infra.Config;
    using TrellisKit.Infra.Nag;
    using Constructs;

    /// <summary>
    /// S3 buckets, one per purpose so each has its own access, lifecycle, and cost profile:
    /// website build; media source (original image and file uploads, kept forever); media video source (original
    /// video uploads, kept forever); media images (processed image derivatives served by the media CDN); media
    /// transcoded (HLS renditions written by MediaConvert, served by the media CDN); media retained (content removed
    /// by moderators, kept for a limited time for review and appeals); updates (live update bundles, assets, and
    /// manifests, served by the media CDN under the updates path); and access logs. In the shared topology the website
    /// buckets belong to each property's web stack instead, and this stack only grants them log delivery. Every bucket blocks public
    /// access, enforces TLS, and is encrypted. CloudFront reads the website, images, and transcoded buckets through
    /// Origin Access Control; the grant is scoped to distributions in this account here because a per-distribution
    /// grant would create a dependency cycle between stacks. The server receives each media bucket name as an
    /// environment variable and the admin console can override them (storage settings).
    /// </summary>
    public class StorageStack : Stack
    {
        /// <summary>
        /// Receives S3 server access logs, ALB access logs, and CloudFront standard logs.
        /// </summary>
        public Bucket AccessLogBucket { get; }

        /// <summary>
        /// Access log prefix of the website buckets owned by property web stacks (shared topology). Each property
        /// writes under its own id below this prefix.
        /// </summary>
        public static readonly string SiteAccessLogPrefix = "s3/sites/";

        /// <summary>
        /// Website static build (synced by CI). Null in the shared topology, where each property's web stack owns its
        /// website bucket.
        /// </summary>
        public Bucket? WebsiteBucket { get; }

        /// <summary>
        /// Live update bundles and assets (content-addressed, cached immutably) and manifests (never cached), served by
        /// the media distribution under <see cref="UpdatesSettings.PathPattern"/>.
        /// </summary>
        public Bucket UpdatesBucket { get; }

        /// <summary>
        /// Original image and file uploads from clients (presigned PUT). Never expired.
        /// </summary>
        public Bucket MediaSourceBucket { get; }

        /// <summary>
        /// Original video uploads from clients (presigned multipart PUT), read by MediaConvert. Never expired.
        /// </summary>
        public Bucket MediaVideoSourceBucket { get; }

        /// <summary>
        /// Processed image derivatives (sized images, avatars, thumbnails, poster frames, link preview images).
        /// </summary>
        public Bucket MediaImagesBucket { get; }

        /// <summary>
        /// HLS renditions written by MediaConvert.
        /// </summary>
        public Bucket MediaTranscodedBucket { get; }

        /// <summary>
        /// Content removed by moderators, retained for <see cref="StorageSettings.RetainedExpirationDays"/>.
        /// </summary>
        public Bucket MediaRetainedBucket { get; }

        /// <summary>
        /// Every media bucket (source, video source, images, transcoded, retained).
        /// </summary>
        public Bucket[] MediaBuckets
        {
            get { return new Bucket[] { MediaSourceBucket, MediaVideoSourceBucket, MediaImagesBucket, MediaTranscodedBucket, MediaRetainedBucket }; }
        }

        /// <summary>
        /// Instantiate the stack.
        /// </summary>
        /// <param name="scope">Parent construct.</param>
        /// <param name="id">Construct id.</param>
        /// <param name="config">Environment configuration.</param>
        /// <param name="props">Stack properties.</param>
        /// <exception cref="ArgumentNullException">The configuration is null.</exception>
        public StorageStack(Construct scope, string id, EnvironmentConfig config, IStackProps props) : base(scope, id, props)
        {
            ArgumentNullException.ThrowIfNull(config);

            AccessLogBucket = new Bucket(this, "AccessLogBucket", new BucketProps
            {
                BucketName = config.BucketName("access-logs"),
                BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
                Encryption = BucketEncryption.S3_MANAGED,
                EnforceSSL = true,
                // CloudFront standard logging writes with ACLs, so ACLs must stay enabled on this bucket only.
                ObjectOwnership = ObjectOwnership.BUCKET_OWNER_PREFERRED,
                RemovalPolicy = config.StatefulRemovalPolicy,
                LifecycleRules = new ILifecycleRule[]
                {
                    new LifecycleRule
                    {
                        Id = "expire-logs",
                        Expiration = Duration.Days(config.Storage.AccessLogExpirationDays),
                        AbortIncompleteMultipartUploadAfter = Duration.Days(config.Storage.AbortIncompleteMultipartUploadDays)
                    }
                }
            });
            NagHelper.Suppress(AccessLogBucket, "AwsSolutions-S1",
                "This is the access log destination; logging it to itself would recurse.");

            if (!config.IsShared)
            {
                WebsiteBucket = PrivateBucket(config, "WebsiteBucket", "website", true, null, new LifecycleRule
                {
                    Id = "expire-old-builds",
                    NoncurrentVersionExpiration = Duration.Days(config.Storage.NoncurrentVersionExpirationDays),
                    AbortIncompleteMultipartUploadAfter = Duration.Days(config.Storage.AbortIncompleteMultipartUploadDays)
                });
            }
            else
            {
                // Property web stacks own their website buckets and log here through an imported reference, so the
                // grant cannot be added per bucket without a cycle; scope it to this account's website buckets of this
                // environment (<slug>-<env>-website-<account>), so another environment in the same account cannot log here.
                AccessLogBucket.AddToResourcePolicy(new PolicyStatement(new PolicyStatementProps
                {
                    Sid = "AllowSiteBucketAccessLogs",
                    Effect = Effect.ALLOW,
                    Principals = new IPrincipal[] { new ServicePrincipal("logging.s3.amazonaws.com") },
                    Actions = new string[] { "s3:PutObject" },
                    Resources = new string[] { AccessLogBucket.ArnForObjects(SiteAccessLogPrefix + "*") },
                    Conditions = new Dictionary<string, object>
                    {
                        ["StringEquals"] = new Dictionary<string, object> { ["aws:SourceAccount"] = config.Account },
                        ["ArnLike"] = new Dictionary<string, object> { ["aws:SourceArn"] = "arn:aws:s3:::*-" + config.Name + "-website-" + config.Account }
                    }
                }));
            }

            ICorsRule[] uploadCors = new ICorsRule[]
            {
                new CorsRule
                {
                    AllowedOrigins = config.WebOrigins.ToArray(),
                    AllowedMethods = new HttpMethods[] { HttpMethods.PUT, HttpMethods.GET, HttpMethods.HEAD },
                    AllowedHeaders = new string[] { "*" },
                    ExposedHeaders = new string[] { "ETag" },
                    MaxAge = config.Media.CorsMaxAgeSeconds
                }
            };

            // Originals are written once by presigned uploads and never overwritten, so versioning adds cost only.
            // Every original is kept: no expiration, only cheaper storage classes as it ages.
            MediaSourceBucket = PrivateBucket(config, "MediaSourceBucket", "media-source", false, uploadCors, OriginalsRule(config));
            MediaVideoSourceBucket = PrivateBucket(config, "MediaVideoSourceBucket", "media-video-source", false, uploadCors, OriginalsRule(config));

            MediaImagesBucket = PrivateBucket(config, "MediaImagesBucket", "media-images", true, null, new LifecycleRule
            {
                Id = "expire-replaced-images",
                NoncurrentVersionExpiration = Duration.Days(config.Storage.NoncurrentVersionExpirationDays),
                AbortIncompleteMultipartUploadAfter = Duration.Days(config.Storage.AbortIncompleteMultipartUploadDays)
            });

            MediaTranscodedBucket = PrivateBucket(config, "MediaTranscodedBucket", "media-transcoded", true, null, new LifecycleRule
            {
                Id = "expire-replaced-renditions",
                NoncurrentVersionExpiration = Duration.Days(config.Storage.NoncurrentVersionExpirationDays),
                AbortIncompleteMultipartUploadAfter = Duration.Days(config.Storage.AbortIncompleteMultipartUploadDays)
            });

            MediaRetainedBucket = PrivateBucket(config, "MediaRetainedBucket", "media-retained", false, null, new LifecycleRule
            {
                Id = "expire-retained-content",
                Expiration = Duration.Days(config.Storage.RetainedExpirationDays),
                AbortIncompleteMultipartUploadAfter = Duration.Days(config.Storage.AbortIncompleteMultipartUploadDays)
            });

            // Bundles and assets are written once under content-addressed keys; manifests may be rewritten, so keep a
            // short version history for them.
            UpdatesBucket = PrivateBucket(config, "UpdatesBucket", "updates", true, null, new LifecycleRule
            {
                Id = "expire-replaced-update-objects",
                NoncurrentVersionExpiration = Duration.Days(config.Storage.NoncurrentVersionExpirationDays),
                AbortIncompleteMultipartUploadAfter = Duration.Days(config.Storage.AbortIncompleteMultipartUploadDays)
            });

            if (WebsiteBucket != null) GrantCloudFrontRead(WebsiteBucket, config, "*");
            GrantCloudFrontRead(MediaImagesBucket, config, "*");
            GrantCloudFrontRead(MediaTranscodedBucket, config, "*");
            // Only the served prefix (updates/, the object keys behind the /updates/* CDN path): source maps under
            // private/sourcemaps/ stay unreadable by CloudFront even if a behavior were added for them.
            GrantCloudFrontRead(UpdatesBucket, config, config.Updates.PathPrefix.TrimStart('/') + "/*");

            if (WebsiteBucket != null) new CfnOutput(this, "WebsiteBucketName", new CfnOutputProps { Value = WebsiteBucket.BucketName });
            new CfnOutput(this, "MediaSourceBucketName", new CfnOutputProps { Value = MediaSourceBucket.BucketName });
            new CfnOutput(this, "MediaVideoSourceBucketName", new CfnOutputProps { Value = MediaVideoSourceBucket.BucketName });
            new CfnOutput(this, "MediaImagesBucketName", new CfnOutputProps { Value = MediaImagesBucket.BucketName });
            new CfnOutput(this, "MediaTranscodedBucketName", new CfnOutputProps { Value = MediaTranscodedBucket.BucketName });
            new CfnOutput(this, "MediaRetainedBucketName", new CfnOutputProps { Value = MediaRetainedBucket.BucketName });
            new CfnOutput(this, "UpdatesBucketName", new CfnOutputProps { Value = UpdatesBucket.BucketName });
        }

        private Bucket PrivateBucket(EnvironmentConfig config, string id, string purpose, bool versioned, ICorsRule[]? cors, ILifecycleRule rule)
        {
            return new Bucket(this, id, new BucketProps
            {
                BucketName = config.BucketName(purpose),
                BlockPublicAccess = BlockPublicAccess.BLOCK_ALL,
                Encryption = BucketEncryption.S3_MANAGED,
                EnforceSSL = true,
                ObjectOwnership = ObjectOwnership.BUCKET_OWNER_ENFORCED,
                Versioned = versioned,
                ServerAccessLogsBucket = AccessLogBucket,
                ServerAccessLogsPrefix = "s3/" + purpose + "/",
                RemovalPolicy = config.StatefulRemovalPolicy,
                Cors = cors,
                LifecycleRules = new ILifecycleRule[] { rule }
            });
        }

        private static ILifecycleRule OriginalsRule(EnvironmentConfig config)
        {
            return new LifecycleRule
            {
                Id = "originals-to-colder-storage",
                AbortIncompleteMultipartUploadAfter = Duration.Days(config.Storage.AbortIncompleteMultipartUploadDays),
                Transitions = new ITransition[]
                {
                    new Transition
                    {
                        StorageClass = StorageClass.INFREQUENT_ACCESS,
                        TransitionAfter = Duration.Days(config.Storage.IngestInfrequentAccessDays)
                    },
                    new Transition
                    {
                        StorageClass = StorageClass.GLACIER_INSTANT_RETRIEVAL,
                        TransitionAfter = Duration.Days(Math.Max(config.Storage.SourceArchiveDays, config.Storage.IngestInfrequentAccessDays + 30))
                    }
                }
            };
        }

        private static void GrantCloudFrontRead(Bucket bucket, EnvironmentConfig config, string keyPattern)
        {
            bucket.AddToResourcePolicy(new PolicyStatement(new PolicyStatementProps
            {
                Sid = "AllowCloudFrontOriginAccessControl",
                Effect = Effect.ALLOW,
                Principals = new IPrincipal[] { new ServicePrincipal("cloudfront.amazonaws.com") },
                Actions = new string[] { "s3:GetObject" },
                Resources = new string[] { bucket.ArnForObjects(keyPattern) },
                Conditions = new Dictionary<string, object>
                {
                    ["StringEquals"] = new Dictionary<string, object> { ["AWS:SourceAccount"] = config.Account },
                    ["ArnLike"] = new Dictionary<string, object>
                    {
                        ["AWS:SourceArn"] = "arn:aws:cloudfront::" + config.Account + ":distribution/*"
                    }
                }
            }));
        }
    }
}
