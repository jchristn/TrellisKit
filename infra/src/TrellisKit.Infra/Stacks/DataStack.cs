namespace TrellisKit.Infra.Stacks
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Amazon.CDK;
    using Amazon.CDK.AWS.EC2;
    using Amazon.CDK.AWS.Logs;
    using Amazon.CDK.AWS.RDS;
    using Amazon.CDK.AWS.SecretsManager;
    using TrellisKit.Infra.Config;
    using TrellisKit.Infra.Nag;
    using Constructs;
    using InstanceType = Amazon.CDK.AWS.EC2.InstanceType;

    /// <summary>
    /// RDS PostgreSQL 17 in the isolated subnets: encrypted, automated backups with point-in-time recovery, generated
    /// master and application credentials in Secrets Manager, a custom parameter group, Multi-AZ and deletion
    /// protection per environment. The pgvector extension and the non-superuser application role are created by the
    /// database bootstrap container of the API task (<see cref="ApiStack"/>), which runs as the master user.
    /// </summary>
    public class DataStack : Stack
    {
        /// <summary>
        /// Log types exported to CloudWatch Logs.
        /// </summary>
        public static readonly string[] ExportedLogTypes = new string[] { "postgresql", "upgrade" };

        /// <summary>
        /// The database instance.
        /// </summary>
        public DatabaseInstance Database { get; }

        /// <summary>
        /// Generated master credentials (JSON with username, password, host, port, dbname). Read only by the bootstrap
        /// container.
        /// </summary>
        public ISecret DatabaseSecret { get; }

        /// <summary>
        /// Generated credentials of the application role the server connects as (JSON with username and password).
        /// </summary>
        public DatabaseSecret ApplicationSecret { get; }

        /// <summary>
        /// Instantiate the stack.
        /// </summary>
        /// <param name="scope">Parent construct.</param>
        /// <param name="id">Construct id.</param>
        /// <param name="config">Environment configuration.</param>
        /// <param name="network">Network stack.</param>
        /// <param name="props">Stack properties.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        public DataStack(Construct scope, string id, EnvironmentConfig config, NetworkStack network, IStackProps props) : base(scope, id, props)
        {
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(network);

            DatabaseSettings settings = config.Database;
            IInstanceEngine engine = DatabaseInstanceEngine.Postgres(new PostgresInstanceEngineProps
            {
                Version = PostgresEngineVersion.Of(settings.EngineMajorVersion, settings.EngineMajorVersion)
            });

            ParameterGroup parameterGroup = new ParameterGroup(this, "ParameterGroup", new ParameterGroupProps
            {
                Engine = engine,
                Description = config.DisplayName + " PostgreSQL " + settings.EngineMajorVersion + " (" + config.Name + ")",
                Parameters = new Dictionary<string, string>
                {
                    ["rds.force_ssl"] = "1",
                    ["log_min_duration_statement"] = settings.SlowQueryMilliseconds.ToString(CultureInfo.InvariantCulture),
                    ["log_connections"] = "1",
                    ["log_disconnections"] = "1",
                    ["log_lock_waits"] = "1",
                    ["shared_preload_libraries"] = "pg_stat_statements"
                }
            });

            // Create the log groups up front so retention is set without the LogRetention custom resource Lambda.
            List<LogGroup> logGroups = new List<LogGroup>();
            foreach (string logType in ExportedLogTypes)
            {
                logGroups.Add(new LogGroup(this, "DatabaseLogs-" + logType, new LogGroupProps
                {
                    LogGroupName = "/aws/rds/instance/" + settings.InstanceIdentifier + "/" + logType,
                    Retention = config.Observability.LogRetention,
                    RemovalPolicy = config.StatefulRemovalPolicy
                }));
            }

            Database = new DatabaseInstance(this, "Database", new DatabaseInstanceProps
            {
                InstanceIdentifier = settings.InstanceIdentifier,
                Engine = engine,
                InstanceType = InstanceType.Of(settings.InstanceClass, settings.InstanceSize),
                Vpc = network.Vpc,
                VpcSubnets = new SubnetSelection { SubnetType = SubnetType.PRIVATE_ISOLATED },
                SecurityGroups = new ISecurityGroup[] { network.DatabaseSecurityGroup },
                Port = settings.Port,
                DatabaseName = settings.DatabaseName,
                Credentials = Credentials.FromGeneratedSecret(settings.MasterUsername, new CredentialsBaseOptions
                {
                    SecretName = config.ResourcePath("database")
                }),
                ParameterGroup = parameterGroup,
                MultiAz = settings.MultiAz,
                StorageType = StorageType.GP3,
                AllocatedStorage = settings.AllocatedStorageGiB,
                MaxAllocatedStorage = settings.MaxAllocatedStorageGiB,
                StorageEncrypted = true,
                BackupRetention = Duration.Days(settings.BackupRetentionDays),
                PreferredBackupWindow = settings.PreferredBackupWindow,
                PreferredMaintenanceWindow = settings.PreferredMaintenanceWindow,
                DeleteAutomatedBackups = false,
                CopyTagsToSnapshot = true,
                DeletionProtection = settings.DeletionProtection,
                RemovalPolicy = RemovalPolicy.SNAPSHOT,
                AutoMinorVersionUpgrade = true,
                AllowMajorVersionUpgrade = false,
                PubliclyAccessible = false,
                CaCertificate = CaCertificate.RDS_CA_RSA2048_G1,
                CloudwatchLogsExports = ExportedLogTypes
            });
            foreach (LogGroup logGroup in logGroups) Database.Node.AddDependency(logGroup);

            DatabaseSecret = Database.Secret!;

            if (settings.ApplicationUsername == settings.MasterUsername)
            {
                throw new ArgumentException("The application role must differ from the master user (" + settings.MasterUsername + ").", nameof(config));
            }

            ApplicationSecret = new DatabaseSecret(this, "ApplicationSecret", new DatabaseSecretProps
            {
                Username = settings.ApplicationUsername,
                SecretName = config.ResourcePath("database-app")
            });
            ApplicationSecret.ApplyRemovalPolicy(config.StatefulRemovalPolicy);
            NagHelper.Suppress(ApplicationSecret, "AwsSolutions-SMG4",
                "Tasks read the password only at start and the bootstrap container sets it on the role at every task start, so it is rotated by changing the secret and redeploying the service (see infra/README.md).");

            NagHelper.Suppress(Database, "AwsSolutions-RDS11",
                "PostgreSQL keeps its default port. The instance is in isolated subnets and only the API task security group can reach it, so a non-default port adds no protection.");
            NagHelper.Suppress(Database, "AwsSolutions-SMG4",
                "Automatic rotation would change the password under running tasks, which read it only at start. Rotate manually and redeploy the service (see infra/README.md).");
            if (!settings.MultiAz)
            {
                NagHelper.Suppress(Database, "AwsSolutions-RDS3",
                    "Development runs a single-AZ instance to save cost. Production is Multi-AZ.");
            }

            if (!settings.DeletionProtection)
            {
                NagHelper.Suppress(Database, "AwsSolutions-RDS10",
                    "Development allows deletion so the environment can be torn down. Production enables deletion protection; both snapshot on delete.");
            }

            new CfnOutput(this, "DatabaseEndpoint", new CfnOutputProps { Value = Database.DbInstanceEndpointAddress });
            new CfnOutput(this, "DatabaseSecretName", new CfnOutputProps { Value = config.ResourcePath("database") });
            new CfnOutput(this, "ApplicationSecretName", new CfnOutputProps { Value = config.ResourcePath("database-app") });
            new CfnOutput(this, "PeakServerConnections", new CfnOutputProps
            {
                Value = config.PeakServerDatabaseConnections.ToString(CultureInfo.InvariantCulture),
                Description = "Server connections at the maximum task count (pool plus one realtime LISTEN connection per task); keep below max_connections with headroom."
            });
        }
    }
}
