namespace TrellisKit.Infra.Stacks
{
    using System.Collections.Generic;

    /// <summary>
    /// Names of the storage, media, push, feature, mail, proxy, live updates, and deployment environment variables the infrastructure passes to
    /// the server, kept in one place so they track the backend settings registry
    /// (backend/src/TrellisKit.Server/Settings/SettingsRegistry.cs, LiveUpdateSettingFields.cs, and AiSettingFields.cs).
    /// Every name matches the registry.
    /// </summary>
    public static class ServerEnvironmentKeys
    {
        /// <summary>
        /// Deployment mode, SingleProperty or MultiProperty (setting deployment.mode).
        /// </summary>
        public static readonly string DeploymentMode = "TK_DEPLOYMENT_MODE";

        /// <summary>
        /// Deployment name, the trelliskit.deployment telemetry attribute (setting deployment.name, Restart). Set to the
        /// property slug, or the deployment id of a shared deployment.
        /// </summary>
        public static readonly string DeploymentName = "TK_DEPLOYMENT_NAME";

        /// <summary>
        /// Resolve the property from X-Forwarded-Host instead of Host (setting deployment.trustForwardedHost, Restart).
        /// The website distribution sets X-Forwarded-Host to the viewer's host on every API request.
        /// </summary>
        public static readonly string TrustForwardedHost = "TK_TRUST_FORWARDED_HOST";

        /// <summary>
        /// Comma-separated proxy addresses and CIDR ranges whose X-Forwarded-For is honored (setting rest.trustedProxies).
        /// Set to the VPC CIDR, so only the load balancer's X-Forwarded-For is trusted.
        /// </summary>
        public static readonly string TrustedProxies = "TK_TRUSTED_PROXIES";

        /// <summary>
        /// Comma-separated addresses and CIDR ranges trusted in addition to TK_TRUSTED_PROXIES (setting
        /// rest.additionalTrustedProxies, Live), normally the CloudFront origin-facing ranges. Set only when configured.
        /// </summary>
        public static readonly string AdditionalTrustedProxies = "TK_ADDITIONAL_TRUSTED_PROXIES";

        /// <summary>
        /// DNS-over-HTTPS endpoint for domain verification (setting deployment.domainVerificationResolverUrl, Live). Set
        /// only when configured.
        /// </summary>
        public static readonly string DomainVerificationResolverUrl = "TK_DOMAIN_VERIFICATION_RESOLVER_URL";

        /// <summary>
        /// Secret: base64 of 32 bytes that encrypts per-property integration secrets and queued email payloads (setting
        /// secrets.encryptionKey, Restart). Generated once into {prefix}/secrets-key and never changed by a deploy.
        /// </summary>
        public static readonly string SecretsEncryptionKey = "TK_SECRETS_ENCRYPTION_KEY";

        /// <summary>
        /// Secret: comma-separated earlier keys that still decrypt after a rotation (setting
        /// secrets.previousEncryptionKeys, Restart). Empty until the first rotation.
        /// </summary>
        public static readonly string SecretsPreviousEncryptionKeys = "TK_SECRETS_PREVIOUS_ENCRYPTION_KEYS";

        /// <summary>
        /// Deployment mail provider, Auto, Mailgun, Smtp, or Log (setting email.provider, Live). Set only when configured
        /// (mail.provider Ses passes Smtp; None passes nothing).
        /// </summary>
        public static readonly string EmailProvider = "TK_EMAIL_PROVIDER";

        /// <summary>
        /// Mailgun sending domain for deployment mail (setting email.mailgunDomain, Live). Set only when configured.
        /// </summary>
        public static readonly string MailgunDomain = "TK_MAILGUN_DOMAIN";

        /// <summary>
        /// Mailgun API base URL, US or EU (setting email.mailgunBaseUrl, Live). Set only when configured.
        /// </summary>
        public static readonly string MailgunBaseUrl = "TK_MAILGUN_BASE_URL";

        /// <summary>
        /// Secret: Mailgun API key for deployment mail (setting email.mailgunApiKey, Live), from {prefix}/mailgun only,
        /// and injected only when the deployment uses Mailgun.
        /// </summary>
        public static readonly string MailgunApiKey = "TK_MAILGUN_API_KEY";

        /// <summary>
        /// Partio REST API base URL (setting documents.partio.baseUrl, Live). Set only when partio.baseUrl is configured.
        /// </summary>
        public static readonly string PartioBaseUrl = "TK_PARTIO_BASE_URL";

        /// <summary>
        /// Secret: Partio bearer token (setting documents.partio.apiKey, Live), from {prefix}/partio only, and injected
        /// only when partio.baseUrl is configured.
        /// </summary>
        public static readonly string PartioApiKey = "TK_PARTIO_API_KEY";

        /// <summary>
        /// Comma-separated platform host names (setting platform.hosts, Live). Shared topology only.
        /// </summary>
        public static readonly string PlatformHosts = "TK_PLATFORM_HOSTS";

        /// <summary>
        /// Email of the first platform administrator (setting platform.bootstrapAdminEmail, Live). Shared topology only,
        /// and only when the deployment file sets it.
        /// </summary>
        public static readonly string PlatformBootstrapAdminEmail = "TK_PLATFORM_BOOTSTRAP_ADMIN_EMAIL";

        /// <summary>
        /// Media storage mode, Local or S3 (setting media.storageMode).
        /// </summary>
        public static readonly string MediaStorageMode = "TK_MEDIA_STORAGE_MODE";

        /// <summary>
        /// Region of the media buckets and MediaConvert (setting media.s3Region).
        /// </summary>
        public static readonly string MediaS3Region = "TK_MEDIA_S3_REGION";

        /// <summary>
        /// Original image uploads (setting media.sourceBucket).
        /// </summary>
        public static readonly string MediaSourceBucket = "TK_MEDIA_SOURCE_BUCKET";

        /// <summary>
        /// Original video uploads (setting media.videoSourceBucket).
        /// </summary>
        public static readonly string MediaVideoSourceBucket = "TK_MEDIA_VIDEO_SOURCE_BUCKET";

        /// <summary>
        /// Image variants, posters, and link previews (setting media.imagesBucket).
        /// </summary>
        public static readonly string MediaImagesBucket = "TK_MEDIA_IMAGES_BUCKET";

        /// <summary>
        /// HLS renditions (setting media.transcodedBucket).
        /// </summary>
        public static readonly string MediaTranscodedBucket = "TK_MEDIA_TRANSCODED_BUCKET";

        /// <summary>
        /// Served objects of moderator-removed content (setting media.retainedBucket).
        /// </summary>
        public static readonly string MediaRetainedBucket = "TK_MEDIA_RETAINED_BUCKET";

        /// <summary>
        /// Media CDN origin, for example https://media.example.test (setting media.publicBaseUrl). Per property; not
        /// set in the shared topology.
        /// </summary>
        public static readonly string MediaPublicBaseUrl = "TK_MEDIA_PUBLIC_BASE_URL";

        /// <summary>
        /// Domain attribute of media grant cookies (setting media.cookieDomain). Per property; not set in the shared topology.
        /// </summary>
        public static readonly string MediaCookieDomain = "TK_MEDIA_COOKIE_DOMAIN";

        /// <summary>
        /// Video processor, MediaConvert in AWS (setting media.videoProcessor).
        /// </summary>
        public static readonly string MediaVideoProcessor = "TK_MEDIA_VIDEO_PROCESSOR";

        /// <summary>
        /// CloudFront public key id of the media signing key group (setting media.cloudFrontKeyPairId).
        /// </summary>
        public static readonly string MediaSigningKeyPairId = "TK_MEDIA_SIGNING_KEY_PAIR_ID";

        /// <summary>
        /// Secret: PEM private key that signs media cookies (setting media.cloudFrontPrivateKeyPem).
        /// </summary>
        public static readonly string MediaSigningPrivateKey = "TK_MEDIA_SIGNING_PRIVATE_KEY";

        /// <summary>
        /// Role MediaConvert assumes (setting media.mediaConvertRoleArn).
        /// </summary>
        public static readonly string MediaConvertRoleArn = "TK_MEDIACONVERT_ROLE_ARN";

        /// <summary>
        /// Topic of MediaConvert job state changes, checked by the webhook (setting media.snsTopicArn).
        /// </summary>
        public static readonly string MediaSnsTopicArn = "TK_MEDIA_SNS_TOPIC_ARN";

        /// <summary>
        /// MediaConvert queue that runs transcode jobs (setting media.mediaConvertQueueArn).
        /// </summary>
        public static readonly string MediaConvertQueueArn = "TK_MEDIACONVERT_QUEUE_ARN";

        /// <summary>
        /// Name of the MediaConvert job template (setting media.mediaConvertJobTemplate). The backend overrides only
        /// the destinations of its first (HLS) and second (frame capture) output groups.
        /// </summary>
        public static readonly string MediaConvertJobTemplate = "TK_MEDIACONVERT_JOB_TEMPLATE";

        /// <summary>
        /// Push notifications on or off (setting push.enabled).
        /// </summary>
        public static readonly string PushEnabled = "TK_PUSH_ENABLED";

        /// <summary>
        /// Secret: Expo push access token (setting push.expoAccessToken). Empty sends unauthenticated pushes.
        /// </summary>
        public static readonly string ExpoAccessToken = "TK_EXPO_ACCESS_TOKEN";

        /// <summary>
        /// Link preview cards on or off (setting linkPreviews.enabled).
        /// </summary>
        public static readonly string LinkPreviewsEnabled = "TK_LINK_PREVIEWS_ENABLED";

        /// <summary>
        /// Feed post emails on or off (setting email.feedPostEmailsEnabled).
        /// </summary>
        public static readonly string EmailFeedPostsEnabled = "TK_EMAIL_FEED_POSTS_ENABLED";

        /// <summary>
        /// Hours a finished account export stays downloadable (setting account.exportRetentionHours).
        /// </summary>
        public static readonly string AccountExportRetentionHours = "TK_ACCOUNT_EXPORT_RETENTION_HOURS";

        /// <summary>
        /// Live updates storage mode, Local or S3 (setting updates.storageMode, Restart).
        /// </summary>
        public static readonly string UpdatesStorageMode = "TK_UPDATES_STORAGE_MODE";

        /// <summary>
        /// Region of the updates bucket (setting updates.s3Region, Restart).
        /// </summary>
        public static readonly string UpdatesS3Region = "TK_UPDATES_S3_REGION";

        /// <summary>
        /// Updates bucket (setting updates.bucket, Restart). Assets are stored at updates/assets/{propertyId}/{sha256}, mirroring
        /// their CDN path; source maps at private/sourcemaps/, which the CDN never serves.
        /// </summary>
        public static readonly string UpdatesBucket = "TK_UPDATES_BUCKET";

        /// <summary>
        /// Public base URL of updates assets, for example https://media.example.test/updates (setting updates.publicBaseUrl,
        /// Live; set always because the CDN decides it, which locks the console field). Per property;
        /// not set in the shared topology.
        /// </summary>
        public static readonly string UpdatesPublicBaseUrl = "TK_UPDATES_PUBLIC_BASE_URL";

        /// <summary>
        /// Secret: PEM private key that signs update manifests (docs/TRELLISKIT.md section 8.1).
        /// </summary>
        public static readonly string UpdatesSigningKey = "TK_UPDATES_SIGNING_KEY";

        /// <summary>
        /// Secret: PEM private key of the standby signing certificate (setting updates.standbySigningKeyPem, Live), so
        /// signing can switch keys without a new binary. Empty until filled out of band.
        /// </summary>
        public static readonly string UpdatesStandbySigningKey = "TK_UPDATES_STANDBY_SIGNING_KEY";

        /// <summary>
        /// RecallDB base URL (setting ai.recallDbEndpoint, Live). Set only when ai.recallDb is configured, to the
        /// service discovery name of the RecallDB service.
        /// </summary>
        public static readonly string RecallDbEndpoint = "TK_RECALLDB_ENDPOINT";

        /// <summary>
        /// RecallDB tenant (setting ai.recallDbTenantId, Live). Set only when ai.recallDb is configured.
        /// </summary>
        public static readonly string RecallDbTenant = "TK_RECALLDB_TENANT";

        /// <summary>
        /// Secret: bearer token of the server's RecallDB credential (setting ai.recallDbToken, Live), generated into
        /// {prefix}/recalldb-token. Injected only when ai.recallDb is configured.
        /// </summary>
        public static readonly string RecallDbToken = "TK_RECALLDB_TOKEN";

        /// <summary>
        /// Live settings admins edit in the console. Each is set only when configured in ApiSettings, because an
        /// environment variable overrides and locks the setting.
        /// </summary>
        public static readonly IReadOnlyList<string> OptionalLiveKeys = new List<string>
        {
            PushEnabled,
            LinkPreviewsEnabled,
            EmailFeedPostsEnabled,
            AccountExportRetentionHours,
            AdditionalTrustedProxies,
            DomainVerificationResolverUrl,
            EmailProvider,
            MailgunDomain,
            MailgunBaseUrl,
            PartioBaseUrl,
            RecallDbEndpoint,
            RecallDbTenant
        };

        /// <summary>
        /// Plain variables that only make sense for one property and are therefore left unset on the shared API
        /// (the backend reads them per property in MultiProperty mode).
        /// </summary>
        public static readonly IReadOnlyList<string> PerPropertyKeys = new List<string>
        {
            "TK_WEB_PUBLIC_BASE_URL",
            "TK_SMTP_FROM_ADDRESS",
            MediaPublicBaseUrl,
            MediaCookieDomain,
            UpdatesPublicBaseUrl
        };
    }
}
