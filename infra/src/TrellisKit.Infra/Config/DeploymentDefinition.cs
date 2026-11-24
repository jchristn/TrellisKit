namespace TrellisKit.Infra.Config
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Text.RegularExpressions;

    /// <summary>
    /// A deployment context file: the topology and the property folders one synthesis deploys, plus optional
    /// deployment-wide account ids, alarm address, and GitHub repository. Selected with the CDK context value
    /// deployment. Without it, the app deploys the single property selected by propertyDir in the per-property topology.
    /// Thread safety: immutable after loading.
    /// </summary>
    public class DeploymentDefinition
    {
        /// <summary>
        /// Most properties one shared deployment serves. An Application Load Balancer accepts 25 certificates per
        /// listener by default and the shared API needs one per property API host.
        /// </summary>
        public static readonly int MaxSharedProperties = 25;

        /// <summary>
        /// Most certificates the shared API listener carries: the default certificate plus the 25 an Application Load
        /// Balancer accepts by default. Every property API host and every platform host needs one.
        /// </summary>
        public static readonly int MaxListenerCertificates = 26;

        private static readonly Regex _HostPattern = new Regex("^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$");

        private static readonly Regex _IdPattern = new Regex("^[a-z0-9][a-z0-9-]{0,19}$");
        private static readonly Regex _AccountPattern = new Regex("^[0-9]{12}$");

        /// <summary>
        /// Absolute path of the deployment file.
        /// </summary>
        public string File { get; private set; } = String.Empty;

        /// <summary>
        /// Deployment id (same format as a property slug, at most 20 characters). Prefixes the shared stacks and their resources in the shared
        /// topology; unused in the per-property topology.
        /// </summary>
        public string Id { get; private set; } = String.Empty;

        /// <summary>
        /// Display name of the deployment, used in shared stack descriptions. Defaults to the id.
        /// </summary>
        public string Name { get; private set; } = String.Empty;

        /// <summary>
        /// Topology. Default <see cref="DeploymentTopologyEnum.PerProperty"/>.
        /// </summary>
        public DeploymentTopologyEnum Topology { get; private set; } = DeploymentTopologyEnum.PerProperty;

        /// <summary>
        /// Absolute property folders, in file order. The first property is the primary one: its API host receives the
        /// MediaConvert webhook in the shared topology.
        /// </summary>
        public List<string> PropertyDirectories { get; private set; } = new List<string>();

        /// <summary>
        /// Account ids by environment name. Empty when not set; each property's deploy/infra.json is used then
        /// (per-property topology) or the first property's (shared topology).
        /// </summary>
        public Dictionary<string, string> Accounts { get; private set; } = new Dictionary<string, string>();

        /// <summary>
        /// Alarm and budget address of the shared stacks, or empty.
        /// </summary>
        public string AlarmEmail { get; private set; } = String.Empty;

        /// <summary>
        /// GitHub owner of the repository that deploys the shared stacks, or empty.
        /// </summary>
        public string GitHubOwner { get; private set; } = String.Empty;

        /// <summary>
        /// GitHub repository that deploys the shared stacks, or empty.
        /// </summary>
        public string GitHubRepository { get; private set; } = String.Empty;

        /// <summary>
        /// Production platform host names from platform.hosts (shared topology; at least one required there). Each must
        /// be a subdomain of one listed property's web domain; development prefixes that domain with dev. exactly as
        /// it does for the API and media hosts.
        /// </summary>
        public List<string> PlatformHosts { get; private set; } = new List<string>();

        /// <summary>
        /// Email of the first platform administrator from platform.bootstrapAdminEmail, or empty.
        /// </summary>
        public string PlatformBootstrapAdminEmail { get; private set; } = String.Empty;

        /// <summary>
        /// Deployment mail settings of the shared API from the mail section, empty when absent. The per-property
        /// topology ignores it and uses each property's deploy/infra.json.
        /// </summary>
        public MailSettings Mail { get; private set; } = new MailSettings();

        /// <summary>
        /// DNS management from the dns section, or null when it sets no dns.mode. When set it applies to every listed
        /// property and wins over their own deploy/infra.json.
        /// </summary>
        public DnsSettings? Dns { get; private set; } = null;

        /// <summary>
        /// Load and validate a deployment file. Relative property paths resolve against the folder that contains the file.
        /// </summary>
        /// <param name="file">Absolute path of the deployment file.</param>
        /// <returns>Deployment.</returns>
        /// <exception cref="ArgumentNullException">The path is empty.</exception>
        /// <exception cref="ArgumentException">The file is missing or invalid.</exception>
        public static DeploymentDefinition Load(string file)
        {
            if (String.IsNullOrWhiteSpace(file)) throw new ArgumentNullException(nameof(file));
            string full = Path.GetFullPath(file);
            if (!System.IO.File.Exists(full))
            {
                throw new ArgumentException("Deployment file not found: " + full + ". Pass -c deployment=<path to a deployment json file>.", nameof(file));
            }

            DeploymentDefinition deployment = new DeploymentDefinition();
            deployment.File = full;
            string folder = Path.GetDirectoryName(full) ?? System.IO.Directory.GetCurrentDirectory();

            using (JsonDocument document = JsonDocument.Parse(System.IO.File.ReadAllText(full)))
            {
                JsonElement root = document.RootElement;
                deployment.Id = Text(root, "id");
                deployment.Name = Text(root, "name");
                if (String.IsNullOrWhiteSpace(deployment.Name)) deployment.Name = deployment.Id;

                string topology = Text(root, "topology");
                if (!String.IsNullOrWhiteSpace(topology))
                {
                    if (!Enum.TryParse(topology, false, out DeploymentTopologyEnum parsed) || !Enum.IsDefined(parsed))
                    {
                        throw new ArgumentException("topology in " + full + " must be PerProperty or Shared, got '" + topology + "'.");
                    }

                    deployment.Topology = parsed;
                }

                if (root.TryGetProperty("properties", out JsonElement properties) && properties.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement item in properties.EnumerateArray())
                    {
                        string? value = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                        if (String.IsNullOrWhiteSpace(value)) throw new ArgumentException("Every entry of properties in " + full + " must be a property folder path.");
                        string trimmed = value.Trim();
                        deployment.PropertyDirectories.Add(Path.GetFullPath(Path.IsPathRooted(trimmed) ? trimmed : Path.Combine(folder, trimmed)));
                    }
                }

                if (root.TryGetProperty("accounts", out JsonElement accounts) && accounts.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty account in accounts.EnumerateObject())
                    {
                        string? value = account.Value.ValueKind == JsonValueKind.String ? account.Value.GetString() : account.Value.ToString();
                        if (String.IsNullOrWhiteSpace(value)) continue;
                        if (!_AccountPattern.IsMatch(value))
                        {
                            throw new ArgumentException("accounts." + account.Name + " in " + full + " must be a twelve-digit AWS account id.");
                        }

                        deployment.Accounts[account.Name] = value;
                    }
                }

                if (root.TryGetProperty("platform", out JsonElement platform) && platform.ValueKind == JsonValueKind.Object)
                {
                    if (platform.TryGetProperty("hosts", out JsonElement hosts) && hosts.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement item in hosts.EnumerateArray())
                        {
                            string? value = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                            if (String.IsNullOrWhiteSpace(value)) throw new ArgumentException("Every entry of platform.hosts in " + full + " must be a host name.");
                            deployment.PlatformHosts.Add(value.Trim().ToLowerInvariant());
                        }
                    }

                    deployment.PlatformBootstrapAdminEmail = Text(platform, "bootstrapAdminEmail");
                }

                deployment.AlarmEmail = Text(root, "alarmEmail");
                if (root.TryGetProperty("github", out JsonElement github) && github.ValueKind == JsonValueKind.Object)
                {
                    deployment.GitHubOwner = Text(github, "owner");
                    deployment.GitHubRepository = Text(github, "repository");
                }

                deployment.Mail = MailSettings.FromJson(root, full);
                deployment.Dns = DnsSettings.FromJson(root, full);
            }

            deployment.Validate();
            return deployment;
        }

        private void Validate()
        {
            if (PropertyDirectories.Count == 0)
            {
                throw new ArgumentException("properties in " + File + " must list at least one property folder.");
            }

            HashSet<string> unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string directory in PropertyDirectories)
            {
                if (!unique.Add(directory)) throw new ArgumentException("Property folder " + directory + " is listed twice in " + File + ".");
            }

            if (Topology == DeploymentTopologyEnum.Shared)
            {
                if (!_IdPattern.IsMatch(Id))
                {
                    throw new ArgumentException("id in " + File + " must be 1 to 20 lowercase letters, digits, or hyphens for the shared topology, got '" + Id + "'.");
                }

                if (PropertyDirectories.Count > MaxSharedProperties)
                {
                    throw new ArgumentException("A shared deployment serves at most " + MaxSharedProperties + " properties (one API certificate per property on the load balancer listener); " + File + " lists " + PropertyDirectories.Count + ".");
                }

                if (PlatformHosts.Count == 0)
                {
                    throw new ArgumentException("platform.hosts in " + File + " must name at least one platform host for the shared topology (the backend runs MultiProperty and refuses to start without one).");
                }

                HashSet<string> hosts = new HashSet<string>(StringComparer.Ordinal);
                foreach (string host in PlatformHosts)
                {
                    if (!_HostPattern.IsMatch(host)) throw new ArgumentException("platform.hosts in " + File + " contains an invalid host name '" + host + "'.");
                    if (!hosts.Add(host)) throw new ArgumentException("Platform host '" + host + "' is listed twice in " + File + ".");
                }

                if (PropertyDirectories.Count + PlatformHosts.Count > MaxListenerCertificates)
                {
                    throw new ArgumentException("The shared API listener carries at most " + MaxListenerCertificates + " certificates (one per property API host and per platform host); " + File + " needs " + (PropertyDirectories.Count + PlatformHosts.Count) + ".");
                }
            }
        }

        private static string Text(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String) return String.Empty;
            return (value.GetString() ?? String.Empty).Trim();
        }
    }
}
