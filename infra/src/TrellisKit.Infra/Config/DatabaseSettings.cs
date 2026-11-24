namespace TrellisKit.Infra.Config
{
    using System;
    using System.Text.RegularExpressions;
    using Amazon.CDK.AWS.EC2;

    /// <summary>
    /// RDS PostgreSQL sizing and protection.
    /// </summary>
    public class DatabaseSettings
    {
        private static readonly Regex _RolePattern = new Regex("^[a-z_][a-z0-9_]{0,62}$");

        private string _InstanceIdentifier = "trelliskit";
        private string _EngineMajorVersion = "17";
        private string _DatabaseName = "trelliskit";
        private string _MasterUsername = "trelliskit";
        private string _ApplicationUsername = "trelliskit_app";
        private string _SearchSchema = "search";
        private string _BootstrapImage = "public.ecr.aws/docker/library/postgres:17-alpine";
        private int _BootstrapMemoryMiB = 128;
        private int _BootstrapWaitSeconds = 300;
        private int _Port = 5432;
        private int _AllocatedStorageGiB = 20;
        private int _MaxAllocatedStorageGiB = 100;
        private int _BackupRetentionDays = 7;
        private string _PreferredBackupWindow = "09:00-09:30";
        private string _PreferredMaintenanceWindow = "Sun:10:00-Sun:10:30";
        private int _SlowQueryMilliseconds = 1000;
        private int _MaxPoolSize = 50;

        /// <summary>
        /// RDS instance identifier. Fixed (not generated) so the PostgreSQL log group name is known in advance.
        /// Default trelliskit.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string InstanceIdentifier
        {
            get { return _InstanceIdentifier; }
            set { _InstanceIdentifier = Guard.NotEmpty(value, nameof(InstanceIdentifier)); }
        }

        /// <summary>
        /// PostgreSQL major version. Default 17. Only the major version is pinned so automatic minor upgrades
        /// never conflict with the template.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string EngineMajorVersion
        {
            get { return _EngineMajorVersion; }
            set { _EngineMajorVersion = Guard.NotEmpty(value, nameof(EngineMajorVersion)); }
        }

        /// <summary>
        /// Instance class. Default BURSTABLE4_GRAVITON (db.t4g).
        /// </summary>
        public InstanceClass InstanceClass { get; set; } = InstanceClass.BURSTABLE4_GRAVITON;

        /// <summary>
        /// Instance size. Default MICRO.
        /// </summary>
        public InstanceSize InstanceSize { get; set; } = InstanceSize.MICRO;

        /// <summary>
        /// Run a synchronous standby in the second AZ. Default false. Roughly doubles the instance cost.
        /// </summary>
        public bool MultiAz { get; set; } = false;

        /// <summary>
        /// Block deletion of the instance through the API or CloudFormation. Default false.
        /// </summary>
        public bool DeletionProtection { get; set; } = false;

        /// <summary>
        /// Initial database created on the instance. Default trelliskit.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string DatabaseName
        {
            get { return _DatabaseName; }
            set { _DatabaseName = Guard.NotEmpty(value, nameof(DatabaseName)); }
        }

        /// <summary>
        /// Master user name (rds_superuser; used only by the bootstrap container, never by the server). The password is
        /// generated into Secrets Manager. Default trelliskit.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string MasterUsername
        {
            get { return _MasterUsername; }
            set { _MasterUsername = Guard.NotEmpty(value, nameof(MasterUsername)); }
        }

        /// <summary>
        /// PostgreSQL role the server connects as (TK_DB_USERNAME): LOGIN, NOSUPERUSER, NOCREATEDB, NOCREATEROLE, with
        /// CONNECT, TEMPORARY, and CREATE on the database (CREATE only because the server runs CREATE SCHEMA IF NOT EXISTS
        /// for its search schema, which PostgreSQL checks even when the schema exists), USAGE and CREATE on schema public
        /// (it owns the tables its migrations create), and ownership of <see cref="SearchSchema"/>. Created by the database bootstrap container with the master user; its password is generated into
        /// Secrets Manager. Lowercase letters, digits, and underscores; must differ from the master user. Default trelliskit_app.
        /// </summary>
        /// <exception cref="ArgumentException">The value is not a simple lowercase role name.</exception>
        public string ApplicationUsername
        {
            get { return _ApplicationUsername; }
            set
            {
                if (value == null || !_RolePattern.IsMatch(value))
                {
                    throw new ArgumentException("ApplicationUsername must be lowercase letters, digits, and underscores (1 to 63 characters), got '" + value + "'.", nameof(ApplicationUsername));
                }

                _ApplicationUsername = value;
            }
        }

        /// <summary>
        /// Schema of the Verbex search index tables (backend setting search.schema, whose empty default resolves to
        /// search for the default engine schema). The bootstrap container creates it owned by the application role.
        /// Lowercase letters, digits, and underscores. Default search.
        /// </summary>
        /// <exception cref="ArgumentException">The value is not a simple lowercase identifier.</exception>
        public string SearchSchema
        {
            get
            {
                return _SearchSchema;
            }
            set
            {
                if (value == null || !_RolePattern.IsMatch(value))
                {
                    throw new ArgumentException("SearchSchema must be lowercase letters, digits, and underscores (1 to 63 characters), got '" + value + "'.", nameof(SearchSchema));
                }

                _SearchSchema = value;
            }
        }

        /// <summary>
        /// Image of the database bootstrap container (needs psql and pg_isready) that runs before the server in every
        /// task: as the master user it creates the pgvector extension and the application role and grants its rights.
        /// Default public.ecr.aws/docker/library/postgres:17-alpine (the engine's PostgreSQL major version).
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string BootstrapImage
        {
            get { return _BootstrapImage; }
            set { _BootstrapImage = Guard.NotEmpty(value, nameof(BootstrapImage)); }
        }

        /// <summary>
        /// Memory reservation of the bootstrap container in MiB. Default 128. Minimum 32, maximum 1024.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int BootstrapMemoryMiB
        {
            get { return _BootstrapMemoryMiB; }
            set { _BootstrapMemoryMiB = Guard.Range(value, 32, 1024, nameof(BootstrapMemoryMiB)); }
        }

        /// <summary>
        /// Seconds the bootstrap container waits for the database to accept connections before it fails the task.
        /// Default 300. Minimum 10, maximum 1800.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int BootstrapWaitSeconds
        {
            get { return _BootstrapWaitSeconds; }
            set { _BootstrapWaitSeconds = Guard.Range(value, 10, 1800, nameof(BootstrapWaitSeconds)); }
        }

        /// <summary>
        /// Listener port. Default 5432. Minimum 1150, maximum 65535.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int Port
        {
            get { return _Port; }
            set { _Port = Guard.Range(value, 1150, 65535, nameof(Port)); }
        }

        /// <summary>
        /// Initial gp3 storage in GiB. Default 20. Minimum 20, maximum 65536.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int AllocatedStorageGiB
        {
            get { return _AllocatedStorageGiB; }
            set { _AllocatedStorageGiB = Guard.Range(value, 20, 65536, nameof(AllocatedStorageGiB)); }
        }

        /// <summary>
        /// Upper bound for storage autoscaling in GiB. Default 100. Minimum 20, maximum 65536.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int MaxAllocatedStorageGiB
        {
            get { return _MaxAllocatedStorageGiB; }
            set { _MaxAllocatedStorageGiB = Guard.Range(value, 20, 65536, nameof(MaxAllocatedStorageGiB)); }
        }

        /// <summary>
        /// Automated backup and point-in-time recovery retention in days. Default 7. Minimum 1, maximum 35.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int BackupRetentionDays
        {
            get { return _BackupRetentionDays; }
            set { _BackupRetentionDays = Guard.Range(value, 1, 35, nameof(BackupRetentionDays)); }
        }

        /// <summary>
        /// Daily backup window in UTC (hh24:mi-hh24:mi). Default 09:00-09:30 (early morning Pacific).
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string PreferredBackupWindow
        {
            get { return _PreferredBackupWindow; }
            set { _PreferredBackupWindow = Guard.NotEmpty(value, nameof(PreferredBackupWindow)); }
        }

        /// <summary>
        /// Weekly maintenance window in UTC (ddd:hh24:mi-ddd:hh24:mi). Default Sun:10:00-Sun:10:30.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string PreferredMaintenanceWindow
        {
            get { return _PreferredMaintenanceWindow; }
            set { _PreferredMaintenanceWindow = Guard.NotEmpty(value, nameof(PreferredMaintenanceWindow)); }
        }

        /// <summary>
        /// Statements slower than this are logged (log_min_duration_statement). Default 1000. Minimum -1 (off), maximum 600000.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int SlowQueryMilliseconds
        {
            get { return _SlowQueryMilliseconds; }
            set { _SlowQueryMilliseconds = Guard.Range(value, -1, 600000, nameof(SlowQueryMilliseconds)); }
        }

        /// <summary>
        /// Connections per server task (TK_DB_MAX_POOL_SIZE). Keep this times the maximum task count below the
        /// instance's max_connections. Default 50. Minimum 1, maximum 1000.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int MaxPoolSize
        {
            get { return _MaxPoolSize; }
            set { _MaxPoolSize = Guard.Range(value, 1, 1000, nameof(MaxPoolSize)); }
        }
    }
}
