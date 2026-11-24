namespace TrellisKit.Infra.Config
{
    using System;

    /// <summary>
    /// ECS Fargate service, load balancer, and container settings for the backend server.
    /// </summary>
    public class ApiSettings
    {
        private int _ContainerPort = 8000;
        private int _HttpsPort = 443;
        private int _HttpPort = 80;
        private int _TaskCpu = 512;
        private int _TaskMemoryMiB = 1024;
        private int _CollectorCpu = 128;
        private int _CollectorMemoryMiB = 256;
        private int _MinTasks = 1;
        private int _MaxTasks = 2;
        private int _CpuTargetPercent = 60;
        private int _RequestsPerTarget = 1000;
        private int _ScaleInCooldownSeconds = 300;
        private int _ScaleOutCooldownSeconds = 60;
        private int _IdleTimeoutSeconds = 120;
        private int _DeregistrationDelaySeconds = 30;
        private int _StopTimeoutSeconds = 30;
        private string _RealtimePath = "/api/v1/ws";
        private string _ReadinessPath = "/api/v1/health/ready";
        private string _LivenessPath = "/api/v1/health";
        private int _HealthCheckIntervalSeconds = 15;
        private int _HealthCheckTimeoutSeconds = 5;
        private int _HealthyThreshold = 2;
        private int _UnhealthyThreshold = 3;
        private int _HealthCheckGracePeriodSeconds = 120;
        private int _ContainerHealthCheckStartPeriodSeconds = 60;
        private string _ImageTag = "latest";
        private int _MaxImageCount = 30;
        private string _CollectorImage = "public.ecr.aws/aws-observability/aws-otel-collector:v0.50.0";
        private int _OtlpGrpcPort = 4317;
        private int _OtlpHttpPort = 4318;
        private string _TelemetryServiceName = "trelliskit-server";
        private int _SmtpPort = 587;
        private string _SmtpFromLocalPart = "no-reply";
        private string _PropertyDirectory = "/app/property";
        private int? _AccountExportRetentionHours = null;
        private int _CloudFrontOriginHttpsPort = 8443;
        private string _ViewerAddressHeader = "CloudFront-Viewer-Address";
        private string? _DomainVerificationResolverUrl = null;

        /// <summary>
        /// Port the server listens on inside the task (TK_REST_PORT). Default 8000. Minimum 1024, maximum 65535.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int ContainerPort
        {
            get { return _ContainerPort; }
            set { _ContainerPort = Guard.Range(value, 1024, 65535, nameof(ContainerPort)); }
        }

        /// <summary>
        /// HTTPS listener port on the load balancer. Default 443. Minimum 1, maximum 65535.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int HttpsPort
        {
            get { return _HttpsPort; }
            set { _HttpsPort = Guard.Range(value, 1, 65535, nameof(HttpsPort)); }
        }

        /// <summary>
        /// HTTP listener port that redirects to HTTPS. Default 80. Minimum 1, maximum 65535.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int HttpPort
        {
            get { return _HttpPort; }
            set { _HttpPort = Guard.Range(value, 1, 65535, nameof(HttpPort)); }
        }

        /// <summary>
        /// Task CPU units (1024 = 1 vCPU). Must be a Fargate-supported value. Default 512. Minimum 256, maximum 16384.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int TaskCpu
        {
            get { return _TaskCpu; }
            set { _TaskCpu = Guard.Range(value, 256, 16384, nameof(TaskCpu)); }
        }

        /// <summary>
        /// Task memory in MiB. Must pair with <see cref="TaskCpu"/> per Fargate rules. Default 1024. Minimum 512, maximum 122880.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int TaskMemoryMiB
        {
            get { return _TaskMemoryMiB; }
            set { _TaskMemoryMiB = Guard.Range(value, 512, 122880, nameof(TaskMemoryMiB)); }
        }

        /// <summary>
        /// CPU units reserved for the ADOT collector sidecar out of the task total. Default 128. Minimum 64, maximum 1024.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int CollectorCpu
        {
            get { return _CollectorCpu; }
            set { _CollectorCpu = Guard.Range(value, 64, 1024, nameof(CollectorCpu)); }
        }

        /// <summary>
        /// Memory in MiB reserved for the ADOT collector sidecar out of the task total. Default 256. Minimum 128, maximum 2048.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int CollectorMemoryMiB
        {
            get { return _CollectorMemoryMiB; }
            set { _CollectorMemoryMiB = Guard.Range(value, 128, 2048, nameof(CollectorMemoryMiB)); }
        }

        /// <summary>
        /// Minimum (and initial) running tasks. Default 1. Minimum 1, maximum 50.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int MinTasks
        {
            get { return _MinTasks; }
            set { _MinTasks = Guard.Range(value, 1, 50, nameof(MinTasks)); }
        }

        /// <summary>
        /// Maximum running tasks under autoscaling. Default 2. Minimum 1, maximum 50.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int MaxTasks
        {
            get { return _MaxTasks; }
            set { _MaxTasks = Guard.Range(value, 1, 50, nameof(MaxTasks)); }
        }

        /// <summary>
        /// Target average service CPU utilization for scaling. Default 60. Minimum 10, maximum 90.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int CpuTargetPercent
        {
            get { return _CpuTargetPercent; }
            set { _CpuTargetPercent = Guard.Range(value, 10, 90, nameof(CpuTargetPercent)); }
        }

        /// <summary>
        /// Target ALB requests per task per minute for scaling. Default 1000. Minimum 10, maximum 100000.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int RequestsPerTarget
        {
            get { return _RequestsPerTarget; }
            set { _RequestsPerTarget = Guard.Range(value, 10, 100000, nameof(RequestsPerTarget)); }
        }

        /// <summary>
        /// Wait after a scale-in before scaling in again. Default 300. Minimum 0, maximum 3600.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int ScaleInCooldownSeconds
        {
            get { return _ScaleInCooldownSeconds; }
            set { _ScaleInCooldownSeconds = Guard.Range(value, 0, 3600, nameof(ScaleInCooldownSeconds)); }
        }

        /// <summary>
        /// Wait after a scale-out before scaling out again. Default 60. Minimum 0, maximum 3600.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int ScaleOutCooldownSeconds
        {
            get { return _ScaleOutCooldownSeconds; }
            set { _ScaleOutCooldownSeconds = Guard.Range(value, 0, 3600, nameof(ScaleOutCooldownSeconds)); }
        }

        /// <summary>
        /// ALB idle timeout. The realtime gateway's clients send a ping every 25 seconds (realtime.heartbeatSeconds) and
        /// the server's socket keepalive runs every 30 seconds, so 120 seconds keeps quiet WebSocket connections open
        /// while idle HTTP connections close sooner (docs/PHASE5_CONTRACT.md section 17). Must exceed the heartbeat.
        /// Default 120. Minimum 60, maximum 4000.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int IdleTimeoutSeconds
        {
            get { return _IdleTimeoutSeconds; }
            set { _IdleTimeoutSeconds = Guard.Range(value, 60, 4000, nameof(IdleTimeoutSeconds)); }
        }

        /// <summary>
        /// Target group deregistration delay (connection draining) when a task is replaced: the realtime gateway sends
        /// shutdown and closes its sockets with 1012 over up to 20 seconds. Default 30. Minimum 0, maximum 3600.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int DeregistrationDelaySeconds
        {
            get { return _DeregistrationDelaySeconds; }
            set { _DeregistrationDelaySeconds = Guard.Range(value, 0, 3600, nameof(DeregistrationDelaySeconds)); }
        }

        /// <summary>
        /// Server container stop timeout (time between SIGTERM and SIGKILL), long enough for the realtime drain (up to 20
        /// seconds of 1012 closes plus registry cleanup). Default 30. Minimum 30, maximum 120 (the Fargate limit).
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int StopTimeoutSeconds
        {
            get { return _StopTimeoutSeconds; }
            set { _StopTimeoutSeconds = Guard.Range(value, 30, 120, nameof(StopTimeoutSeconds)); }
        }

        /// <summary>
        /// Path of the realtime WebSocket endpoint (GET /api/v1/ws). Both HTTPS listeners carry an explicit rule that
        /// forwards it to the API target group, which has no stickiness (the realtime bus fans events out across tasks).
        /// Default /api/v1/ws.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string RealtimePath
        {
            get { return _RealtimePath; }
            set { _RealtimePath = Guard.NotEmpty(value, nameof(RealtimePath)); }
        }

        /// <summary>
        /// Target group health check path (readiness: database reachable and migrations applied). Default /api/v1/health/ready.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string ReadinessPath
        {
            get { return _ReadinessPath; }
            set { _ReadinessPath = Guard.NotEmpty(value, nameof(ReadinessPath)); }
        }

        /// <summary>
        /// Container health check path (liveness). Default /api/v1/health.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string LivenessPath
        {
            get { return _LivenessPath; }
            set { _LivenessPath = Guard.NotEmpty(value, nameof(LivenessPath)); }
        }

        /// <summary>
        /// Seconds between health checks. Default 15. Minimum 5, maximum 300.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int HealthCheckIntervalSeconds
        {
            get { return _HealthCheckIntervalSeconds; }
            set { _HealthCheckIntervalSeconds = Guard.Range(value, 5, 300, nameof(HealthCheckIntervalSeconds)); }
        }

        /// <summary>
        /// Health check timeout. Must be lower than the interval. Default 5. Minimum 2, maximum 120.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int HealthCheckTimeoutSeconds
        {
            get { return _HealthCheckTimeoutSeconds; }
            set { _HealthCheckTimeoutSeconds = Guard.Range(value, 2, 120, nameof(HealthCheckTimeoutSeconds)); }
        }

        /// <summary>
        /// Consecutive successes before a target is healthy. Default 2. Minimum 2, maximum 10.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int HealthyThreshold
        {
            get { return _HealthyThreshold; }
            set { _HealthyThreshold = Guard.Range(value, 2, 10, nameof(HealthyThreshold)); }
        }

        /// <summary>
        /// Consecutive failures before a target is unhealthy. Default 3. Minimum 2, maximum 10.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int UnhealthyThreshold
        {
            get { return _UnhealthyThreshold; }
            set { _UnhealthyThreshold = Guard.Range(value, 2, 10, nameof(UnhealthyThreshold)); }
        }

        /// <summary>
        /// Time ECS ignores load balancer health checks after a task starts (migrations run at startup). Default 120.
        /// Minimum 0, maximum 3600.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int HealthCheckGracePeriodSeconds
        {
            get { return _HealthCheckGracePeriodSeconds; }
            set { _HealthCheckGracePeriodSeconds = Guard.Range(value, 0, 3600, nameof(HealthCheckGracePeriodSeconds)); }
        }

        /// <summary>
        /// Container health check start period. Default 60. Minimum 0, maximum 300.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int ContainerHealthCheckStartPeriodSeconds
        {
            get { return _ContainerHealthCheckStartPeriodSeconds; }
            set { _ContainerHealthCheckStartPeriodSeconds = Guard.Range(value, 0, 300, nameof(ContainerHealthCheckStartPeriodSeconds)); }
        }

        /// <summary>
        /// Server image tag in ECR. Default latest. Override with the imageTag context value to pin a commit SHA.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string ImageTag
        {
            get { return _ImageTag; }
            set { _ImageTag = Guard.NotEmpty(value, nameof(ImageTag)); }
        }

        /// <summary>
        /// Images kept in the ECR repository; older ones expire. Default 30. Minimum 1, maximum 1000.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int MaxImageCount
        {
            get { return _MaxImageCount; }
            set { _MaxImageCount = Guard.Range(value, 1, 1000, nameof(MaxImageCount)); }
        }

        /// <summary>
        /// AWS Distro for OpenTelemetry collector image, pinned. Default public.ecr.aws/aws-observability/aws-otel-collector:v0.50.0.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string CollectorImage
        {
            get { return _CollectorImage; }
            set { _CollectorImage = Guard.NotEmpty(value, nameof(CollectorImage)); }
        }

        /// <summary>
        /// OTLP gRPC port of the sidecar; the server exports here (TK_OTLP_ENDPOINT). Default 4317. Minimum 1024, maximum 65535.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int OtlpGrpcPort
        {
            get { return _OtlpGrpcPort; }
            set { _OtlpGrpcPort = Guard.Range(value, 1024, 65535, nameof(OtlpGrpcPort)); }
        }

        /// <summary>
        /// OTLP HTTP port of the sidecar. Default 4318. Minimum 1024, maximum 65535.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int OtlpHttpPort
        {
            get { return _OtlpHttpPort; }
            set { _OtlpHttpPort = Guard.Range(value, 1024, 65535, nameof(OtlpHttpPort)); }
        }

        /// <summary>
        /// service.name on all telemetry (TK_TELEMETRY_SERVICE_NAME). Default trelliskit-server.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string TelemetryServiceName
        {
            get { return _TelemetryServiceName; }
            set { _TelemetryServiceName = Guard.NotEmpty(value, nameof(TelemetryServiceName)); }
        }

        /// <summary>
        /// SES SMTP port with STARTTLS (TK_SMTP_PORT). Default 587. Minimum 1, maximum 65535.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int SmtpPort
        {
            get { return _SmtpPort; }
            set { _SmtpPort = Guard.Range(value, 1, 65535, nameof(SmtpPort)); }
        }

        /// <summary>
        /// Directory of the property inside the server image (TK_PROPERTY_DIR). The image build copies the property
        /// folder here. Default /app/property.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string PropertyDirectory
        {
            get { return _PropertyDirectory; }
            set { _PropertyDirectory = Guard.NotEmpty(value, nameof(PropertyDirectory)); }
        }

        /// <summary>
        /// Local part of the sender address on the primary domain (TK_SMTP_FROM_ADDRESS). Set from the local part of the property's contact.emailFromAddress. Default no-reply.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string SmtpFromLocalPart
        {
            get { return _SmtpFromLocalPart; }
            set { _SmtpFromLocalPart = Guard.NotEmpty(value, nameof(SmtpFromLocalPart)); }
        }

        /// <summary>
        /// Enable ALB deletion protection. Default false.
        /// </summary>
        public bool LoadBalancerDeletionProtection { get; set; } = false;

        /// <summary>
        /// Resolve the property from X-Forwarded-Host (TK_TRUST_FORWARDED_HOST, a Restart setting, so it is always set).
        /// The website distribution overwrites X-Forwarded-Host with the viewer's host; requests that reach the load
        /// balancer directly carry the API host in Host. Default true.
        /// </summary>
        public bool TrustForwardedHost { get; set; } = true;

        /// <summary>
        /// Send push notifications through Expo (TK_PUSH_ENABLED). Default null: the variable is not set, so the
        /// backend default and the admin console apply. A value sets the variable, which overrides and locks the
        /// live setting.
        /// </summary>
        public bool? PushEnabled { get; set; } = null;

        /// <summary>
        /// Build link preview cards for the first link in posts and comments (TK_LINK_PREVIEWS_ENABLED). Default null:
        /// not set, so the backend default and the admin console apply. A value overrides and locks the live setting.
        /// </summary>
        public bool? LinkPreviewsEnabled { get; set; } = null;

        /// <summary>
        /// Email members about new feed posts, opt-out per member (TK_EMAIL_FEED_POSTS_ENABLED). Default null: not set,
        /// so the backend default and the admin console apply. A value overrides and locks the live setting.
        /// </summary>
        public bool? EmailFeedPostsEnabled { get; set; } = null;

        /// <summary>
        /// Hours a finished account data export stays downloadable (TK_ACCOUNT_EXPORT_RETENTION_HOURS). Default null:
        /// not set, so the backend default and the admin console apply. A value overrides and locks the live setting.
        /// Minimum 1, maximum 168 (the backend's range) when set.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int? AccountExportRetentionHours
        {
            get { return _AccountExportRetentionHours; }
            set { _AccountExportRetentionHours = value == null ? null : Guard.Range(value.Value, 1, 168, nameof(AccountExportRetentionHours)); }
        }

        /// <summary>
        /// HTTPS listener port on the load balancer that only CloudFront reaches: the website distributions' API origin
        /// connects here, and the load balancer's CloudFront security group admits this port only from the CloudFront
        /// origin-facing prefix list. The apps keep using <see cref="HttpsPort"/>, open to the internet. Must differ
        /// from <see cref="HttpsPort"/> and <see cref="HttpPort"/>. Default 8443. Minimum 1, maximum 65535.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int CloudFrontOriginHttpsPort
        {
            get { return _CloudFrontOriginHttpsPort; }
            set { _CloudFrontOriginHttpsPort = Guard.Range(value, 1, 65535, nameof(CloudFrontOriginHttpsPort)); }
        }

        /// <summary>
        /// Header CloudFront sets to the viewer's address (the backend's rest.viewerAddressHeader default). The website
        /// API behaviors forward it to the CloudFront origin listener, and the public HTTPS listener rejects any request
        /// that carries it (status 400), so a client that calls the load balancer directly cannot choose its own address.
        /// Not passed to the server (a live setting whose backend default matches). Default CloudFront-Viewer-Address.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string ViewerAddressHeader
        {
            get { return _ViewerAddressHeader; }
            set { _ViewerAddressHeader = Guard.NotEmpty(value, nameof(ViewerAddressHeader)).Trim(); }
        }

        /// <summary>
        /// DNS-over-HTTPS JSON endpoint used to verify property domains (TK_DOMAIN_VERIFICATION_RESOLVER_URL, setting
        /// deployment.domainVerificationResolverUrl, live). Default null: not set, so the backend default
        /// (https://cloudflare-dns.com/dns-query) and the admin console apply. A value overrides and locks the setting.
        /// The tasks reach it over outbound HTTPS through the NAT gateways.
        /// </summary>
        /// <exception cref="ArgumentException">The value is not an absolute https URL.</exception>
        public string? DomainVerificationResolverUrl
        {
            get
            {
                return _DomainVerificationResolverUrl;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value))
                {
                    _DomainVerificationResolverUrl = null;
                    return;
                }

                string text = value.Trim();
                if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps || String.IsNullOrEmpty(uri.Host))
                {
                    throw new ArgumentException("DomainVerificationResolverUrl must be an absolute https URL, got '" + text + "'.", nameof(DomainVerificationResolverUrl));
                }

                _DomainVerificationResolverUrl = text;
            }
        }
    }
}
