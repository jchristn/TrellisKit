namespace TrellisKit.Infra.Stacks
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using Amazon.CDK;
    using Amazon.CDK.AWS.EC2;
    using Amazon.CDK.AWS.ECS;
    using Amazon.CDK.AWS.RDS;
    using Amazon.CDK.AWS.SecretsManager;
    using Amazon.CDK.AWS.ServiceDiscovery;
    using TrellisKit.Infra.Config;
    using TrellisKit.Infra.Nag;
    using Constructs;
    using EcsHealthCheck = Amazon.CDK.AWS.ECS.HealthCheck;
    using EcsProtocol = Amazon.CDK.AWS.ECS.Protocol;
    using EcsSecret = Amazon.CDK.AWS.ECS.Secret;
    using SecretsManagerSecret = Amazon.CDK.AWS.SecretsManager.Secret;

    /// <summary>
    /// RecallDB (docs/AI_CONTRACT.md, docs/TRELLISKIT.md section 13) as a Fargate service in the private subnets,
    /// created only when <see cref="AiSettings.RecallDbEnabled"/> is set. It runs the pinned linux/amd64 image on X86_64
    /// with the same start script as the Docker stack (docker/recalldb/start.sh, read at synth time): the script writes
    /// RecallDB's settings file (all interfaces, the admin API key from Secrets Manager, TLS to RDS) and makes sure the
    /// server's credential exists. Its database and role are created by the API task's db-bootstrap container
    /// (<see cref="ApiStack.RecallDbBootstrapSql"/>). The service registers in the cluster's private DNS namespace, its
    /// security group admits only the API tasks, and the server reaches it at <see cref="Endpoint"/>.
    /// </summary>
    public class RecallDbService : Construct
    {
        /// <summary>
        /// Name of the RecallDB container.
        /// </summary>
        public static readonly string ContainerName = "recalldb";

        /// <summary>
        /// Path of the start script relative to the engine root.
        /// </summary>
        public static readonly string StartScriptPath = Path.Combine("docker", "recalldb", "start.sh");

        /// <summary>
        /// Database credentials of the RecallDB role (username and password), read by db-bootstrap and RecallDB.
        /// </summary>
        public DatabaseSecret DatabaseSecret { get; }

        /// <summary>
        /// RecallDB admin API key (generated; plain string), read only by the RecallDB task.
        /// </summary>
        public SecretsManagerSecret AdminKeySecret { get; }

        /// <summary>
        /// Bearer token of the server's RecallDB credential (generated; plain string), read by RecallDB and the server.
        /// </summary>
        public SecretsManagerSecret TokenSecret { get; }

        /// <summary>
        /// The RecallDB task definition.
        /// </summary>
        public FargateTaskDefinition TaskDefinition { get; }

        /// <summary>
        /// The RecallDB service.
        /// </summary>
        public FargateService Service { get; }

        /// <summary>
        /// Base URL the server uses (TK_RECALLDB_ENDPOINT), for example http://recalldb.example-dev.internal:8600.
        /// </summary>
        public string Endpoint { get; }

        /// <summary>
        /// Instantiate the construct.
        /// </summary>
        /// <param name="scope">Parent construct (the API stack).</param>
        /// <param name="id">Construct id.</param>
        /// <param name="config">Environment configuration.</param>
        /// <param name="cluster">ECS cluster the service runs in; it receives the private DNS namespace.</param>
        /// <param name="dependencies">Stacks the API reads from (network, data, observability).</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        /// <exception cref="InvalidOperationException">RecallDB is not enabled, or the network has no RecallDB security group.</exception>
        /// <exception cref="FileNotFoundException">The start script is missing.</exception>
        public RecallDbService(Construct scope, string id, EnvironmentConfig config, Cluster cluster, ApiStackDependencies dependencies) : base(scope, id)
        {
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(cluster);
            ArgumentNullException.ThrowIfNull(dependencies);
            AiSettings ai = config.Ai;
            if (!ai.RecallDbEnabled) throw new InvalidOperationException("RecallDB is not enabled (ai.recallDb).");
            SecurityGroup securityGroup = dependencies.Network.RecallDbSecurityGroup
                ?? throw new InvalidOperationException("The network stack has no RecallDB security group.");

            DatabaseSecret = new DatabaseSecret(this, "DatabaseSecret", new DatabaseSecretProps
            {
                Username = ai.RecallDbUsername,
                SecretName = config.ResourcePath("recalldb-database")
            });
            DatabaseSecret.ApplyRemovalPolicy(config.StatefulRemovalPolicy);
            NagHelper.Suppress(DatabaseSecret, "AwsSolutions-SMG4",
                "The db-bootstrap container sets this password on the RecallDB role at every API task start, so it is rotated by changing the secret and redeploying both services (see infra/README.md).");

            AdminKeySecret = new SecretsManagerSecret(this, "AdminKeySecret", new SecretProps
            {
                SecretName = config.ResourcePath("recalldb-admin-key"),
                Description = config.DisplayName + " RecallDB admin API key (generated; used only by the RecallDB task).",
                GenerateSecretString = new SecretStringGenerator { PasswordLength = 48, ExcludePunctuation = true },
                RemovalPolicy = config.StatefulRemovalPolicy
            });
            NagHelper.Suppress(AdminKeySecret, "AwsSolutions-SMG4",
                "RecallDB reads its admin API key only at start; rotate by putting a new value and redeploying the RecallDB service.");

            TokenSecret = new SecretsManagerSecret(this, "TokenSecret", new SecretProps
            {
                SecretName = config.ResourcePath("recalldb-token"),
                Description = config.DisplayName + " bearer token of the server's RecallDB credential (TK_RECALLDB_TOKEN; generated).",
                GenerateSecretString = new SecretStringGenerator { PasswordLength = 48, ExcludePunctuation = true },
                RemovalPolicy = config.StatefulRemovalPolicy
            });
            NagHelper.Suppress(TokenSecret, "AwsSolutions-SMG4",
                "Both RecallDB (which registers the credential at start) and the server read the token only at start; rotation is manual (see infra/README.md).");

            string namespaceName = config.ResourcePrefix + ".internal";
            cluster.AddDefaultCloudMapNamespace(new CloudMapNamespaceOptions
            {
                Name = namespaceName,
                Type = NamespaceType.DNS_PRIVATE,
                Vpc = dependencies.Network.Vpc
            });
            string port = ai.RecallDbPort.ToString(CultureInfo.InvariantCulture);
            Endpoint = "http://" + ai.RecallDbServiceName + "." + namespaceName + ":" + port;

            TaskDefinition = new FargateTaskDefinition(this, "TaskDefinition", new FargateTaskDefinitionProps
            {
                Family = config.ResourceName("recalldb"),
                Cpu = ai.RecallDbCpu,
                MemoryLimitMiB = ai.RecallDbMemoryMiB,
                // The published image is linux/amd64 only.
                RuntimePlatform = new RuntimePlatform
                {
                    CpuArchitecture = CpuArchitecture.X86_64,
                    OperatingSystemFamily = OperatingSystemFamily.LINUX
                }
            });

            TaskDefinition.AddContainer(ContainerName, new ContainerDefinitionOptions
            {
                ContainerName = ContainerName,
                Image = ContainerImage.FromRegistry(ai.RecallDbImage),
                Essential = true,
                // The start script travels as an environment variable (no secrets in it) so the public image needs no
                // custom build.
                EntryPoint = new string[] { "/bin/sh", "-c" },
                Command = new string[] { "printf '%s' \"$RECALLDB_START_SCRIPT\" > /tmp/recalldb-start.sh && exec /bin/sh /tmp/recalldb-start.sh" },
                Environment = new Dictionary<string, string>
                {
                    ["RECALLDB_START_SCRIPT"] = ReadStartScript(),
                    ["RECALLDB_DB_HOST"] = dependencies.Data.Database.DbInstanceEndpointAddress,
                    ["RECALLDB_DB_PORT"] = dependencies.Data.Database.DbInstanceEndpointPort,
                    ["RECALLDB_DB_NAME"] = ai.RecallDbDatabaseName,
                    ["RECALLDB_DB_SCHEMA"] = "public",
                    ["RECALLDB_DB_REQUIRE_ENCRYPTION"] = "true",
                    ["RECALLDB_TENANT"] = ai.RecallDbTenant,
                    ["RECALLDB_PORT"] = port,
                    ["RECALLDB_MCP_ENABLED"] = "false",
                    ["RECALLDB_OBS_ENABLED"] = "false"
                },
                Secrets = new Dictionary<string, EcsSecret>
                {
                    ["RECALLDB_DB_USER"] = EcsSecret.FromSecretsManager(DatabaseSecret, "username"),
                    ["RECALLDB_DB_PASS"] = EcsSecret.FromSecretsManager(DatabaseSecret, "password"),
                    ["RECALLDB_ADMIN_API_KEY"] = EcsSecret.FromSecretsManager(AdminKeySecret),
                    ["RECALLDB_SERVER_TOKEN"] = EcsSecret.FromSecretsManager(TokenSecret)
                },
                PortMappings = new IPortMapping[]
                {
                    new PortMapping { ContainerPort = ai.RecallDbPort, Protocol = EcsProtocol.TCP, Name = "http" }
                },
                Logging = LogDriver.AwsLogs(new AwsLogDriverProps
                {
                    LogGroup = dependencies.Observability.ServerLogGroup,
                    StreamPrefix = ContainerName
                }),
                HealthCheck = new EcsHealthCheck
                {
                    Command = new string[] { "CMD-SHELL", "curl -fsS http://127.0.0.1:" + port + "/ || exit 1" },
                    Interval = Duration.Seconds(15),
                    Timeout = Duration.Seconds(5),
                    Retries = 3,
                    StartPeriod = Duration.Seconds(120)
                }
            });
            NagHelper.Suppress(TaskDefinition, "AwsSolutions-ECS2",
                "Only non-secret configuration (database host and name, tenant, the start script) is passed as environment variables. The database password, admin API key, and server token come from Secrets Manager through ECS secrets.");

            Service = new FargateService(this, "Service", new FargateServiceProps
            {
                ServiceName = config.ResourceName("recalldb"),
                Cluster = cluster,
                TaskDefinition = TaskDefinition,
                DesiredCount = ai.RecallDbDesiredCount,
                MinHealthyPercent = 100,
                MaxHealthyPercent = 200,
                AssignPublicIp = false,
                VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_WITH_EGRESS },
                SecurityGroups = new ISecurityGroup[] { securityGroup },
                CircuitBreaker = new DeploymentCircuitBreaker { Enable = true, Rollback = true },
                PlatformVersion = FargatePlatformVersion.LATEST,
                EnableExecuteCommand = false,
                PropagateTags = PropagatedTagSource.SERVICE,
                CloudMapOptions = new CloudMapOptions
                {
                    Name = ai.RecallDbServiceName,
                    DnsRecordType = DnsRecordType.A,
                    DnsTtl = Duration.Seconds(10)
                }
            });

            new CfnOutput(this, "RecallDbEndpoint", new CfnOutputProps { Value = Endpoint });
            new CfnOutput(this, "RecallDbTokenSecretName", new CfnOutputProps { Value = config.ResourcePath("recalldb-token") });
        }

        /// <summary>
        /// Read the RecallDB start script from the engine (docker/recalldb/start.sh).
        /// </summary>
        /// <returns>Script text.</returns>
        /// <exception cref="FileNotFoundException">The script is missing.</exception>
        public static string ReadStartScript()
        {
            string path = Path.Combine(PropertyDefinition.EngineRoot(), StartScriptPath);
            if (!File.Exists(path)) throw new FileNotFoundException("The RecallDB start script is missing: " + path + ".", path);
            return File.ReadAllText(path).Replace("\r\n", "\n");
        }
    }
}
