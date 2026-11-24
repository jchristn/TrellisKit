namespace TrellisKit.Infra.Config
{
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using Amazon.CDK;

    /// <summary>
    /// Every name and size used by one deployment environment. Stacks read only from this object, so there are
    /// no magic numbers in stack code. Build instances with <see cref="EnvironmentConfigFactory"/>.
    /// </summary>
    public class EnvironmentConfig
    {
        private static readonly Regex _AccountPattern = new Regex("^[0-9]{12}$");

        private string _Name = "dev";
        private string _Account = "111111111111";
        private string _Region = "us-west-2";
        private string _EdgeRegion = "us-east-1";
        private string _ResourcePrefix = "example-dev";
        private string _StackPrefix = "example-dev";
        private PropertyDefinition? _Property = null;
        private DomainSettings _Domains = new DomainSettings();
        private NetworkSettings _Network = new NetworkSettings();
        private DatabaseSettings _Database = new DatabaseSettings();
        private StorageSettings _Storage = new StorageSettings();
        private MediaSettings _Media = new MediaSettings();
        private ApiSettings _Api = new ApiSettings();
        private WebSettings _Web = new WebSettings();
        private EmailSettings _Email = new EmailSettings();
        private ObservabilitySettings _Observability = new ObservabilitySettings();
        private BudgetSettings _Budget = new BudgetSettings();
        private GitHubSettings _GitHub = new GitHubSettings();
        private UpdatesSettings _Updates = new UpdatesSettings();
        private AiSettings _Ai = new AiSettings();
        private LocationSettings _Location = new LocationSettings();
        private PartioSettings _Partio = new PartioSettings();
        private PlatformSettings _Platform = new PlatformSettings();
        private MailSettings _Mail = new MailSettings();
        private DnsSettings _Dns = new DnsSettings();
        private string? _DeploymentId = null;
        private string? _DisplayName = null;
        private List<string> _WebOrigins = new List<string>();

        /// <summary>
        /// Environment name: dev or prod. Default dev.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string Name
        {
            get { return _Name; }
            set { _Name = Guard.NotEmpty(value, nameof(Name)); }
        }

        /// <summary>
        /// Twelve-digit AWS account id. Default 111111111111 (placeholder). Override with the account context value.
        /// </summary>
        /// <exception cref="ArgumentException">The value is not twelve digits.</exception>
        public string Account
        {
            get { return _Account; }
            set
            {
                if (value == null || !_AccountPattern.IsMatch(value))
                {
                    throw new ArgumentException("Account must be a twelve-digit AWS account id, got '" + value + "'.", nameof(Account));
                }

                _Account = value;
            }
        }

        /// <summary>
        /// Primary region. Default us-west-2.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string Region
        {
            get { return _Region; }
            set { _Region = Guard.NotEmpty(value, nameof(Region)); }
        }

        /// <summary>
        /// Region of CloudFront certificates and the budget. Must be us-east-1 for CloudFront. Default us-east-1.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string EdgeRegion
        {
            get { return _EdgeRegion; }
            set { _EdgeRegion = Guard.NotEmpty(value, nameof(EdgeRegion)); }
        }

        /// <summary>
        /// Lowercase prefix of physical resource names (buckets, cluster, load balancer). Set from the property slug (never the opaque property id, which has underscores and uppercase letters) and environment, for example example-dev.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string ResourcePrefix
        {
            get { return _ResourcePrefix; }
            set { _ResourcePrefix = Guard.NotEmpty(value, nameof(ResourcePrefix)); }
        }

        /// <summary>
        /// Prefix of CloudFormation stack names. Set from the property slug and environment, for example example-dev.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string StackPrefix
        {
            get { return _StackPrefix; }
            set { _StackPrefix = Guard.NotEmpty(value, nameof(StackPrefix)); }
        }

        /// <summary>
        /// The property this environment deploys (from the propertyDir context value).
        /// </summary>
        /// <exception cref="InvalidOperationException">No property was set.</exception>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public PropertyDefinition Property
        {
            get { return _Property ?? throw new InvalidOperationException("No property is set on the environment configuration."); }
            set { _Property = value ?? throw new ArgumentNullException(nameof(Property)); }
        }

        /// <summary>
        /// Topology of the deployment this configuration belongs to. Default <see cref="DeploymentTopologyEnum.PerProperty"/>.
        /// In the shared topology, the shared stacks and each property's stacks carry their own configuration.
        /// </summary>
        public DeploymentTopologyEnum Topology { get; set; } = DeploymentTopologyEnum.PerProperty;

        /// <summary>
        /// Cosmetic name of the owner of the stacks built from this configuration: the property slug (default), or the
        /// deployment id for the shared stacks of a shared deployment. Used in secret paths, the Project tag, and the
        /// server's deployment name (TK_DEPLOYMENT_NAME). Never the opaque property id.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string DeploymentId
        {
            get { return _DeploymentId ?? Property.Slug; }
            set { _DeploymentId = Guard.NotEmpty(value, nameof(DeploymentId)); }
        }

        /// <summary>
        /// Name used in stack and resource descriptions: the property name (default), or the deployment name for the
        /// shared stacks of a shared deployment.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string DisplayName
        {
            get { return _DisplayName ?? Property.Name; }
            set { _DisplayName = Guard.NotEmpty(value, nameof(DisplayName)); }
        }

        /// <summary>
        /// Browser origins allowed by upload CORS and the API: the website apex and www host of this environment by
        /// default, or the origins of every property for the shared stacks of a shared deployment. Setting an empty
        /// list restores the default.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public List<string> WebOrigins
        {
            get
            {
                if (_WebOrigins.Count > 0) return _WebOrigins;
                return new List<string> { _Domains.WebsiteUrl, "https://" + _Domains.WwwHost };
            }
            set { _WebOrigins = Guard.NotNull(value, nameof(WebOrigins)); }
        }

        /// <summary>
        /// True in the shared topology, for the shared stacks and for each property's stacks (the backend serves several properties).
        /// </summary>
        public bool IsShared
        {
            get { return Topology == DeploymentTopologyEnum.Shared; }
        }

        /// <summary>
        /// True when another environment of the same deployment (dev and prod of one property, or of one shared
        /// deployment) deploys into the same AWS account. Every physical name already carries the environment, so
        /// nothing collides; the flag scopes the cost budget to this environment's resources (see
        /// <see cref="BudgetSettings.CostTagValue"/>). Default false.
        /// </summary>
        public bool SharedAccount { get; set; } = false;

        /// <summary>
        /// True for production. Production retains stateful resources on stack deletion.
        /// </summary>
        public bool IsProduction { get; set; } = false;

        /// <summary>
        /// Removal policy of stateful resources (buckets, secrets, log groups, repository): RETAIN in production,
        /// DESTROY otherwise. The database always snapshots on deletion.
        /// </summary>
        public RemovalPolicy StatefulRemovalPolicy
        {
            get { return IsProduction ? RemovalPolicy.RETAIN : RemovalPolicy.DESTROY; }
        }

        /// <summary>
        /// DNS names.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public DomainSettings Domains
        {
            get { return _Domains; }
            set { _Domains = value ?? throw new ArgumentNullException(nameof(Domains)); }
        }

        /// <summary>
        /// VPC layout.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public NetworkSettings Network
        {
            get { return _Network; }
            set { _Network = value ?? throw new ArgumentNullException(nameof(Network)); }
        }

        /// <summary>
        /// RDS PostgreSQL.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public DatabaseSettings Database
        {
            get { return _Database; }
            set { _Database = value ?? throw new ArgumentNullException(nameof(Database)); }
        }

        /// <summary>
        /// S3 lifecycle.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public StorageSettings Storage
        {
            get { return _Storage; }
            set { _Storage = value ?? throw new ArgumentNullException(nameof(Storage)); }
        }

        /// <summary>
        /// Media CDN and transcoding.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public MediaSettings Media
        {
            get { return _Media; }
            set { _Media = value ?? throw new ArgumentNullException(nameof(Media)); }
        }

        /// <summary>
        /// Backend service.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public ApiSettings Api
        {
            get { return _Api; }
            set { _Api = value ?? throw new ArgumentNullException(nameof(Api)); }
        }

        /// <summary>
        /// Website distribution.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public WebSettings Web
        {
            get { return _Web; }
            set { _Web = value ?? throw new ArgumentNullException(nameof(Web)); }
        }

        /// <summary>
        /// SES.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public EmailSettings Email
        {
            get { return _Email; }
            set { _Email = value ?? throw new ArgumentNullException(nameof(Email)); }
        }

        /// <summary>
        /// Logs, metrics, and alarms.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public ObservabilitySettings Observability
        {
            get { return _Observability; }
            set { _Observability = value ?? throw new ArgumentNullException(nameof(Observability)); }
        }

        /// <summary>
        /// Monthly cost budget.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public BudgetSettings Budget
        {
            get { return _Budget; }
            set { _Budget = value ?? throw new ArgumentNullException(nameof(Budget)); }
        }

        /// <summary>
        /// GitHub Actions deploy role.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public GitHubSettings GitHub
        {
            get { return _GitHub; }
            set { _GitHub = value ?? throw new ArgumentNullException(nameof(GitHub)); }
        }

        /// <summary>
        /// AI infrastructure: optional RecallDB service and the assistant embed frame ancestors. Default off.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public AiSettings Ai
        {
            get { return _Ai; }
            set { _Ai = value ?? throw new ArgumentNullException(nameof(Ai)); }
        }

        /// <summary>
        /// Amazon Location Service place index and map of this property. Default none.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public LocationSettings Location
        {
            get { return _Location; }
            set { _Location = value ?? throw new ArgumentNullException(nameof(Location)); }
        }

        /// <summary>
        /// Optional Partio document text service (TK_PARTIO_BASE_URL and the {prefix}/partio secret). Default unset.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public PartioSettings Partio
        {
            get { return _Partio; }
            set { _Partio = value ?? throw new ArgumentNullException(nameof(Partio)); }
        }

        /// <summary>
        /// Database connections the server tasks can hold at the maximum task count: each task's pool
        /// (DatabaseSettings.MaxPoolSize) plus its one dedicated realtime bus LISTEN connection, times ApiSettings.MaxTasks.
        /// Keep it below the instance's max_connections with headroom for the bootstrap container, RecallDB, rolling
        /// deployments (up to twice the running tasks briefly), and RDS reserved connections. A Data stack output.
        /// </summary>
        public int PeakServerDatabaseConnections
        {
            get { return Api.MaxTasks * (Database.MaxPoolSize + 1); }
        }

        /// <summary>
        /// Live updates bucket and CDN paths.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public UpdatesSettings Updates
        {
            get { return _Updates; }
            set { _Updates = value ?? throw new ArgumentNullException(nameof(Updates)); }
        }

        /// <summary>
        /// Platform hosts and the first platform administrator (shared topology only; empty otherwise).
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public PlatformSettings Platform
        {
            get { return _Platform; }
            set { _Platform = value ?? throw new ArgumentNullException(nameof(Platform)); }
        }

        /// <summary>
        /// Deployment mail settings (provider and Mailgun domain and region), from the property's deploy/infra.json or,
        /// in the shared topology, the deployment file. Empty by default: nothing is passed to the server.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public MailSettings Mail
        {
            get { return _Mail; }
            set { _Mail = value ?? throw new ArgumentNullException(nameof(Mail)); }
        }

        /// <summary>
        /// DNS management: Route 53 hosted zones (default) or records the operator creates at an external provider.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public DnsSettings Dns
        {
            get { return _Dns; }
            set { _Dns = value ?? throw new ArgumentNullException(nameof(Dns)); }
        }

        /// <summary>
        /// CDK environment of the primary region.
        /// </summary>
        public Amazon.CDK.Environment PrimaryEnvironment
        {
            get { return new Amazon.CDK.Environment { Account = _Account, Region = _Region }; }
        }

        /// <summary>
        /// CDK environment of the edge region (us-east-1).
        /// </summary>
        public Amazon.CDK.Environment EdgeEnvironment
        {
            get { return new Amazon.CDK.Environment { Account = _Account, Region = _EdgeRegion }; }
        }

        /// <summary>
        /// Build a stack name from the stack prefix, for example example-prod-Network.
        /// </summary>
        /// <param name="suffix">Stack suffix.</param>
        /// <returns>Stack name.</returns>
        /// <exception cref="ArgumentNullException">The suffix is empty.</exception>
        public string StackName(string suffix)
        {
            return _StackPrefix + "-" + Guard.NotEmpty(suffix, nameof(suffix));
        }

        /// <summary>
        /// Build a physical resource name from the resource prefix, for example example-prod-media-source.
        /// </summary>
        /// <param name="suffix">Resource suffix.</param>
        /// <returns>Resource name.</returns>
        /// <exception cref="ArgumentNullException">The suffix is empty.</exception>
        public string ResourceName(string suffix)
        {
            return _ResourcePrefix + "-" + Guard.NotEmpty(suffix, nameof(suffix));
        }

        /// <summary>
        /// Build a globally unique bucket name, for example example-prod-website-222222222222.
        /// </summary>
        /// <param name="suffix">Bucket purpose.</param>
        /// <returns>Bucket name.</returns>
        /// <exception cref="ArgumentNullException">The suffix is empty.</exception>
        public string BucketName(string suffix)
        {
            return ResourceName(suffix) + "-" + _Account;
        }

        /// <summary>
        /// Build a slash-separated path such as a secret name from the deployment id, for example example/prod/server.
        /// </summary>
        /// <param name="suffix">Path suffix.</param>
        /// <returns>Path.</returns>
        /// <exception cref="ArgumentNullException">The suffix is empty.</exception>
        public string ResourcePath(string suffix)
        {
            return DeploymentId + "/" + _Name + "/" + Guard.NotEmpty(suffix, nameof(suffix));
        }
    }
}
