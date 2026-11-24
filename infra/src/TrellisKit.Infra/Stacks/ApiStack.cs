namespace TrellisKit.Infra.Stacks
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using System.Text.Json;
    using Amazon.CDK;
    using Amazon.CDK.AWS.ApplicationAutoScaling;
    using Amazon.CDK.AWS.CertificateManager;
    using Amazon.CDK.AWS.EC2;
    using Amazon.CDK.AWS.ECS;
    using Amazon.CDK.AWS.ElasticLoadBalancingV2;
    using Amazon.CDK.AWS.IAM;
    using Amazon.CDK.AWS.Route53;
    using Amazon.CDK.AWS.Route53.Targets;
    using Amazon.CDK.AWS.SecretsManager;
    using TrellisKit.Infra.Config;
    using TrellisKit.Infra.Nag;
    using Constructs;
    using EcsHealthCheck = Amazon.CDK.AWS.ECS.HealthCheck;
    using EcsProtocol = Amazon.CDK.AWS.ECS.Protocol;
    using EcsSecret = Amazon.CDK.AWS.ECS.Secret;
    using HealthCheck = Amazon.CDK.AWS.ElasticLoadBalancingV2.HealthCheck;
    using Protocol = Amazon.CDK.AWS.ElasticLoadBalancingV2.Protocol;
    using SecretsManagerSecret = Amazon.CDK.AWS.SecretsManager.Secret;

    /// <summary>
    /// The backend: ECS Fargate service (server plus ADOT collector sidecar) behind an internet-facing ALB on
    /// the property's API host (every property's API host in the shared topology, one certificate each) with HTTPS,
    /// HTTP to HTTPS redirect, a WebSocket-friendly idle timeout, readiness health checks, and CPU and request-count
    /// autoscaling. A second HTTPS listener serves the website distributions only (CloudFront origin-facing ranges).
    /// Secrets reach the container through ECS secrets only. Outbound HTTPS (Mailgun, the DNS-over-HTTPS resolver,
    /// model providers, stores, payment and sign-in providers) leaves through the NAT gateways.
    /// </summary>
    public class ApiStack : Stack
    {
        /// <summary>
        /// Name of the server container in the task definition.
        /// </summary>
        public static readonly string ServerContainerName = "server";

        /// <summary>
        /// Name of the collector container in the task definition.
        /// </summary>
        public static readonly string CollectorContainerName = "otel-collector";

        /// <summary>
        /// Name of the database bootstrap container: runs to completion before the server starts in every task.
        /// </summary>
        public static readonly string BootstrapContainerName = "db-bootstrap";

        /// <summary>
        /// SQL the bootstrap container runs as the master user (idempotent; psql variables app_user, app_password, db,
        /// and search_schema). It creates the non-superuser application role when missing, sets its password from the
        /// application secret, enables pgvector, and grants least-privilege rights (docs/TRELLISKIT.md section 13):
        /// CONNECT and TEMPORARY on the database, USAGE and CREATE on schema public, the Verbex search schema
        /// (DatabaseSettings.SearchSchema) owned by the role, and CREATE on the database. CREATE on the database is
        /// needed only because the server runs CREATE SCHEMA IF NOT EXISTS for the search schema on start and PostgreSQL
        /// checks that privilege even when the schema exists; it allows creating schemas, never databases or roles. ALTER ROLE
        /// sets only LOGIN and PASSWORD because RDS's master user may not name the SUPERUSER, REPLICATION, or BYPASSRLS
        /// attributes on an existing role.
        /// </summary>
        public static readonly string BootstrapSql = String.Join("\n", new string[]
        {
            "SELECT format('CREATE ROLE %I LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE', :'app_user') WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'app_user') \\gexec",
            "ALTER ROLE :\"app_user\" WITH LOGIN PASSWORD :'app_password';",
            "REVOKE ALL ON DATABASE :\"db\" FROM PUBLIC;",
            "GRANT CONNECT, TEMPORARY, CREATE ON DATABASE :\"db\" TO :\"app_user\";",
            "CREATE EXTENSION IF NOT EXISTS vector SCHEMA public;",
            "REVOKE CREATE ON SCHEMA public FROM PUBLIC;",
            "GRANT USAGE, CREATE ON SCHEMA public TO :\"app_user\";",
            "CREATE SCHEMA IF NOT EXISTS :\"search_schema\" AUTHORIZATION :\"app_user\";",
            "GRANT USAGE, CREATE ON SCHEMA :\"search_schema\" TO :\"app_user\";"
        }) + "\n";

        /// <summary>
        /// SQL the bootstrap container appends when RecallDB is enabled (idempotent; psql variables rd_user,
        /// rd_password, and rd_db). It creates RecallDB's role and database when missing, sets the role's password from
        /// its secret, and grants CONNECT and TEMPORARY on that database and USAGE and CREATE on its schema public (RecallDB
        /// migrates its own tables there). The database stays owned by the master user. pgvector and pg_trgm are created
        /// here because RecallDB runs CREATE EXTENSION IF NOT EXISTS for both, which needs no privilege once they exist.
        /// It runs last because it switches the connection to RecallDB's database.
        /// </summary>
        public static readonly string RecallDbBootstrapSql = String.Join("\n", new string[]
        {
            "SELECT format('CREATE ROLE %I LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE', :'rd_user') WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'rd_user') \\gexec",
            "ALTER ROLE :\"rd_user\" WITH LOGIN PASSWORD :'rd_password';",
            "SELECT format('CREATE DATABASE %I', :'rd_db') WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = :'rd_db') \\gexec",
            "REVOKE ALL ON DATABASE :\"rd_db\" FROM PUBLIC;",
            "GRANT CONNECT, TEMPORARY ON DATABASE :\"rd_db\" TO :\"rd_user\";",
            "\\connect :\"rd_db\"",
            "CREATE EXTENSION IF NOT EXISTS vector SCHEMA public;",
            "CREATE EXTENSION IF NOT EXISTS pg_trgm SCHEMA public;",
            "REVOKE CREATE ON SCHEMA public FROM PUBLIC;",
            "GRANT USAGE, CREATE ON SCHEMA public TO :\"rd_user\";"
        }) + "\n";

        /// <summary>
        /// The ECS cluster.
        /// </summary>
        public Cluster Cluster { get; }

        /// <summary>
        /// The Fargate service.
        /// </summary>
        public FargateService Service { get; }

        /// <summary>
        /// The task definition.
        /// </summary>
        public FargateTaskDefinition TaskDefinition { get; }

        /// <summary>
        /// The internet-facing load balancer.
        /// </summary>
        public ApplicationLoadBalancer LoadBalancer { get; }

        /// <summary>
        /// Target group of the service.
        /// </summary>
        public ApplicationTargetGroup TargetGroup { get; }

        /// <summary>
        /// Application secret (third-party keys, admin token, token encryption key).
        /// </summary>
        public SecretsManagerSecret ServerSecret { get; }

        /// <summary>
        /// Private key that signs live update manifests (filled manually), injected as TK_UPDATES_SIGNING_KEY.
        /// </summary>
        public SecretsManagerSecret UpdatesSigningKeySecret { get; }

        /// <summary>
        /// Private key of the standby updates signing certificate (JSON key TK_UPDATES_STANDBY_SIGNING_KEY, created
        /// empty and filled out of band), injected as TK_UPDATES_STANDBY_SIGNING_KEY.
        /// </summary>
        public SecretsManagerSecret UpdatesStandbySigningKeySecret { get; }

        /// <summary>
        /// The RecallDB service, or null unless ai.recallDb is configured.
        /// </summary>
        public RecallDbService? RecallDb { get; }

        /// <summary>
        /// Push credentials (TK_EXPO_ACCESS_TOKEN), created with an empty value and filled manually. Kept apart from
        /// <see cref="ServerSecret"/> so adding it never regenerates that secret's template.
        /// </summary>
        public SecretsManagerSecret PushSecret { get; }

        /// <summary>
        /// Platform credentials (TK_PLATFORM_BOOTSTRAP_ADMIN_PASSWORD) of a shared deployment, created with an empty
        /// value and filled manually; null in the per-property topology.
        /// </summary>
        public SecretsManagerSecret? PlatformSecret { get; }

        /// <summary>
        /// Deployment Mailgun credentials (TK_MAILGUN_API_KEY), created with an empty value and filled manually; null
        /// unless the deployment's mail settings use Mailgun.
        /// </summary>
        public SecretsManagerSecret? MailgunSecret { get; }

        /// <summary>
        /// Partio credentials (TK_PARTIO_API_KEY), created with an empty value and filled manually; null unless
        /// partio.baseUrl is configured.
        /// </summary>
        public SecretsManagerSecret? PartioSecret { get; }

        /// <summary>
        /// Amazon Location Service resources of each property that opts in (location section), in host order.
        /// </summary>
        public List<LocationResources> Location { get; } = new List<LocationResources>();

        /// <summary>
        /// The engine secrets key (TK_SECRETS_ENCRYPTION_KEY and TK_SECRETS_PREVIOUS_ENCRYPTION_KEYS), generated once.
        /// </summary>
        public SecretsKeySecret SecretsKey { get; }

        /// <summary>
        /// The public HTTPS listener (apps, webhooks, platform hosts).
        /// </summary>
        public ApplicationListener HttpsListener { get; }

        /// <summary>
        /// The HTTPS listener that only CloudFront reaches (website API behaviors).
        /// </summary>
        public ApplicationListener CloudFrontListener { get; }

        /// <summary>
        /// Instantiate the stack.
        /// </summary>
        /// <param name="scope">Parent construct.</param>
        /// <param name="id">Construct id.</param>
        /// <param name="config">Environment configuration.</param>
        /// <param name="dependencies">Stacks this stack reads from.</param>
        /// <param name="props">Stack properties.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public ApiStack(Construct scope, string id, EnvironmentConfig config, ApiStackDependencies dependencies, IStackProps props) : base(scope, id, props)
        {
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(dependencies);
            if (dependencies.Hosts.Count == 0) throw new ArgumentException("The API needs at least one host binding.", nameof(dependencies));

            ApiSettings api = config.Api;
            NetworkStack network = dependencies.Network;

            ServerSecret = new SecretsManagerSecret(this, "ServerSecret", new SecretProps
            {
                SecretName = config.ResourcePath("server"),
                Description = config.DisplayName + " server secrets and third-party identifiers. Fill the empty keys; see infra/README.md.",
                GenerateSecretString = new SecretStringGenerator
                {
                    SecretStringTemplate = BuildSecretTemplate(),
                    GenerateStringKey = ServerSecretKeys.GeneratedKey,
                    PasswordLength = 48,
                    ExcludePunctuation = true
                },
                RemovalPolicy = config.StatefulRemovalPolicy
            });
            NagHelper.Suppress(ServerSecret, "AwsSolutions-SMG4",
                "Values are third-party credentials (Stripe, Apple, Google, Facebook, SES SMTP) rotated at the provider, plus the admin token and token encryption key whose rotation signs users out; all are rotated manually.");

            UpdatesSigningKeySecret = new SecretsManagerSecret(this, "UpdatesSigningKey", new SecretProps
            {
                SecretName = config.ResourcePath("updates-signing-key"),
                Description = "PEM private key that signs live update manifests (expo-updates code signing). Replace the generated placeholder.",
                GenerateSecretString = new SecretStringGenerator { PasswordLength = 32, ExcludePunctuation = true },
                RemovalPolicy = config.StatefulRemovalPolicy
            });
            NagHelper.Suppress(UpdatesSigningKeySecret, "AwsSolutions-SMG4",
                "The matching certificate is embedded in every shipped app binary, so rotating the key needs a store release; it is a planned manual procedure (see infra/README.md).");

            // A separate JSON secret created with an empty value: the backend treats an empty key as absent, and the
            // existing active key secret is left as it is so a filled value is never regenerated by a deploy.
            UpdatesStandbySigningKeySecret = new SecretsManagerSecret(this, "UpdatesStandbySigningKey", new SecretProps
            {
                SecretName = config.ResourcePath("updates-standby-signing-key"),
                Description = "PEM private key of the standby live updates signing certificate. Set " + ServerEnvironmentKeys.UpdatesStandbySigningKey + " out of band; see infra/README.md.",
                SecretObjectValue = new Dictionary<string, SecretValue>
                {
                    [ServerEnvironmentKeys.UpdatesStandbySigningKey] = SecretValue.UnsafePlainText(String.Empty)
                },
                RemovalPolicy = config.StatefulRemovalPolicy
            });
            NagHelper.Suppress(UpdatesStandbySigningKeySecret, "AwsSolutions-SMG4",
                "The standby certificate is embedded in every shipped app binary, so the key is replaced only with a store release; it is a planned manual procedure (see infra/README.md).");

            PushSecret = new SecretsManagerSecret(this, "PushSecret", new SecretProps
            {
                SecretName = config.ResourcePath("push"),
                Description = config.DisplayName + " push credentials. Set " + ServerEnvironmentKeys.ExpoAccessToken + " to the Expo access token, or leave it empty for unauthenticated Expo pushes.",
                SecretObjectValue = new Dictionary<string, SecretValue>
                {
                    [ServerEnvironmentKeys.ExpoAccessToken] = SecretValue.UnsafePlainText(String.Empty)
                },
                RemovalPolicy = config.StatefulRemovalPolicy
            });
            NagHelper.Suppress(PushSecret, "AwsSolutions-SMG4",
                "The Expo access token is issued and revoked in the Expo dashboard; it is rotated manually (see infra/README.md).");

            if (config.IsShared)
            {
                PlatformSecret = new SecretsManagerSecret(this, "PlatformSecret", new SecretProps
                {
                    SecretName = config.ResourcePath("platform"),
                    Description = config.DisplayName + " platform credentials. Set " + ServerSecretKeys.PlatformBootstrapAdminPassword + " to the first platform administrator's initial password, or leave it empty to have the server log an invitation token.",
                    SecretObjectValue = new Dictionary<string, SecretValue>
                    {
                        [ServerSecretKeys.PlatformBootstrapAdminPassword] = SecretValue.UnsafePlainText(String.Empty)
                    },
                    RemovalPolicy = config.StatefulRemovalPolicy
                });
                NagHelper.Suppress(PlatformSecret, "AwsSolutions-SMG4",
                    "The bootstrap password is used once, when the first platform administrator is created; the administrator then changes it in the console.");
            }

            if (config.Mail.UsesMailgun)
            {
                MailgunSecret = new SecretsManagerSecret(this, "MailgunSecret", new SecretProps
                {
                    SecretName = config.ResourcePath("mailgun"),
                    Description = config.DisplayName + " deployment mail. Set " + ServerEnvironmentKeys.MailgunApiKey + " to the Mailgun API key of the deployment's sending domain.",
                    SecretObjectValue = new Dictionary<string, SecretValue>
                    {
                        [ServerEnvironmentKeys.MailgunApiKey] = SecretValue.UnsafePlainText(String.Empty)
                    },
                    RemovalPolicy = config.StatefulRemovalPolicy
                });
                NagHelper.Suppress(MailgunSecret, "AwsSolutions-SMG4",
                    "The Mailgun API key is issued and revoked in the Mailgun dashboard; it is rotated manually (see infra/README.md).");
            }

            if (config.Partio.Enabled)
            {
                PartioSecret = new SecretsManagerSecret(this, "PartioSecret", new SecretProps
                {
                    SecretName = config.ResourcePath("partio"),
                    Description = config.DisplayName + " Partio document text service. Set " + ServerEnvironmentKeys.PartioApiKey + " to the Partio bearer token.",
                    SecretObjectValue = new Dictionary<string, SecretValue>
                    {
                        [ServerEnvironmentKeys.PartioApiKey] = SecretValue.UnsafePlainText(String.Empty)
                    },
                    RemovalPolicy = config.StatefulRemovalPolicy
                });
                NagHelper.Suppress(PartioSecret, "AwsSolutions-SMG4",
                    "The Partio API key is issued and revoked by the Partio operator; it is rotated manually (see infra/README.md).");
            }

            SecretsKey = new SecretsKeySecret(this, "SecretsKey", config);

            Cluster = new Cluster(this, "Cluster", new ClusterProps
            {
                ClusterName = config.ResourceName("cluster"),
                Vpc = network.Vpc,
                ContainerInsightsV2 = ContainerInsights.ENABLED
            });

            TaskDefinition = new FargateTaskDefinition(this, "TaskDefinition", new FargateTaskDefinitionProps
            {
                Family = config.ResourceName("server"),
                Cpu = api.TaskCpu,
                MemoryLimitMiB = api.TaskMemoryMiB,
                RuntimePlatform = new RuntimePlatform
                {
                    CpuArchitecture = CpuArchitecture.X86_64,
                    OperatingSystemFamily = OperatingSystemFamily.LINUX
                }
            });

            AddTaskRolePermissions(config, dependencies);

            // Opt-in Amazon Location resources, one set per property that asks for them (zero cost otherwise).
            foreach (ApiHostBinding host in dependencies.Hosts)
            {
                if (!host.Property.Location.AnyEnabled) continue;
                Location.Add(new LocationResources(this, "Location" + host.ConstructSuffix, host.Property, TaskDefinition.TaskRole, host.ConstructSuffix));
            }

            ContainerDefinition collector = TaskDefinition.AddContainer(CollectorContainerName, new ContainerDefinitionOptions
            {
                ContainerName = CollectorContainerName,
                Image = ContainerImage.FromRegistry(api.CollectorImage),
                Essential = false,
                Cpu = api.CollectorCpu,
                MemoryReservationMiB = api.CollectorMemoryMiB,
                Environment = new Dictionary<string, string>
                {
                    ["AOT_CONFIG_CONTENT"] = BuildCollectorConfig(config, dependencies)
                },
                Logging = LogDriver.AwsLogs(new AwsLogDriverProps
                {
                    LogGroup = dependencies.Observability.CollectorLogGroup,
                    StreamPrefix = CollectorContainerName
                }),
                HealthCheck = new EcsHealthCheck
                {
                    Command = new string[] { "CMD", "/healthcheck" },
                    Interval = Duration.Seconds(api.HealthCheckIntervalSeconds),
                    Timeout = Duration.Seconds(api.HealthCheckTimeoutSeconds),
                    Retries = api.UnhealthyThreshold,
                    StartPeriod = Duration.Seconds(api.ContainerHealthCheckStartPeriodSeconds)
                }
            });

            if (config.Ai.RecallDbEnabled) RecallDb = new RecallDbService(this, "RecallDb", config, Cluster, dependencies);

            ContainerDefinition bootstrap = AddBootstrapContainer(config, dependencies);

            ContainerDefinition server = TaskDefinition.AddContainer(ServerContainerName, new ContainerDefinitionOptions
            {
                ContainerName = ServerContainerName,
                Image = ContainerImage.FromEcrRepository(dependencies.Registry.Repository, api.ImageTag),
                Essential = true,
                Environment = BuildServerEnvironment(config, dependencies, RecallDb),
                Secrets = BuildServerSecrets(dependencies),
                PortMappings = new IPortMapping[]
                {
                    new PortMapping { ContainerPort = api.ContainerPort, Protocol = EcsProtocol.TCP, Name = "http" }
                },
                Logging = LogDriver.AwsLogs(new AwsLogDriverProps
                {
                    LogGroup = dependencies.Observability.ServerLogGroup,
                    StreamPrefix = ServerContainerName
                }),
                HealthCheck = new EcsHealthCheck
                {
                    Command = new string[]
                    {
                        "CMD-SHELL",
                        "curl -fsS http://127.0.0.1:" + api.ContainerPort.ToString(CultureInfo.InvariantCulture) + api.LivenessPath + " || exit 1"
                    },
                    Interval = Duration.Seconds(api.HealthCheckIntervalSeconds),
                    Timeout = Duration.Seconds(api.HealthCheckTimeoutSeconds),
                    Retries = api.UnhealthyThreshold,
                    StartPeriod = Duration.Seconds(api.ContainerHealthCheckStartPeriodSeconds)
                },
                // Time between SIGTERM and SIGKILL: covers the realtime drain (shutdown frame, 1012 closes over up to
                // 20 seconds, registry cleanup) that starts once the load balancer deregisters the task.
                StopTimeout = Duration.Seconds(api.StopTimeoutSeconds)
            });
            server.AddContainerDependencies(new ContainerDependency
            {
                Container = collector,
                Condition = ContainerDependencyCondition.START
            });
            // The server starts only after the role, grants, and pgvector exist.
            server.AddContainerDependencies(new ContainerDependency
            {
                Container = bootstrap,
                Condition = ContainerDependencyCondition.SUCCESS
            });

            Service = new FargateService(this, "Service", new FargateServiceProps
            {
                ServiceName = config.ResourceName("server"),
                Cluster = Cluster,
                TaskDefinition = TaskDefinition,
                DesiredCount = api.MinTasks,
                MinHealthyPercent = 100,
                MaxHealthyPercent = 200,
                AssignPublicIp = false,
                VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_WITH_EGRESS },
                SecurityGroups = new ISecurityGroup[] { network.ServiceSecurityGroup },
                CircuitBreaker = new DeploymentCircuitBreaker { Enable = true, Rollback = true },
                HealthCheckGracePeriod = Duration.Seconds(api.HealthCheckGracePeriodSeconds),
                PlatformVersion = FargatePlatformVersion.LATEST,
                EnableExecuteCommand = false,
                PropagateTags = PropagatedTagSource.SERVICE
            });
            // No task starts before the secrets key holds a value.
            Service.Node.AddDependency(SecretsKey.Initializer);
            // RecallDB starts after the API service is stable, so db-bootstrap has created its database and role.
            if (RecallDb != null) RecallDb.Service.Node.AddDependency(Service);

            LoadBalancer = new ApplicationLoadBalancer(this, "LoadBalancer", new ApplicationLoadBalancerProps
            {
                LoadBalancerName = config.ResourceName("api"),
                Vpc = network.Vpc,
                InternetFacing = true,
                VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PUBLIC },
                SecurityGroup = network.LoadBalancerSecurityGroup,
                IdleTimeout = Duration.Seconds(api.IdleTimeoutSeconds),
                DropInvalidHeaderFields = true,
                DeletionProtection = api.LoadBalancerDeletionProtection,
                Http2Enabled = true,
                // Pass Host, X-Forwarded-Host, X-TK-Property, and X-TK-App through unchanged and append the peer to
                // X-Forwarded-For, which the server trusts only from the VPC (TK_TRUSTED_PROXIES).
                PreserveHostHeader = true,
                XffHeaderProcessingMode = XffHeaderProcessingMode.APPEND
            });
            LoadBalancer.AddSecurityGroup(network.CloudFrontOriginSecurityGroup);
            LoadBalancer.LogAccessLogs(dependencies.Storage.AccessLogBucket, "alb/api");

            TargetGroup = new ApplicationTargetGroup(this, "TargetGroup", new ApplicationTargetGroupProps
            {
                Vpc = network.Vpc,
                TargetType = TargetType.IP,
                Port = api.ContainerPort,
                Protocol = ApplicationProtocol.HTTP,
                DeregistrationDelay = Duration.Seconds(api.DeregistrationDelaySeconds),
                HealthCheck = new HealthCheck
                {
                    Path = api.ReadinessPath,
                    Protocol = Protocol.HTTP,
                    HealthyHttpCodes = "200",
                    Interval = Duration.Seconds(api.HealthCheckIntervalSeconds),
                    Timeout = Duration.Seconds(api.HealthCheckTimeoutSeconds),
                    HealthyThresholdCount = api.HealthyThreshold,
                    UnhealthyThresholdCount = api.UnhealthyThreshold
                }
            });
            Service.AttachToApplicationTargetGroup(TargetGroup);

            List<ICertificate> platformCertificates = new List<ICertificate>();
            foreach (PlatformHostBinding host in dependencies.PlatformHosts)
            {
                if (host.Certificate != null)
                {
                    platformCertificates.Add(host.Certificate);
                    continue;
                }

                platformCertificates.Add(new Certificate(this, "PlatformCertificate" + host.ConstructSuffix, new CertificateProps
                {
                    DomainName = host.Host,
                    Validation = CertificateValidation.FromDns(host.Dns.PrimaryZone!)
                }));
            }

            // Every path, including /api/v1/webhooks/* (Apple, Google, Stripe, Mailgun, MediaConvert), goes to the server.
            HttpsListener = LoadBalancer.AddListener("Https", new BaseApplicationListenerProps
            {
                Port = api.HttpsPort,
                Protocol = ApplicationProtocol.HTTPS,
                Certificates = ListenerCertificates(dependencies, platformCertificates),
                SslPolicy = SslPolicy.RECOMMENDED_TLS,
                Open = false,
                DefaultTargetGroups = new IApplicationTargetGroup[] { TargetGroup }
            });

            // The server trusts the load balancer (the VPC) as a proxy, so it would believe a viewer address header on
            // any request. Only CloudFront may send one: on the public listener a request that carries it is refused.
            HttpsListener.AddAction("RejectViewerAddress", new AddApplicationActionProps
            {
                Priority = 1,
                Conditions = new ListenerCondition[] { ListenerCondition.HttpHeader(api.ViewerAddressHeader, new string[] { "*" }) },
                Action = ListenerAction.FixedResponse(400, new FixedResponseOptions
                {
                    ContentType = "text/plain",
                    MessageBody = api.ViewerAddressHeader + " is accepted only from the website distribution."
                })
            });

            // The website distributions' API origin. Its security group admits only CloudFront's origin-facing ranges,
            // so CloudFront-Viewer-Address here always comes from CloudFront. API host certificates only (SNI is the
            // origin domain, the property's API host).
            CloudFrontListener = LoadBalancer.AddListener("CloudFrontHttps", new BaseApplicationListenerProps
            {
                Port = api.CloudFrontOriginHttpsPort,
                Protocol = ApplicationProtocol.HTTPS,
                Certificates = ListenerCertificates(dependencies, new List<ICertificate>()),
                SslPolicy = SslPolicy.RECOMMENDED_TLS,
                Open = false,
                DefaultTargetGroups = new IApplicationTargetGroup[] { TargetGroup }
            });

            // WebSockets (GET /api/v1/ws) upgrade on either HTTPS listener and go to the same target group, which has no
            // stickiness: the realtime bus fans events out across tasks. The explicit rule keeps the route pinned even
            // if the default action changes.
            HttpsListener.AddAction("Realtime", new AddApplicationActionProps
            {
                Priority = 2,
                Conditions = new ListenerCondition[] { ListenerCondition.PathPatterns(new string[] { api.RealtimePath }) },
                Action = ListenerAction.Forward(new IApplicationTargetGroup[] { TargetGroup })
            });
            CloudFrontListener.AddAction("Realtime", new AddApplicationActionProps
            {
                Priority = 2,
                Conditions = new ListenerCondition[] { ListenerCondition.PathPatterns(new string[] { api.RealtimePath }) },
                Action = ListenerAction.Forward(new IApplicationTargetGroup[] { TargetGroup })
            });

            LoadBalancer.AddListener("Http", new BaseApplicationListenerProps
            {
                Port = api.HttpPort,
                Protocol = ApplicationProtocol.HTTP,
                Open = false,
                DefaultAction = ListenerAction.Redirect(new RedirectOptions
                {
                    Protocol = "HTTPS",
                    Port = api.HttpsPort.ToString(CultureInfo.InvariantCulture),
                    Permanent = true
                })
            });

            ScalableTaskCount scaling = Service.AutoScaleTaskCount(new EnableScalingProps
            {
                MinCapacity = api.MinTasks,
                MaxCapacity = api.MaxTasks
            });
            scaling.ScaleOnCpuUtilization("CpuScaling", new CpuUtilizationScalingProps
            {
                TargetUtilizationPercent = api.CpuTargetPercent,
                ScaleInCooldown = Duration.Seconds(api.ScaleInCooldownSeconds),
                ScaleOutCooldown = Duration.Seconds(api.ScaleOutCooldownSeconds)
            });
            scaling.ScaleOnRequestCount("RequestScaling", new RequestCountScalingProps
            {
                RequestsPerTarget = api.RequestsPerTarget,
                TargetGroup = TargetGroup,
                ScaleInCooldown = Duration.Seconds(api.ScaleInCooldownSeconds),
                ScaleOutCooldown = Duration.Seconds(api.ScaleOutCooldownSeconds)
            });
            scaling.Node.AddDependency(HttpsListener);
            scaling.Node.AddDependency(CloudFrontListener);

            // Certificates whose DNS is external must be issued before the listeners use them.
            List<ICertificate> pending = new List<ICertificate>();
            List<string> hints = new List<string>();
            foreach (ApiHostBinding host in dependencies.Hosts)
            {
                if (host.Dns.IsExternal)
                {
                    DnsRecordOutputs.AddTarget(this, "Api" + host.ConstructSuffix, host.Property, host.Property.Domains.ApiOriginHost,
                        LoadBalancer.LoadBalancerDnsName, "API load balancer (apps, webhooks, and the website's API origin)");
                    pending.Add(host.Dns.ApiCertificate);
                    if (!hints.Contains(host.Dns.PendingHint)) hints.Add(host.Dns.PendingHint);
                    continue;
                }

                new ARecord(this, "ApiAliasA" + host.ConstructSuffix, new ARecordProps
                {
                    Zone = host.Dns.PrimaryZone!,
                    RecordName = host.Property.Domains.ApiOriginHost,
                    Target = RecordTarget.FromAlias(new LoadBalancerTarget(LoadBalancer))
                });
            }

            foreach (PlatformHostBinding host in dependencies.PlatformHosts)
            {
                if (host.Dns.IsExternal)
                {
                    DnsRecordOutputs.Add(this, "Platform" + host.ConstructSuffix, host.Host, "CNAME", LoadBalancer.LoadBalancerDnsName,
                        "Platform host on the API load balancer.");
                    if (host.Certificate != null) pending.Add(host.Certificate);
                    if (!hints.Contains(host.Dns.PendingHint)) hints.Add(host.Dns.PendingHint);
                    continue;
                }

                new ARecord(this, "PlatformAliasA" + host.ConstructSuffix, new ARecordProps
                {
                    Zone = host.Dns.PrimaryZone!,
                    RecordName = host.Host,
                    Target = RecordTarget.FromAlias(new LoadBalancerTarget(LoadBalancer))
                });
            }

            ApplyNagSuppressions();

            new CfnOutput(this, "ApiUrl", new CfnOutputProps { Value = config.Domains.ApiUrl });
            new CfnOutput(this, "ClusterName", new CfnOutputProps { Value = Cluster.ClusterName });
            new CfnOutput(this, "ServiceName", new CfnOutputProps { Value = Service.ServiceName });
            new CfnOutput(this, "ServerSecretName", new CfnOutputProps { Value = config.ResourcePath("server") });
            new CfnOutput(this, "UpdatesSigningKeySecretName", new CfnOutputProps { Value = config.ResourcePath("updates-signing-key") });
            new CfnOutput(this, "UpdatesStandbySigningKeySecretName", new CfnOutputProps { Value = config.ResourcePath("updates-standby-signing-key") });
            new CfnOutput(this, "PushSecretName", new CfnOutputProps { Value = config.ResourcePath("push") });
            if (PlatformSecret != null)
            {
                new CfnOutput(this, "PlatformSecretName", new CfnOutputProps { Value = config.ResourcePath("platform") });
            }

            if (MailgunSecret != null)
            {
                new CfnOutput(this, "MailgunSecretName", new CfnOutputProps { Value = config.ResourcePath("mailgun") });
            }

            if (config.Mail.UsesMailgun && !config.Mail.CreatesSesIdentity)
            {
                // Mailgun's records come from the Mailgun dashboard, not from AWS, so they are named here, not listed.
                string sendingDomain = config.Mail.MailgunDomain.Length > 0 ? config.Mail.MailgunDomain : "the Mailgun sending domain";
                new CfnOutput(this, "MailDnsRecords", new CfnOutputProps
                {
                    Value = "Mailgun (no SES identity): create the SPF TXT, DKIM TXT, MX, and tracking CNAME records that the Mailgun dashboard (Sending, Domain settings, DNS records) lists for "
                        + sendingDomain + ", plus a DMARC TXT record on _dmarc of each sending domain's organizational domain.",
                    Description = "Deployment mail DNS records (outside AWS)."
                });
            }

            if (PartioSecret != null)
            {
                new CfnOutput(this, "PartioSecretName", new CfnOutputProps { Value = config.ResourcePath("partio") });
            }

            new CfnOutput(this, "SecretsKeySecretName", new CfnOutputProps { Value = config.ResourcePath("secrets-key") });
            new CfnOutput(this, "LoadBalancerDnsName", new CfnOutputProps { Value = LoadBalancer.LoadBalancerDnsName });

            if (pending.Count > 0)
            {
                new CertificateIssuedCheck(this, "CertificateCheck", config, "certificate-check-api", pending, String.Join(" ", hints)).Gate(this);
            }
        }

        private static IListenerCertificate[] ListenerCertificates(ApiStackDependencies dependencies, List<ICertificate> platformCertificates)
        {
            List<IListenerCertificate> certificates = new List<IListenerCertificate>();
            foreach (ApiHostBinding host in dependencies.Hosts)
            {
                certificates.Add(ListenerCertificate.FromCertificateManager(host.Dns.ApiCertificate));
            }

            foreach (ICertificate certificate in platformCertificates)
            {
                certificates.Add(ListenerCertificate.FromCertificateManager(certificate));
            }

            return certificates.ToArray();
        }

        private void AddTaskRolePermissions(EnvironmentConfig config, ApiStackDependencies dependencies)
        {
            IRole taskRole = TaskDefinition.TaskRole;

            List<string> objectArns = new List<string>();
            List<string> bucketArns = new List<string>();
            foreach (Amazon.CDK.AWS.S3.Bucket bucket in dependencies.Storage.MediaBuckets)
            {
                objectArns.Add(bucket.ArnForObjects("*"));
                bucketArns.Add(bucket.BucketArn);
            }

            // Presigned uploads (single and multipart) to the source buckets, derivative writes, moves into the
            // retained bucket, and erasure of a deleted member's media all need object read, write, and delete.
            taskRole.AddToPrincipalPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Sid = "MediaObjects",
                Actions = new string[] { "s3:PutObject", "s3:GetObject", "s3:DeleteObject", "s3:AbortMultipartUpload", "s3:ListMultipartUploadParts" },
                Resources = objectArns.ToArray()
            }));
            taskRole.AddToPrincipalPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Sid = "MediaBucketsList",
                Actions = new string[] { "s3:ListBucket", "s3:ListBucketMultipartUploads" },
                Resources = bucketArns.ToArray()
            }));
            // Publishing writes bundles, assets, and manifests; rollback and cleanup delete them.
            taskRole.AddToPrincipalPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Sid = "UpdatesObjects",
                Actions = new string[] { "s3:PutObject", "s3:GetObject", "s3:DeleteObject", "s3:AbortMultipartUpload", "s3:ListMultipartUploadParts" },
                Resources = new string[] { dependencies.Storage.UpdatesBucket.ArnForObjects("*") }
            }));
            taskRole.AddToPrincipalPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Sid = "UpdatesBucketList",
                Actions = new string[] { "s3:ListBucket", "s3:ListBucketMultipartUploads" },
                Resources = new string[] { dependencies.Storage.UpdatesBucket.BucketArn }
            }));
            // The backend calls only CreateJob (from the template, on the queue) and GetJob (polling fallback). The SNS
            // webhook needs no IAM: SubscriptionConfirmation is confirmed by fetching its SubscribeURL and every
            // message is verified by signature.
            MediaPipeline media = dependencies.Media;
            taskRole.AddToPrincipalPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Sid = "MediaConvertCreateJob",
                Actions = new string[] { "mediaconvert:CreateJob" },
                Resources = new string[] { media.JobArnPattern, media.Queue.AttrArn, media.JobTemplate.AttrArn }
            }));
            taskRole.AddToPrincipalPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Sid = "MediaConvertGetJob",
                Actions = new string[] { "mediaconvert:GetJob" },
                Resources = new string[] { media.JobArnPattern }
            }));
            taskRole.AddToPrincipalPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Sid = "MediaConvertDescribeEndpoints",
                Actions = new string[] { "mediaconvert:DescribeEndpoints" },
                Resources = new string[] { "*" }
            }));
            taskRole.AddToPrincipalPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Sid = "PassMediaConvertRole",
                Actions = new string[] { "iam:PassRole" },
                Resources = new string[] { media.MediaConvertRole.RoleArn },
                Conditions = new Dictionary<string, object>
                {
                    ["StringEquals"] = new Dictionary<string, object> { ["iam:PassedToService"] = "mediaconvert.amazonaws.com" }
                }
            }));
            taskRole.AddToPrincipalPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Sid = "PrometheusRemoteWrite",
                Actions = new string[] { "aps:RemoteWrite" },
                Resources = new string[] { dependencies.Observability.PrometheusWorkspace.AttrArn }
            }));
            taskRole.AddToPrincipalPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Sid = "XRayExport",
                Actions = new string[] { "xray:PutTraceSegments", "xray:PutTelemetryRecords", "xray:GetSamplingRules", "xray:GetSamplingTargets", "xray:GetSamplingStatisticSummaries" },
                Resources = new string[] { "*" }
            }));
            taskRole.AddToPrincipalPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Sid = "OtlpLogsExport",
                Actions = new string[] { "logs:CreateLogStream", "logs:PutLogEvents", "logs:DescribeLogStreams" },
                Resources = new string[] { dependencies.Observability.TelemetryLogGroup.LogGroupArn, dependencies.Observability.TelemetryLogGroup.LogGroupArn + ":log-stream:*" }
            }));
            // Embedded metric format records of the metrics CloudWatch alarms read (realtime bus queue usage).
            taskRole.AddToPrincipalPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Sid = "EmfMetricsExport",
                Actions = new string[] { "logs:CreateLogStream", "logs:PutLogEvents", "logs:DescribeLogStreams" },
                Resources = new string[] { dependencies.Observability.MetricsLogGroup.LogGroupArn, dependencies.Observability.MetricsLogGroup.LogGroupArn + ":log-stream:*" }
            }));
        }

        private ContainerDefinition AddBootstrapContainer(EnvironmentConfig config, ApiStackDependencies dependencies)
        {
            DatabaseSettings database = config.Database;
            string wait = database.BootstrapWaitSeconds.ToString(CultureInfo.InvariantCulture);

            // Wait until the instance accepts connections, then apply BootstrapSql as the master user. The application
            // password is passed as a psql variable (argv of the psql process inside this container), never logged.
            RecallDbService? recallDb = RecallDb;
            string recallDbArguments = recallDb != null
                ? " -v rd_user=\"$RECALLDB_DB_USERNAME\" -v rd_password=\"$RECALLDB_DB_PASSWORD\" -v rd_db=\"$RECALLDB_DB_NAME\""
                : String.Empty;
            string script =
                "set -eu; " +
                "deadline=$(( $(date +%s) + " + wait + " )); " +
                "until pg_isready -q; do if [ $(date +%s) -ge $deadline ]; then echo 'database not reachable' >&2; exit 1; fi; sleep 5; done; " +
                "printf '%s' \"$BOOTSTRAP_SQL\" | psql -v ON_ERROR_STOP=1 -v app_user=\"$APP_DB_USERNAME\" -v app_password=\"$APP_DB_PASSWORD\" -v db=\"$PGDATABASE\" -v search_schema=\"$SEARCH_SCHEMA\"" + recallDbArguments + " -q -f -; " +
                "echo 'trelliskit-db-bootstrap: role, grants, search schema, and pgvector are in place'";

            Dictionary<string, string> environment = new Dictionary<string, string>
            {
                ["PGHOST"] = dependencies.Data.Database.DbInstanceEndpointAddress,
                ["PGPORT"] = dependencies.Data.Database.DbInstanceEndpointPort,
                ["PGDATABASE"] = database.DatabaseName,
                ["PGSSLMODE"] = "require",
                ["PGCONNECT_TIMEOUT"] = "10",
                ["SEARCH_SCHEMA"] = database.SearchSchema,
                ["BOOTSTRAP_SQL"] = recallDb != null ? BootstrapSql + RecallDbBootstrapSql : BootstrapSql
            };
            Dictionary<string, EcsSecret> secrets = new Dictionary<string, EcsSecret>
            {
                ["PGUSER"] = EcsSecret.FromSecretsManager(dependencies.Data.DatabaseSecret, "username"),
                ["PGPASSWORD"] = EcsSecret.FromSecretsManager(dependencies.Data.DatabaseSecret, "password"),
                ["APP_DB_USERNAME"] = EcsSecret.FromSecretsManager(dependencies.Data.ApplicationSecret, "username"),
                ["APP_DB_PASSWORD"] = EcsSecret.FromSecretsManager(dependencies.Data.ApplicationSecret, "password")
            };
            if (recallDb != null)
            {
                environment["RECALLDB_DB_NAME"] = config.Ai.RecallDbDatabaseName;
                secrets["RECALLDB_DB_USERNAME"] = EcsSecret.FromSecretsManager(recallDb.DatabaseSecret, "username");
                secrets["RECALLDB_DB_PASSWORD"] = EcsSecret.FromSecretsManager(recallDb.DatabaseSecret, "password");
            }

            return TaskDefinition.AddContainer(BootstrapContainerName, new ContainerDefinitionOptions
            {
                ContainerName = BootstrapContainerName,
                Image = ContainerImage.FromRegistry(database.BootstrapImage),
                Essential = false,
                MemoryReservationMiB = database.BootstrapMemoryMiB,
                EntryPoint = new string[] { "sh", "-c" },
                Command = new string[] { script },
                Environment = environment,
                Secrets = secrets,
                Logging = LogDriver.AwsLogs(new AwsLogDriverProps
                {
                    LogGroup = dependencies.Observability.ServerLogGroup,
                    StreamPrefix = BootstrapContainerName
                })
            });
        }

        private void ApplyNagSuppressions()
        {
            NagHelper.Suppress(TaskDefinition, "AwsSolutions-ECS2",
                "Only non-secret configuration (hostnames, ports, URLs, the collector pipeline) is passed as environment variables. Every secret is injected from Secrets Manager through ECS secrets.");
            NagHelper.SuppressWildcards(TaskDefinition,
                "Object-level wildcards are scoped to the media buckets (presigned uploads use per-object keys) and the updates bucket (content-addressed keys chosen per publish). MediaConvert job ids are assigned by the service, so CreateJob and GetJob are scoped to this account's jobs in this region. mediaconvert:DescribeEndpoints and the X-Ray actions do not support resource-level permissions. The log stream wildcard is scoped to one log group. The execution role's ecr:GetAuthorizationToken requires Resource *.",
                NagHelper.AnyResourcePattern,
                "/^Resource::arn:aws:mediaconvert:[a-z0-9-]+:[0-9]{12}:jobs\\/\\*$/g",
                NagHelper.ResourcePattern("MediaSourceBucket", "Arn", "\\/\\*"),
                NagHelper.ResourcePattern("MediaVideoSourceBucket", "Arn", "\\/\\*"),
                NagHelper.ResourcePattern("MediaImagesBucket", "Arn", "\\/\\*"),
                NagHelper.ResourcePattern("MediaTranscodedBucket", "Arn", "\\/\\*"),
                NagHelper.ResourcePattern("MediaRetainedBucket", "Arn", "\\/\\*"),
                NagHelper.ResourcePattern("UpdatesBucket", "Arn", "\\/\\*"),
                NagHelper.ResourcePattern("TelemetryLogGroup", "Arn", ":log-stream:\\*"),
                NagHelper.ResourcePattern("MetricsLogGroup", "Arn", ":log-stream:\\*"));
        }

        private static Dictionary<string, string> BuildServerEnvironment(EnvironmentConfig config, ApiStackDependencies dependencies, RecallDbService? recallDb)
        {
            ApiSettings api = config.Api;
            string port = api.ContainerPort.ToString(CultureInfo.InvariantCulture);
            string otlp = "http://127.0.0.1:" + api.OtlpGrpcPort.ToString(CultureInfo.InvariantCulture);
            StorageStack storage = dependencies.Storage;
            MediaPipeline media = dependencies.Media;

            Dictionary<string, string> environment = new Dictionary<string, string>
            {
                ["TK_PROPERTY_DIR"] = api.PropertyDirectory,
                [ServerEnvironmentKeys.DeploymentMode] = config.IsShared ? "MultiProperty" : "SingleProperty",
                [ServerEnvironmentKeys.DeploymentName] = config.DeploymentId,
                [ServerEnvironmentKeys.TrustForwardedHost] = Flag(api.TrustForwardedHost),
                [ServerEnvironmentKeys.TrustedProxies] = config.Network.CidrBlock,
                ["TK_REST_HOSTNAME"] = "0.0.0.0",
                ["TK_REST_PORT"] = port,
                ["TK_CORS_ORIGINS"] = String.Join(",", config.WebOrigins),
                ["TK_DB_TYPE"] = "Postgresql",
                ["TK_DB_HOSTNAME"] = dependencies.Data.Database.DbInstanceEndpointAddress,
                ["TK_DB_PORT"] = dependencies.Data.Database.DbInstanceEndpointPort,
                ["TK_DB_DATABASE"] = config.Database.DatabaseName,
                ["TK_DB_MAX_POOL_SIZE"] = config.Database.MaxPoolSize.ToString(CultureInfo.InvariantCulture),
                ["TK_TELEMETRY_ENABLED"] = "true",
                ["TK_OTLP_ENDPOINT"] = otlp,
                ["TK_OTLP_PROTOCOL"] = "Grpc",
                ["TK_TELEMETRY_SERVICE_NAME"] = api.TelemetryServiceName,
                [ServerEnvironmentKeys.MediaStorageMode] = "S3",
                [ServerEnvironmentKeys.MediaS3Region] = config.Region,
                [ServerEnvironmentKeys.MediaSourceBucket] = storage.MediaSourceBucket.BucketName,
                [ServerEnvironmentKeys.MediaVideoSourceBucket] = storage.MediaVideoSourceBucket.BucketName,
                [ServerEnvironmentKeys.MediaImagesBucket] = storage.MediaImagesBucket.BucketName,
                [ServerEnvironmentKeys.MediaTranscodedBucket] = storage.MediaTranscodedBucket.BucketName,
                [ServerEnvironmentKeys.MediaRetainedBucket] = storage.MediaRetainedBucket.BucketName,
                [ServerEnvironmentKeys.MediaVideoProcessor] = "MediaConvert",
                [ServerEnvironmentKeys.MediaSigningKeyPairId] = media.SigningPublicKey.PublicKeyId,
                [ServerEnvironmentKeys.MediaConvertRoleArn] = media.MediaConvertRole.RoleArn,
                [ServerEnvironmentKeys.MediaSnsTopicArn] = media.JobEventsTopic.TopicArn,
                [ServerEnvironmentKeys.MediaConvertQueueArn] = media.Queue.AttrArn,
                [ServerEnvironmentKeys.MediaConvertJobTemplate] = media.JobTemplateName,
                [ServerEnvironmentKeys.UpdatesStorageMode] = "S3",
                [ServerEnvironmentKeys.UpdatesS3Region] = config.Region,
                [ServerEnvironmentKeys.UpdatesBucket] = storage.UpdatesBucket.BucketName
            };

            // The SES SMTP endpoint only when the SES identity exists (mail.provider other than Mailgun and None).
            if (config.Mail.CreatesSesIdentity)
            {
                environment["TK_SMTP_HOST"] = String.Format(CultureInfo.InvariantCulture, config.Email.SmtpHostTemplate, config.Region);
                environment["TK_SMTP_PORT"] = api.SmtpPort.ToString(CultureInfo.InvariantCulture);
                environment["TK_SMTP_USE_TLS"] = "true";
            }

            // Live settings admins edit in the console: an environment variable overrides and locks them, so each is set
            // only when configured in ApiSettings.
            if (api.PushEnabled != null) environment[ServerEnvironmentKeys.PushEnabled] = Flag(api.PushEnabled.Value);
            if (api.LinkPreviewsEnabled != null) environment[ServerEnvironmentKeys.LinkPreviewsEnabled] = Flag(api.LinkPreviewsEnabled.Value);
            if (api.EmailFeedPostsEnabled != null) environment[ServerEnvironmentKeys.EmailFeedPostsEnabled] = Flag(api.EmailFeedPostsEnabled.Value);
            if (api.AccountExportRetentionHours != null)
                environment[ServerEnvironmentKeys.AccountExportRetentionHours] = api.AccountExportRetentionHours.Value.ToString(CultureInfo.InvariantCulture);

            // Platform settings are live too: set only when the deployment file configures them (shared topology).
            if (config.Platform.Hosts.Count > 0) environment[ServerEnvironmentKeys.PlatformHosts] = String.Join(",", config.Platform.Hosts);
            AddIfSet(environment, ServerEnvironmentKeys.PlatformBootstrapAdminEmail, config.Platform.BootstrapAdminEmail);

            // Live proxy, domain verification, and deployment mail settings: set only when configured.
            if (config.Network.AdditionalTrustedProxies.Count > 0)
                environment[ServerEnvironmentKeys.AdditionalTrustedProxies] = String.Join(",", config.Network.AdditionalTrustedProxies);
            AddIfSet(environment, ServerEnvironmentKeys.DomainVerificationResolverUrl, api.DomainVerificationResolverUrl ?? String.Empty);
            AddIfSet(environment, ServerEnvironmentKeys.EmailProvider, config.Mail.ServerProvider);
            AddIfSet(environment, ServerEnvironmentKeys.MailgunDomain, config.Mail.MailgunDomain);
            AddIfSet(environment, ServerEnvironmentKeys.MailgunBaseUrl, config.Mail.MailgunBaseUrl);
            AddIfSet(environment, ServerEnvironmentKeys.PartioBaseUrl, config.Partio.BaseUrl);

            // Live RecallDB settings: set only when ai.recallDb is configured, because the stack then decides them.
            if (recallDb != null)
            {
                environment[ServerEnvironmentKeys.RecallDbEndpoint] = recallDb.Endpoint;
                environment[ServerEnvironmentKeys.RecallDbTenant] = config.Ai.RecallDbTenant;
            }

            // In the shared topology these differ per property; the backend reads them from each property's
            // configuration (docs/TRELLISKIT.md section 10.4), so only a single-property deployment sets them here.
            if (config.IsShared) return environment;

            environment["TK_WEB_PUBLIC_BASE_URL"] = config.Domains.WebsiteUrl;
            environment["TK_SMTP_FROM_ADDRESS"] = api.SmtpFromLocalPart + "@" + config.Domains.PrimaryDomain;
            environment[ServerEnvironmentKeys.MediaPublicBaseUrl] = "https://" + config.Domains.MediaHost;
            environment[ServerEnvironmentKeys.MediaCookieDomain] = config.Domains.PrimaryDomain;
            environment[ServerEnvironmentKeys.UpdatesPublicBaseUrl] = "https://" + config.Domains.MediaHost + config.Updates.PathPrefix;

            // Public identifiers from property.json (not secrets), set only when the property defines them.
            PropertyDefinition property = config.Property;
            AddIfSet(environment, "TK_APPSTORE_BUNDLE_ID", property.BundleId);
            AddIfSet(environment, "TK_APPLE_BUNDLE_ID", property.BundleId);
            AddIfSet(environment, "TK_GOOGLEPLAY_PACKAGE_NAME", property.AndroidPackage);
            AddIfSet(environment, "TK_APPSTORE_PRODUCT_ID", property.StoreProductId);
            AddIfSet(environment, "TK_GOOGLEPLAY_PRODUCT_ID", property.StoreProductId);
            return environment;
        }

        private static string Flag(bool value)
        {
            return value ? "true" : "false";
        }

        private static void AddIfSet(Dictionary<string, string> environment, string key, string value)
        {
            if (!String.IsNullOrWhiteSpace(value)) environment[key] = value;
        }

        private Dictionary<string, EcsSecret> BuildServerSecrets(ApiStackDependencies dependencies)
        {
            Dictionary<string, EcsSecret> secrets = new Dictionary<string, EcsSecret>
            {
                ["TK_DB_USERNAME"] = EcsSecret.FromSecretsManager(dependencies.Data.ApplicationSecret, "username"),
                ["TK_DB_PASSWORD"] = EcsSecret.FromSecretsManager(dependencies.Data.ApplicationSecret, "password"),
                [ServerEnvironmentKeys.MediaSigningPrivateKey] = EcsSecret.FromSecretsManager(dependencies.Media.SigningPrivateKeySecret),
                [ServerEnvironmentKeys.UpdatesSigningKey] = EcsSecret.FromSecretsManager(UpdatesSigningKeySecret),
                [ServerEnvironmentKeys.UpdatesStandbySigningKey] = EcsSecret.FromSecretsManager(UpdatesStandbySigningKeySecret, ServerEnvironmentKeys.UpdatesStandbySigningKey),
                [ServerEnvironmentKeys.ExpoAccessToken] = EcsSecret.FromSecretsManager(PushSecret, ServerEnvironmentKeys.ExpoAccessToken),
                [ServerEnvironmentKeys.SecretsEncryptionKey] = EcsSecret.FromSecretsManager(SecretsKey.Secret, ServerEnvironmentKeys.SecretsEncryptionKey),
                [ServerEnvironmentKeys.SecretsPreviousEncryptionKeys] = EcsSecret.FromSecretsManager(SecretsKey.Secret, ServerEnvironmentKeys.SecretsPreviousEncryptionKeys)
            };

            if (MailgunSecret != null)
            {
                secrets[ServerEnvironmentKeys.MailgunApiKey] = EcsSecret.FromSecretsManager(MailgunSecret, ServerEnvironmentKeys.MailgunApiKey);
            }

            if (PartioSecret != null)
            {
                secrets[ServerEnvironmentKeys.PartioApiKey] = EcsSecret.FromSecretsManager(PartioSecret, ServerEnvironmentKeys.PartioApiKey);
            }

            foreach (string key in ServerSecretKeys.AllKeys)
            {
                secrets[key] = EcsSecret.FromSecretsManager(ServerSecret, key);
            }

            if (RecallDb != null)
            {
                secrets[ServerEnvironmentKeys.RecallDbToken] = EcsSecret.FromSecretsManager(RecallDb.TokenSecret);
            }

            if (PlatformSecret != null)
            {
                secrets[ServerSecretKeys.PlatformBootstrapAdminPassword] = EcsSecret.FromSecretsManager(PlatformSecret, ServerSecretKeys.PlatformBootstrapAdminPassword);
            }

            return secrets;
        }

        private static string BuildSecretTemplate()
        {
            Dictionary<string, string> template = new Dictionary<string, string>();
            foreach (string key in ServerSecretKeys.ManualKeys) template[key] = String.Empty;
            return JsonSerializer.Serialize(template);
        }

        private static string BuildCollectorConfig(EnvironmentConfig config, ApiStackDependencies dependencies)
        {
            string region = config.Region;
            string grpc = config.Api.OtlpGrpcPort.ToString(CultureInfo.InvariantCulture);
            string http = config.Api.OtlpHttpPort.ToString(CultureInfo.InvariantCulture);
            string health = config.Observability.CollectorHealthPort.ToString(CultureInfo.InvariantCulture);
            string remoteWrite = dependencies.Observability.PrometheusWorkspace.AttrPrometheusEndpoint + "api/v1/remote_write";

            StringBuilder yaml = new StringBuilder();
            yaml.Append("extensions:\n");
            yaml.Append("  health_check:\n");
            yaml.Append("    endpoint: 0.0.0.0:" + health + "\n");
            yaml.Append("  sigv4auth:\n");
            yaml.Append("    region: " + region + "\n");
            yaml.Append("    service: aps\n");
            yaml.Append("receivers:\n");
            yaml.Append("  otlp:\n");
            yaml.Append("    protocols:\n");
            yaml.Append("      grpc:\n");
            yaml.Append("        endpoint: 127.0.0.1:" + grpc + "\n");
            yaml.Append("      http:\n");
            yaml.Append("        endpoint: 127.0.0.1:" + http + "\n");
            yaml.Append("processors:\n");
            yaml.Append("  memory_limiter:\n");
            yaml.Append("    check_interval: 1s\n");
            yaml.Append("    limit_percentage: 80\n");
            yaml.Append("    spike_limit_percentage: 20\n");
            yaml.Append("  resourcedetection:\n");
            yaml.Append("    detectors: [env, ecs]\n");
            yaml.Append("  batch: {}\n");
            yaml.Append("  filter/cloudwatch:\n");
            yaml.Append("    metrics:\n");
            yaml.Append("      include:\n");
            yaml.Append("        match_type: strict\n");
            yaml.Append("        metric_names:\n");
            yaml.Append("          - " + ObservabilitySettings.RealtimeBusQueueUsageMetric + "\n");
            yaml.Append("exporters:\n");
            yaml.Append("  prometheusremotewrite:\n");
            yaml.Append("    endpoint: " + remoteWrite + "\n");
            yaml.Append("    auth:\n");
            yaml.Append("      authenticator: sigv4auth\n");
            yaml.Append("    resource_to_telemetry_conversion:\n");
            yaml.Append("      enabled: true\n");
            yaml.Append("  awsxray:\n");
            yaml.Append("    region: " + region + "\n");
            yaml.Append("  awscloudwatchlogs:\n");
            yaml.Append("    region: " + region + "\n");
            yaml.Append("    log_group_name: " + dependencies.Observability.TelemetryLogGroup.LogGroupName + "\n");
            yaml.Append("    log_stream_name: " + config.Api.TelemetryServiceName + "\n");
            yaml.Append("  awsemf:\n");
            yaml.Append("    region: " + region + "\n");
            yaml.Append("    namespace: " + dependencies.Observability.MetricsNamespace + "\n");
            yaml.Append("    log_group_name: " + dependencies.Observability.MetricsLogGroup.LogGroupName + "\n");
            yaml.Append("    log_stream_name: " + config.Api.TelemetryServiceName + "\n");
            yaml.Append("    dimension_rollup_option: NoDimensionRollup\n");
            yaml.Append("    resource_to_telemetry_conversion:\n");
            yaml.Append("      enabled: false\n");
            yaml.Append("service:\n");
            yaml.Append("  extensions: [health_check, sigv4auth]\n");
            yaml.Append("  pipelines:\n");
            yaml.Append("    metrics:\n");
            yaml.Append("      receivers: [otlp]\n");
            yaml.Append("      processors: [memory_limiter, resourcedetection, batch]\n");
            yaml.Append("      exporters: [prometheusremotewrite]\n");
            yaml.Append("    metrics/cloudwatch:\n");
            yaml.Append("      receivers: [otlp]\n");
            yaml.Append("      processors: [memory_limiter, filter/cloudwatch, batch]\n");
            yaml.Append("      exporters: [awsemf]\n");
            yaml.Append("    traces:\n");
            yaml.Append("      receivers: [otlp]\n");
            yaml.Append("      processors: [memory_limiter, resourcedetection, batch]\n");
            yaml.Append("      exporters: [awsxray]\n");
            yaml.Append("    logs:\n");
            yaml.Append("      receivers: [otlp]\n");
            yaml.Append("      processors: [memory_limiter, resourcedetection, batch]\n");
            yaml.Append("      exporters: [awscloudwatchlogs]\n");
            return yaml.ToString();
        }
    }
}
