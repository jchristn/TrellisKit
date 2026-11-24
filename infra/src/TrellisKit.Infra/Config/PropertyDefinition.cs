namespace TrellisKit.Infra.Config
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;

    /// <summary>
    /// The parts of a property's property.json (and its optional deploy/infra.json) that the infrastructure needs:
    /// identity, domains, contact addresses, and public mobile and store identifiers. Loaded from the directory
    /// selected by the CDK context value propertyDir. The engine itself never names a property.
    /// Thread safety: immutable after loading.
    /// </summary>
    public class PropertyDefinition
    {
        /// <summary>
        /// Property directory used when the propertyDir context value is not set, relative to the engine root.
        /// </summary>
        public static readonly string DefaultDirectory = "samples/example";

        /// <summary>
        /// Name of the property configuration file inside a property directory.
        /// </summary>
        public static readonly string PropertyFileName = "property.json";

        /// <summary>
        /// Optional property-owned deployment settings file, relative to the property directory.
        /// </summary>
        public static readonly string DeployFileName = Path.Combine("deploy", "infra.json");

        /// <summary>
        /// Longest slug the infrastructure accepts. Physical names such as the load balancer (32 characters) append
        /// the environment and a purpose to the slug, so the 63 characters the backend allows do not fit.
        /// </summary>
        public static readonly int MaxSlugLength = 20;

        /// <summary>
        /// Pseudo-ids no property may use as its id or slug (mirrors the backend and shared/src/property.js).
        /// </summary>
        public static readonly IReadOnlyList<string> ReservedIds = new List<string> { "platform", "unresolved", "none", "all", "system" };

        private static readonly Regex _IdPattern = new Regex("^app_[a-z0-9]{6,16}_[A-Za-z0-9]{8,40}$");
        private static readonly Regex _SlugPattern = new Regex("^[a-z0-9][a-z0-9-]{0,62}$");
        private static readonly Regex _AccountPattern = new Regex("^[0-9]{12}$");

        /// <summary>
        /// Absolute path of the property directory.
        /// </summary>
        public string Directory { get; private set; } = String.Empty;

        /// <summary>
        /// Opaque generated property id (app_{k-sortable}_{random}), immutable. Identifies the property to the server,
        /// in data, storage keys, headers, and webhook URLs. It contains underscores and uppercase letters, so it never
        /// names an AWS resource; see <see cref="Slug"/>.
        /// </summary>
        public string Id { get; private set; } = String.Empty;

        /// <summary>
        /// Cosmetic slug (lowercase letters, digits, hyphens; at most <see cref="MaxSlugLength"/> characters): the
        /// property.json slug, or one derived from shortName (else name) exactly as the backend's EffectiveSlug does.
        /// Prefixes every stack and physical resource name. Never identifies the property to the server.
        /// </summary>
        public string Slug { get; private set; } = String.Empty;

        /// <summary>
        /// Display name, used in stack and resource descriptions.
        /// </summary>
        public string Name { get; private set; } = String.Empty;

        /// <summary>
        /// Short display name from property.json, or empty. Source of the derived slug.
        /// </summary>
        public string ShortName { get; private set; } = String.Empty;

        /// <summary>
        /// Production website apex, for example example.test.
        /// </summary>
        public string WebDomain { get; private set; } = String.Empty;

        /// <summary>
        /// Production public API host. May equal <see cref="WebDomain"/> or be its subdomain.
        /// </summary>
        public string ApiDomain { get; private set; } = String.Empty;

        /// <summary>
        /// API load-balancer origin from deploy/infra.json apiOriginDomain. Defaults to api-origin under the web
        /// domain when the public API shares the website, otherwise to the public API domain.
        /// </summary>
        public string ApiOriginDomain { get; private set; } = String.Empty;

        /// <summary>
        /// Production media host. Must be a subdomain of <see cref="WebDomain"/>.
        /// </summary>
        public string MediaDomain { get; private set; } = String.Empty;

        /// <summary>
        /// Vanity apexes that permanently redirect to the website. May be empty.
        /// </summary>
        public List<string> RedirectDomains { get; private set; } = new List<string>();

        /// <summary>
        /// Support address; also the default alarm and budget notification address.
        /// </summary>
        public string SupportEmail { get; private set; } = String.Empty;

        /// <summary>
        /// Sender address of transactional email. Its local part is used on each environment's primary domain.
        /// </summary>
        public string EmailFromAddress { get; private set; } = String.Empty;

        /// <summary>
        /// iOS bundle id (public), or empty.
        /// </summary>
        public string BundleId { get; private set; } = String.Empty;

        /// <summary>
        /// Android package name (public), or empty.
        /// </summary>
        public string AndroidPackage { get; private set; } = String.Empty;

        /// <summary>
        /// App Store and Google Play product id of the membership (public), or empty.
        /// </summary>
        public string StoreProductId { get; private set; } = String.Empty;

        /// <summary>
        /// AWS account ids by environment name, from deploy/infra.json. Empty when the file is absent.
        /// </summary>
        public Dictionary<string, string> Accounts { get; private set; } = new Dictionary<string, string>();

        /// <summary>
        /// Alarm and budget notification address from deploy/infra.json, or empty.
        /// </summary>
        public string AlarmEmail { get; private set; } = String.Empty;

        /// <summary>
        /// GitHub owner of the repository that deploys this property, from deploy/infra.json, or empty.
        /// </summary>
        public string GitHubOwner { get; private set; } = String.Empty;

        /// <summary>
        /// GitHub repository that deploys this property, from deploy/infra.json, or empty.
        /// </summary>
        public string GitHubRepository { get; private set; } = String.Empty;

        /// <summary>
        /// Deployment mail settings from the mail section of deploy/infra.json (per-property topology), empty when absent.
        /// </summary>
        public MailSettings Mail { get; private set; } = new MailSettings();

        /// <summary>
        /// AI infrastructure from the ai section of deploy/infra.json (RecallDB, embed frame ancestors); defaults when absent.
        /// </summary>
        public AiSettings Ai { get; private set; } = new AiSettings();

        /// <summary>
        /// Amazon Location Service resources from the location section of deploy/infra.json; nothing when absent.
        /// </summary>
        public LocationSettings Location { get; private set; } = new LocationSettings();

        /// <summary>
        /// Partio document text service from the partio section of deploy/infra.json; empty when absent.
        /// </summary>
        public PartioSettings Partio { get; private set; } = new PartioSettings();

        /// <summary>
        /// DNS management from the dns section of deploy/infra.json, or null when it sets no dns.mode (Route 53 then,
        /// unless the deployment file or the dnsMode context value says otherwise).
        /// </summary>
        public DnsSettings? Dns { get; private set; } = null;

        /// <summary>
        /// Local part of <see cref="EmailFromAddress"/>, for example no-reply.
        /// </summary>
        public string EmailFromLocalPart
        {
            get
            {
                int at = EmailFromAddress.IndexOf('@');
                return at > 0 ? EmailFromAddress.Substring(0, at) : EmailFromAddress;
            }
        }

        /// <summary>
        /// Load the default property (<see cref="DefaultDirectory"/> under the engine root).
        /// </summary>
        /// <returns>Property.</returns>
        public static PropertyDefinition LoadDefault()
        {
            return Load(ResolveDirectory(null));
        }

        /// <summary>
        /// Resolve a property directory: absolute paths are used as given; relative paths (and the default when the
        /// value is empty) are resolved against the engine root.
        /// </summary>
        /// <param name="value">propertyDir context value, or null.</param>
        /// <returns>Absolute directory.</returns>
        public static string ResolveDirectory(string? value)
        {
            string relative = String.IsNullOrWhiteSpace(value) ? DefaultDirectory : value.Trim();
            if (Path.IsPathRooted(relative)) return Path.GetFullPath(relative);
            return Path.GetFullPath(Path.Combine(EngineRoot(), relative));
        }

        /// <summary>
        /// The engine root: the nearest ancestor of the working directory (or of this assembly) that contains
        /// infra/cdk.json. Falls back to the parent of the working directory.
        /// </summary>
        /// <returns>Absolute directory.</returns>
        public static string EngineRoot()
        {
            foreach (string start in new string[] { System.IO.Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                DirectoryInfo? current = new DirectoryInfo(start);
                while (current != null)
                {
                    if (File.Exists(Path.Combine(current.FullName, "infra", "cdk.json"))) return current.FullName;
                    current = current.Parent;
                }
            }

            return Path.GetFullPath(Path.Combine(System.IO.Directory.GetCurrentDirectory(), ".."));
        }

        /// <summary>
        /// Load and validate a property directory.
        /// </summary>
        /// <param name="directory">Absolute or working-directory-relative path of the property directory.</param>
        /// <returns>Property.</returns>
        /// <exception cref="ArgumentNullException">The directory is empty.</exception>
        /// <exception cref="ArgumentException">The directory or its property.json is missing or invalid.</exception>
        public static PropertyDefinition Load(string directory)
        {
            if (String.IsNullOrWhiteSpace(directory)) throw new ArgumentNullException(nameof(directory));
            string full = Path.GetFullPath(directory);
            string file = Path.Combine(full, PropertyFileName);
            if (!File.Exists(file))
            {
                throw new ArgumentException("Property file not found: " + file + ". Pass -c propertyDir=<path to a property folder>.", nameof(directory));
            }

            PropertyDefinition property = new PropertyDefinition();
            property.Directory = full;

            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(file)))
            {
                JsonElement root = document.RootElement;
                property.Id = Required(root, file, "id");
                property.Name = Required(root, file, "name");
                property.ShortName = Optional(root, "shortName");
                string slug = Optional(root, "slug");
                property.Slug = String.IsNullOrWhiteSpace(slug) ? DeriveSlug(property.ShortName, property.Name) : slug;

                JsonElement domains = Section(root, file, "domains");
                property.WebDomain = Required(domains, file, "domains.web", "web").ToLowerInvariant();
                property.ApiDomain = Required(domains, file, "domains.api", "api").ToLowerInvariant();
                property.MediaDomain = Required(domains, file, "domains.media", "media").ToLowerInvariant();
                if (domains.TryGetProperty("redirects", out JsonElement redirects) && redirects.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement redirect in redirects.EnumerateArray())
                    {
                        string? value = redirect.GetString();
                        if (!String.IsNullOrWhiteSpace(value)) property.RedirectDomains.Add(value.Trim().ToLowerInvariant());
                    }
                }

                JsonElement contact = Section(root, file, "contact");
                property.SupportEmail = Required(contact, file, "contact.supportEmail", "supportEmail");
                property.EmailFromAddress = Required(contact, file, "contact.emailFromAddress", "emailFromAddress");

                if (root.TryGetProperty("mobile", out JsonElement mobile) && mobile.ValueKind == JsonValueKind.Object)
                {
                    property.BundleId = Optional(mobile, "bundleId");
                    property.AndroidPackage = Optional(mobile, "androidPackage");
                }

                if (root.TryGetProperty("membership", out JsonElement membership) && membership.ValueKind == JsonValueKind.Object)
                {
                    property.StoreProductId = Optional(membership, "storeProductId");
                }
            }

            string deploy = Path.Combine(full, DeployFileName);
            if (File.Exists(deploy)) property.LoadDeploy(deploy);

            if (String.IsNullOrEmpty(property.ApiOriginDomain))
            {
                property.ApiOriginDomain = property.ApiDomain == property.WebDomain ? "api-origin." + property.WebDomain : property.ApiDomain;
            }

            property.Validate(file);
            return property;
        }

        /// <summary>
        /// The host label of a host under the web domain, for example api for api.example.test.
        /// </summary>
        /// <param name="host">Host.</param>
        /// <returns>Label.</returns>
        /// <exception cref="ArgumentException">The host is not a subdomain of the web domain.</exception>
        public string LabelOf(string host)
        {
            string suffix = "." + WebDomain;
            if (String.IsNullOrEmpty(host) || !host.EndsWith(suffix, StringComparison.Ordinal) || host.Length <= suffix.Length)
            {
                throw new ArgumentException("Host '" + host + "' must be a subdomain of the web domain '" + WebDomain + "' in " + PropertyFileName + ".", nameof(host));
            }

            return host.Substring(0, host.Length - suffix.Length);
        }

        /// <summary>
        /// Derive a slug from a display name the way the backend's EffectiveSlug does: lowercase ASCII letters and
        /// digits kept, every other run of characters collapsed to one hyphen, leading and trailing hyphens trimmed, at
        /// most 63 characters; "property" when nothing remains.
        /// </summary>
        /// <param name="shortName">Short name, or empty.</param>
        /// <param name="name">Name, used when the short name is empty.</param>
        /// <returns>Slug.</returns>
        public static string DeriveSlug(string? shortName, string? name)
        {
            string source = (String.IsNullOrWhiteSpace(shortName) ? name : shortName) ?? String.Empty;
            StringBuilder builder = new StringBuilder();
            foreach (char c in source.ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) builder.Append(c);
                else if (builder.Length > 0 && builder[builder.Length - 1] != '-') builder.Append('-');
            }

            string slug = builder.ToString().Trim('-');
            if (slug.Length > 63) slug = slug.Substring(0, 63).Trim('-');
            return slug.Length == 0 ? "property" : slug;
        }

        private void LoadDeploy(string file)
        {
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(file)))
            {
                JsonElement root = document.RootElement;
                if (root.TryGetProperty("accounts", out JsonElement accounts) && accounts.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty account in accounts.EnumerateObject())
                    {
                        string? value = account.Value.ValueKind == JsonValueKind.String ? account.Value.GetString() : account.Value.ToString();
                        if (String.IsNullOrWhiteSpace(value)) continue;
                        if (!_AccountPattern.IsMatch(value))
                        {
                            throw new ArgumentException("accounts." + account.Name + " in " + file + " must be a twelve-digit AWS account id.");
                        }

                        Accounts[account.Name] = value;
                    }
                }

                ApiOriginDomain = Optional(root, "apiOriginDomain").ToLowerInvariant();
                AlarmEmail = Optional(root, "alarmEmail");
                if (root.TryGetProperty("github", out JsonElement github) && github.ValueKind == JsonValueKind.Object)
                {
                    GitHubOwner = Optional(github, "owner");
                    GitHubRepository = Optional(github, "repository");
                }

                Mail = MailSettings.FromJson(root, file);
                Ai = AiSettings.FromJson(root, file);
                Location = LocationSettings.FromJson(root, file);
                Partio = PartioSettings.FromJson(root, file);
                Dns = DnsSettings.FromJson(root, file);
            }
        }

        private void Validate(string file)
        {
            if (!_IdPattern.IsMatch(Id) || ReservedIds.Contains(Id))
            {
                throw new ArgumentException("id in " + file + " must be a generated property id (app_{k-sortable}_{random}), got '" + Id + "'.");
            }

            if (!_SlugPattern.IsMatch(Slug) || ReservedIds.Contains(Slug))
            {
                throw new ArgumentException("slug in " + file + " must be lowercase letters, digits, and hyphens (1 to 63 characters) and not a reserved name, got '" + Slug + "'.");
            }

            if (Slug.Length > MaxSlugLength)
            {
                throw new ArgumentException("The slug '" + Slug + "' of " + file + " is longer than the " + MaxSlugLength
                    + " characters AWS resource names allow; set a shorter slug in property.json.");
            }

            if (ApiDomain != WebDomain) LabelOf(ApiDomain);
            LabelOf(MediaDomain);
            LabelOf(ApiOriginDomain);
            if (ApiOriginDomain == MediaDomain || ApiOriginDomain == "www." + WebDomain || ApiDomain == MediaDomain || ApiDomain == "www." + WebDomain)
            {
                throw new ArgumentException("API and API origin hosts in " + file + " must not share a media or www host.");
            }

            string developmentWeb = "dev." + WebDomain;
            string developmentMedia = LabelOf(MediaDomain) + "." + developmentWeb;
            string developmentOrigin = LabelOf(ApiOriginDomain) + "." + developmentWeb;
            if (ApiOriginDomain == developmentWeb || ApiOriginDomain == "www." + developmentWeb || ApiOriginDomain == developmentMedia || developmentOrigin == MediaDomain || developmentOrigin == "www." + WebDomain || developmentOrigin == WebDomain)
            {
                throw new ArgumentException("API origin in " + file + " must not claim a development website or media host.");
            }

            if (ApiDomain != WebDomain && ApiOriginDomain != ApiDomain)
            {
                throw new ArgumentException("apiOriginDomain may differ from domains.api only when the public API shares domains.web in " + file + ".");
            }
            foreach (string redirect in RedirectDomains)
            {
                if (redirect == WebDomain || redirect.EndsWith("." + WebDomain, StringComparison.Ordinal))
                {
                    throw new ArgumentException("Redirect domain '" + redirect + "' in " + file + " must not be inside the web domain.");
                }
            }
        }

        private static JsonElement Section(JsonElement root, string file, string name)
        {
            if (!root.TryGetProperty(name, out JsonElement section) || section.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("Missing object '" + name + "' in " + file + ".");
            }

            return section;
        }

        private static string Required(JsonElement element, string file, string path, string? name = null)
        {
            string value = Optional(element, name ?? path);
            if (String.IsNullOrWhiteSpace(value)) throw new ArgumentException("Missing value '" + path + "' in " + file + ".");
            return value;
        }

        private static string Optional(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String) return String.Empty;
            return (value.GetString() ?? String.Empty).Trim();
        }
    }
}
