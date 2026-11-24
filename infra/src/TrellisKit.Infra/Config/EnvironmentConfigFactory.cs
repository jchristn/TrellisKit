namespace TrellisKit.Infra.Config
{
    using System;
    using System.Collections.Generic;
    using Amazon.CDK.AWS.EC2;
    using Amazon.CDK.AWS.Logs;
    using Constructs;

    /// <summary>
    /// Builds the typed configuration for dev and prod, applying overrides from CDK context.
    /// Thread safety: stateless; returns a new object on every call.
    /// </summary>
    public static class EnvironmentConfigFactory
    {
        /// <summary>
        /// Context key that selects the environment (dev or prod).
        /// </summary>
        public static readonly string EnvironmentContextKey = "env";

        /// <summary>
        /// Context key that overrides the AWS account id.
        /// </summary>
        public static readonly string AccountContextKey = "account";

        /// <summary>
        /// Context key prefix of per-environment account ids, for example account:prod. The property's
        /// deploy/infra.json accounts map is used when neither this nor the account value is set.
        /// </summary>
        public static readonly string AccountMapContextPrefix = "account:";

        /// <summary>
        /// Context key that selects the property directory (absolute, or relative to the engine root).
        /// Default <see cref="PropertyDefinition.DefaultDirectory"/>.
        /// </summary>
        public static readonly string PropertyDirContextKey = "propertyDir";

        /// <summary>
        /// Context key that selects a deployment file (absolute, or relative to the engine root) listing the topology
        /// and the property folders to deploy. Cannot be combined with <see cref="PropertyDirContextKey"/>. Without it,
        /// the single property from propertyDir is deployed in the per-property topology.
        /// </summary>
        public static readonly string DeploymentContextKey = "deployment";

        /// <summary>
        /// Context key that overrides the server image tag.
        /// </summary>
        public static readonly string ImageTagContextKey = "imageTag";

        /// <summary>
        /// Context key that overrides the alarm email address.
        /// </summary>
        public static readonly string AlarmEmailContextKey = "alarmEmail";

        /// <summary>
        /// Context key that overrides the media signing public key (PEM).
        /// </summary>
        public static readonly string MediaSigningPublicKeyContextKey = "mediaSigningPublicKeyPem";

        /// <summary>
        /// Context key with the comma-separated IP addresses and CIDR ranges passed as TK_ADDITIONAL_TRUSTED_PROXIES,
        /// normally the CloudFront origin-facing ranges (see infra/README.md). Unset leaves the live setting unset.
        /// </summary>
        public static readonly string AdditionalTrustedProxiesContextKey = "additionalTrustedProxies";

        /// <summary>
        /// Context key that turns the RecallDB service on or off (true or false), overriding ai.recallDb in the
        /// property's deploy/infra.json.
        /// </summary>
        public static readonly string RecallDbContextKey = "recallDb";

        /// <summary>
        /// Context key with comma-separated https origins that may frame the assistant embed page, replacing
        /// ai.embedFrameAncestors in the property's deploy/infra.json.
        /// </summary>
        public static readonly string EmbedFrameAncestorsContextKey = "embedFrameAncestors";

        /// <summary>
        /// Context key that overrides the id of the CloudFront origin-facing managed prefix list of the region.
        /// </summary>
        public static readonly string CloudFrontPrefixListContextKey = "cloudFrontPrefixListId";

        /// <summary>
        /// Context key that selects the DNS mode (Route53 or External) of every property, overriding dns.mode in the
        /// deployment file and in each property's deploy/infra.json.
        /// </summary>
        public static readonly string DnsModeContextKey = "dnsMode";

        /// <summary>
        /// Placeholder account of dev when no account is configured.
        /// </summary>
        public static readonly string DevPlaceholderAccount = "111111111111";

        /// <summary>
        /// Placeholder account of prod when no account is configured.
        /// </summary>
        public static readonly string ProdPlaceholderAccount = "222222222222";

        /// <summary>
        /// Build the configuration selected by the env context value.
        /// </summary>
        /// <param name="scope">The CDK app (or any construct) whose context is read.</param>
        /// <returns>Configuration.</returns>
        /// <exception cref="ArgumentNullException">The scope is null.</exception>
        /// <exception cref="ArgumentException">The env context value is missing or unknown.</exception>
        public static EnvironmentConfig FromContext(Construct scope)
        {
            ArgumentNullException.ThrowIfNull(scope);

            string name = RequireEnvironment(scope);
            PropertyDefinition property = PropertyDefinition.Load(PropertyDefinition.ResolveDirectory(ReadContext(scope, PropertyDirContextKey)));
            EnvironmentConfig config = Create(name, ResolveAccount(scope, name, property), property);
            if (property.Dns != null) config.Dns = property.Dns.Clone();
            config.SharedAccount = config.Account == ResolveOtherAccount(scope, name, null, property);
            ApplyOverrides(scope, config);
            return config;
        }

        /// <summary>
        /// Build the deployment selected by context: the deployment file named by the deployment value, or else the
        /// single property named by propertyDir in the per-property topology.
        /// </summary>
        /// <param name="scope">The CDK app (or any construct) whose context is read.</param>
        /// <returns>Deployment configuration.</returns>
        /// <exception cref="ArgumentNullException">The scope is null.</exception>
        /// <exception cref="ArgumentException">The context is missing, conflicting, or invalid.</exception>
        public static DeploymentConfig DeploymentFromContext(Construct scope)
        {
            ArgumentNullException.ThrowIfNull(scope);

            string? file = ReadContext(scope, DeploymentContextKey);
            if (String.IsNullOrWhiteSpace(file))
            {
                DeploymentConfig single = new DeploymentConfig();
                single.Properties.Add(FromContext(scope));
                return single;
            }

            if (!String.IsNullOrWhiteSpace(ReadContext(scope, PropertyDirContextKey)))
            {
                throw new ArgumentException("Pass either -c deployment=<file> or -c propertyDir=<folder>, not both.", nameof(scope));
            }

            string name = RequireEnvironment(scope);
            DeploymentDefinition definition = DeploymentDefinition.Load(PropertyDefinition.ResolveDirectory(file));
            List<PropertyDefinition> properties = new List<PropertyDefinition>();
            foreach (string directory in definition.PropertyDirectories) properties.Add(PropertyDefinition.Load(directory));

            string? account = ReadContext(scope, AccountContextKey);
            if (String.IsNullOrWhiteSpace(account)) account = ReadContext(scope, AccountMapContextPrefix + name);
            DeploymentConfig deployment = CreateDeployment(name, account, definition, properties);
            foreach (EnvironmentConfig config in deployment.All)
            {
                config.SharedAccount = config.Account == ResolveOtherAccount(scope, name, definition, config.Property);
                ApplyOverrides(scope, config);
            }

            return deployment;
        }

        /// <summary>
        /// Build a deployment from a loaded deployment file and its properties.
        /// </summary>
        /// <param name="name">dev or prod.</param>
        /// <param name="account">Account id that overrides every file value, or null.</param>
        /// <param name="definition">Deployment file.</param>
        /// <param name="properties">Properties, in deployment file order.</param>
        /// <returns>Deployment configuration.</returns>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        /// <exception cref="ArgumentException">The properties collide (ids or domains), or the name or an account is invalid.</exception>
        public static DeploymentConfig CreateDeployment(string name, string? account, DeploymentDefinition definition, List<PropertyDefinition> properties)
        {
            ArgumentNullException.ThrowIfNull(definition);
            ArgumentNullException.ThrowIfNull(properties);
            if (properties.Count == 0) throw new ArgumentException("A deployment needs at least one property.", nameof(properties));

            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> slugs = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> domains = new HashSet<string>(StringComparer.Ordinal);
            foreach (PropertyDefinition property in properties)
            {
                if (!ids.Add(property.Id)) throw new ArgumentException("Property id '" + property.Id + "' appears twice in " + definition.File + ".", nameof(properties));
                if (!slugs.Add(property.Slug))
                {
                    throw new ArgumentException("Property slug '" + property.Slug + "' appears twice in " + definition.File + " (stack and resource names are built from the slug).", nameof(properties));
                }

                HashSet<string> owned = PropertyHosts(property);
                foreach (string domain in owned)
                {
                    if (!domains.Add(domain)) throw new ArgumentException("Domain '" + domain + "' belongs to two properties of " + definition.File + ".", nameof(properties));
                }
            }

            DeploymentConfig deployment = new DeploymentConfig();
            deployment.Topology = definition.Topology;

            string? deploymentAccount = !String.IsNullOrWhiteSpace(account) ? account : null;
            if (deploymentAccount == null && definition.Accounts.TryGetValue(name, out string? mapped)) deploymentAccount = mapped;

            if (definition.Topology == DeploymentTopologyEnum.PerProperty)
            {
                foreach (PropertyDefinition property in properties)
                {
                    string? propertyAccount = deploymentAccount;
                    if (propertyAccount == null && property.Accounts.TryGetValue(name, out string? own)) propertyAccount = own;
                    EnvironmentConfig config = Create(name, propertyAccount, property);
                    ApplyDns(config, definition, property);
                    config.SharedAccount = config.Account == ResolveOtherAccount(null, name, definition, property);
                    deployment.Properties.Add(config);
                }

                return deployment;
            }

            if (slugs.Contains(definition.Id))
            {
                throw new ArgumentException("The deployment id '" + definition.Id + "' in " + definition.File + " must differ from every property slug.", nameof(definition));
            }

            if (deploymentAccount == null && properties[0].Accounts.TryGetValue(name, out string? first)) deploymentAccount = first;

            List<string> origins = new List<string>();
            List<string> platformHosts = new List<string>();
            foreach (PropertyDefinition property in properties)
            {
                EnvironmentConfig config = Create(name, deploymentAccount, property);
                config.Topology = DeploymentTopologyEnum.Shared;
                ApplyDns(config, definition, property);
                deployment.Properties.Add(config);
                foreach (string origin in config.WebOrigins)
                {
                    if (!origins.Contains(origin)) origins.Add(origin);
                }
            }

            foreach (string host in definition.PlatformHosts)
            {
                platformHosts.Add(ResolvePlatformHost(host, definition, deployment.Properties));
            }

            EnvironmentConfig shared = Create(name, deploymentAccount, properties[0]);
            shared.Topology = DeploymentTopologyEnum.Shared;
            shared.DeploymentId = definition.Id;
            shared.DisplayName = definition.Name;
            shared.ResourcePrefix = definition.Id + "-" + name;
            shared.StackPrefix = definition.Id + "-" + name;
            shared.Database.InstanceIdentifier = definition.Id + "-" + name;
            shared.WebOrigins = origins;
            shared.Platform.Hosts = platformHosts;
            shared.Platform.BootstrapAdminEmail = definition.PlatformBootstrapAdminEmail;
            shared.Mail = definition.Mail;
            ApplyDns(shared, definition, properties[0]);
            string otherAccount = ResolveOtherAccount(null, name, definition, properties[0]);
            shared.SharedAccount = shared.Account == otherAccount;
            foreach (EnvironmentConfig config in deployment.Properties) config.SharedAccount = shared.SharedAccount;
            if (!String.IsNullOrWhiteSpace(definition.AlarmEmail))
            {
                shared.Observability.AlarmEmail = definition.AlarmEmail;
                shared.Budget.NotificationEmails = new List<string> { definition.AlarmEmail };
            }

            if (!String.IsNullOrWhiteSpace(definition.GitHubOwner)) shared.GitHub.Owner = definition.GitHubOwner;
            if (!String.IsNullOrWhiteSpace(definition.GitHubRepository)) shared.GitHub.Repository = definition.GitHubRepository;
            deployment.Shared = shared;
            return deployment;
        }

        /// <summary>
        /// Build the configuration for a named environment.
        /// </summary>
        /// <param name="name">dev or prod.</param>
        /// <param name="account">Twelve-digit account id, or null for the environment's placeholder.</param>
        /// <param name="property">Property to deploy.</param>
        /// <returns>Configuration.</returns>
        /// <exception cref="ArgumentNullException">The property is null.</exception>
        /// <exception cref="ArgumentException">The name is not dev or prod, or the account is malformed.</exception>
        public static EnvironmentConfig Create(string name, string? account, PropertyDefinition property)
        {
            ArgumentNullException.ThrowIfNull(property);
            EnvironmentConfig config;
            if (name == "dev") config = Dev(property);
            else if (name == "prod") config = Prod(property);
            else throw new ArgumentException("Unknown environment '" + name + "'. Use dev or prod.", nameof(name));

            if (!String.IsNullOrWhiteSpace(account)) config.Account = account;
            return config;
        }

        /// <summary>
        /// Development of the default property: smallest sizes, single NAT gateway, single-AZ database, subdomains of the production zones.
        /// </summary>
        /// <returns>Configuration.</returns>
        public static EnvironmentConfig Dev()
        {
            return Dev(PropertyDefinition.LoadDefault());
        }

        /// <summary>
        /// Development configuration of a property: its domains prefixed with dev.
        /// </summary>
        /// <param name="property">Property.</param>
        /// <returns>Configuration.</returns>
        /// <exception cref="ArgumentNullException">The property is null.</exception>
        public static EnvironmentConfig Dev(PropertyDefinition property)
        {
            EnvironmentConfig config = Base(property, "dev", "dev.");
            config.Account = DevPlaceholderAccount;
            config.IsProduction = false;

            config.Network.CidrBlock = "10.40.0.0/16";
            config.Network.NatGateways = 1;

            config.Database.InstanceClass = InstanceClass.BURSTABLE4_GRAVITON;
            config.Database.InstanceSize = InstanceSize.MICRO;
            config.Database.MultiAz = false;
            config.Database.DeletionProtection = false;
            config.Database.AllocatedStorageGiB = 20;
            config.Database.MaxAllocatedStorageGiB = 50;
            config.Database.BackupRetentionDays = 3;
            config.Database.MaxPoolSize = 20;

            config.Api.TaskCpu = 512;
            config.Api.TaskMemoryMiB = 1024;
            config.Api.MinTasks = 1;
            config.Api.MaxTasks = 2;
            config.Api.LoadBalancerDeletionProtection = false;

            config.Observability.LogRetention = RetentionDays.TWO_WEEKS;
            config.Storage.AccessLogExpirationDays = 30;

            config.Budget.MonthlyLimitUsd = 120;
            config.GitHub.DeploymentEnvironment = "dev";
            return config;
        }

        /// <summary>
        /// Production of the default property: two NAT gateways, Multi-AZ database with deletion protection, at least two tasks.
        /// </summary>
        /// <returns>Configuration.</returns>
        public static EnvironmentConfig Prod()
        {
            return Prod(PropertyDefinition.LoadDefault());
        }

        /// <summary>
        /// Production configuration of a property: its domains as configured.
        /// </summary>
        /// <param name="property">Property.</param>
        /// <returns>Configuration.</returns>
        /// <exception cref="ArgumentNullException">The property is null.</exception>
        public static EnvironmentConfig Prod(PropertyDefinition property)
        {
            EnvironmentConfig config = Base(property, "prod", String.Empty);
            config.Account = ProdPlaceholderAccount;
            config.IsProduction = true;

            config.Network.CidrBlock = "10.50.0.0/16";
            config.Network.NatGateways = 2;

            config.Database.InstanceClass = InstanceClass.BURSTABLE4_GRAVITON;
            config.Database.InstanceSize = InstanceSize.SMALL;
            config.Database.MultiAz = true;
            config.Database.DeletionProtection = true;
            config.Database.AllocatedStorageGiB = 50;
            config.Database.MaxAllocatedStorageGiB = 200;
            config.Database.BackupRetentionDays = 14;
            config.Database.MaxPoolSize = 20;

            config.Api.TaskCpu = 1024;
            config.Api.TaskMemoryMiB = 2048;
            config.Api.MinTasks = 2;
            config.Api.MaxTasks = 6;
            config.Api.LoadBalancerDeletionProtection = true;

            config.Observability.LogRetention = RetentionDays.THREE_MONTHS;
            config.Storage.AccessLogExpirationDays = 365;

            config.Budget.MonthlyLimitUsd = 300;
            config.GitHub.DeploymentEnvironment = "prod";
            return config;
        }

        private static EnvironmentConfig Base(PropertyDefinition property, string name, string domainPrefix)
        {
            ArgumentNullException.ThrowIfNull(property);

            EnvironmentConfig config = new EnvironmentConfig();
            config.Property = property;
            config.Name = name;
            config.ResourcePrefix = property.Slug + "-" + name;
            config.StackPrefix = property.Slug + "-" + name;

            config.Domains.PrimaryDomain = domainPrefix + property.WebDomain;
            config.Domains.ApiOnWebsiteHost = property.ApiDomain == property.WebDomain;
            config.Domains.ApiLabel = property.LabelOf(property.ApiOriginDomain);
            config.Domains.MediaLabel = property.LabelOf(property.MediaDomain);
            List<string> redirects = new List<string>();
            foreach (string redirect in property.RedirectDomains) redirects.Add(domainPrefix + redirect);
            config.Domains.RedirectDomains = redirects;
            config.Domains.RedirectTargetUrl = "https://" + domainPrefix + property.WebDomain;

            config.Database.InstanceIdentifier = property.Slug + "-" + name;
            config.Api.SmtpFromLocalPart = property.EmailFromLocalPart;
            config.Mail = property.Mail;
            config.Ai = property.Ai;
            config.Location = property.Location;
            config.Partio = property.Partio;

            string alarmEmail = String.IsNullOrWhiteSpace(property.AlarmEmail) ? property.SupportEmail : property.AlarmEmail;
            config.Observability.AlarmEmail = alarmEmail;
            config.Budget.NotificationEmails = new List<string> { alarmEmail };

            if (!String.IsNullOrWhiteSpace(property.GitHubOwner)) config.GitHub.Owner = property.GitHubOwner;
            if (!String.IsNullOrWhiteSpace(property.GitHubRepository)) config.GitHub.Repository = property.GitHubRepository;
            return config;
        }

        private static HashSet<string> PropertyHosts(PropertyDefinition property)
        {
            HashSet<string> owned = new HashSet<string>(property.RedirectDomains, StringComparer.Ordinal)
            {
                property.WebDomain, property.ApiDomain, property.ApiOriginDomain, property.MediaDomain,
                "www." + property.WebDomain, "dev." + property.WebDomain, "www.dev." + property.WebDomain,
                property.LabelOf(property.ApiOriginDomain) + ".dev." + property.WebDomain,
                property.LabelOf(property.MediaDomain) + ".dev." + property.WebDomain
            };
            foreach (string redirect in property.RedirectDomains)
            {
                owned.Add("www." + redirect);
                owned.Add("dev." + redirect);
                owned.Add("www.dev." + redirect);
            }

            return owned;
        }

        private static string ResolvePlatformHost(string host, DeploymentDefinition definition, List<EnvironmentConfig> configs)
        {
            foreach (EnvironmentConfig config in configs)
            {
                PropertyDefinition property = config.Property;
                string suffix = "." + property.WebDomain;
                if (!host.EndsWith(suffix, StringComparison.Ordinal)) continue;
                string label = host.Substring(0, host.Length - suffix.Length);
                string developmentHost = label + ".dev." + property.WebDomain;
                foreach (EnvironmentConfig owner in configs)
                {
                    HashSet<string> taken = PropertyHosts(owner.Property);
                    if (taken.Contains(host) || taken.Contains(developmentHost))
                    {
                        throw new ArgumentException("Platform host '" + host + "' in " + definition.File
                            + " is already a host of property " + owner.Property.Slug + " in production or development.", nameof(definition));
                    }
                }

                return label + "." + config.Domains.PrimaryDomain;
            }

            throw new ArgumentException("Platform host '" + host + "' in " + definition.File + " must be a subdomain of the web domain of one listed property (its hosted zone holds the record and certificate validation).", nameof(definition));
        }

        private static string RequireEnvironment(Construct scope)
        {
            string? name = ReadContext(scope, EnvironmentContextKey);
            if (String.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Missing context value 'env'. Pass -c env=dev or -c env=prod.", nameof(scope));
            }

            return name;
        }

        private static string? ResolveAccount(Construct scope, string name, PropertyDefinition property)
        {
            string? account = ReadContext(scope, AccountContextKey);
            if (String.IsNullOrWhiteSpace(account)) account = ReadContext(scope, AccountMapContextPrefix + name);
            if (String.IsNullOrWhiteSpace(account) && property.Accounts.TryGetValue(name, out string? mapped)) account = mapped;
            return account;
        }

        /// <summary>
        /// The account the other environment (prod for dev, dev for prod) resolves to without the account context
        /// value, which applies only to the environment being synthesized: account:&lt;other&gt; from context, else the
        /// deployment file's accounts, else the property's deploy/infra.json, else the other environment's placeholder.
        /// </summary>
        /// <param name="scope">Construct whose context is read, or null to read files only.</param>
        /// <param name="name">The environment being synthesized (dev or prod).</param>
        /// <param name="definition">Deployment file, or null.</param>
        /// <param name="property">Property (the first one of a shared deployment).</param>
        /// <returns>Twelve-digit account id.</returns>
        /// <exception cref="ArgumentNullException">The property is null.</exception>
        public static string ResolveOtherAccount(Construct? scope, string name, DeploymentDefinition? definition, PropertyDefinition property)
        {
            ArgumentNullException.ThrowIfNull(property);
            string other = name == "prod" ? "dev" : "prod";
            string? account = scope != null ? ReadContext(scope, AccountMapContextPrefix + other) : null;
            if (String.IsNullOrWhiteSpace(account) && definition != null && definition.Accounts.TryGetValue(other, out string? fromFile)) account = fromFile;
            if (String.IsNullOrWhiteSpace(account) && property.Accounts.TryGetValue(other, out string? fromProperty)) account = fromProperty;
            if (String.IsNullOrWhiteSpace(account)) account = other == "prod" ? ProdPlaceholderAccount : DevPlaceholderAccount;
            return account;
        }

        private static void ApplyDns(EnvironmentConfig config, DeploymentDefinition definition, PropertyDefinition property)
        {
            if (definition.Dns != null) config.Dns = definition.Dns.Clone();
            else if (property.Dns != null) config.Dns = property.Dns.Clone();
        }

        private static void ApplyOverrides(Construct scope, EnvironmentConfig config)
        {
            string? imageTag = ReadContext(scope, ImageTagContextKey);
            if (!String.IsNullOrWhiteSpace(imageTag)) config.Api.ImageTag = imageTag;

            string? alarmEmail = ReadContext(scope, AlarmEmailContextKey);
            if (!String.IsNullOrWhiteSpace(alarmEmail))
            {
                config.Observability.AlarmEmail = alarmEmail;
                config.Budget.NotificationEmails = new List<string> { alarmEmail };
            }

            string? publicKey = ReadContext(scope, MediaSigningPublicKeyContextKey);
            if (!String.IsNullOrWhiteSpace(publicKey)) config.Media.SigningPublicKeyPem = publicKey.Replace("\\n", "\n");

            string? proxies = ReadContext(scope, AdditionalTrustedProxiesContextKey);
            if (!String.IsNullOrWhiteSpace(proxies))
            {
                config.Network.AdditionalTrustedProxies = new List<string>(proxies.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }

            string? recallDb = ReadContext(scope, RecallDbContextKey);
            if (!String.IsNullOrWhiteSpace(recallDb))
            {
                if (!Boolean.TryParse(recallDb.Trim(), out bool enabled))
                {
                    throw new ArgumentException("Context value 'recallDb' must be true or false, got '" + recallDb + "'.", nameof(scope));
                }

                config.Ai.RecallDbEnabled = enabled;
            }

            string? ancestors = ReadContext(scope, EmbedFrameAncestorsContextKey);
            if (!String.IsNullOrWhiteSpace(ancestors))
            {
                config.Ai.EmbedFrameAncestors = new List<string>(ancestors.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }

            string? prefixList = ReadContext(scope, CloudFrontPrefixListContextKey);
            if (!String.IsNullOrWhiteSpace(prefixList)) config.Network.CloudFrontOriginFacingPrefixListId = prefixList;

            string? dnsMode = ReadContext(scope, DnsModeContextKey);
            if (!String.IsNullOrWhiteSpace(dnsMode)) config.Dns.Mode = DnsSettings.ParseMode(dnsMode, "context value '" + DnsModeContextKey + "'");
        }

        private static string? ReadContext(Construct scope, string key)
        {
            object? value = scope.Node.TryGetContext(key);
            if (value == null) return null;
            return value.ToString();
        }
    }
}
