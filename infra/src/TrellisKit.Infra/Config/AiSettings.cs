namespace TrellisKit.Infra.Config
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Text.RegularExpressions;

    /// <summary>
    /// AI infrastructure (docs/AI_CONTRACT.md): the optional RecallDB vector store for assistant knowledge, and the
    /// frame-ancestors sources of the assistant embed page (section 13.3). Read from the ai section of a property's
    /// deploy/infra.json and the recallDb and embedFrameAncestors context values. RecallDB is off by default, so a
    /// deployment without it creates no RecallDB task, secrets, service discovery namespace, or security group and
    /// costs nothing extra; model endpoints themselves are entered in the admin console, never here.
    /// Thread safety: not thread safe; configure before building stacks.
    /// </summary>
    public class AiSettings
    {
        private static readonly Regex _OriginPattern = new Regex("^https://[a-z0-9]([a-z0-9-]*[a-z0-9])?(\\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)*(:[0-9]{1,5})?$");
        private static readonly Regex _IdentifierPattern = new Regex("^[a-z_][a-z0-9_]{0,62}$");

        private string _RecallDbImage = "jchristn77/recalldb-server:v0.2.1";
        private int _RecallDbCpu = 256;
        private int _RecallDbMemoryMiB = 1024;
        private int _RecallDbPort = 8600;
        private int _RecallDbDesiredCount = 1;
        private string _RecallDbDatabaseName = "recalldb";
        private string _RecallDbUsername = "recalldb";
        private string _RecallDbTenant = "default";
        private string _RecallDbServiceName = "recalldb";
        private List<string> _EmbedFrameAncestors = new List<string>();

        /// <summary>
        /// Run RecallDB as an ECS service in the private subnets and point the server at it (TK_RECALLDB_ENDPOINT,
        /// TK_RECALLDB_TENANT, TK_RECALLDB_TOKEN). Default false.
        /// </summary>
        public bool RecallDbEnabled { get; set; } = false;

        /// <summary>
        /// RecallDB image, pinned to a version (the image is linux/amd64 only, so the task runs on X86_64). Default
        /// jchristn77/recalldb-server:v0.2.1, the version the Docker stack and docker/recalldb/start.sh are written for.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        /// <exception cref="ArgumentException">The value has no tag or uses latest.</exception>
        public string RecallDbImage
        {
            get
            {
                return _RecallDbImage;
            }
            set
            {
                string text = Guard.NotEmpty(value, nameof(RecallDbImage)).Trim();
                int colon = text.LastIndexOf(':');
                if (colon < 0 || colon < text.LastIndexOf('/') || text.EndsWith(":latest", StringComparison.Ordinal))
                {
                    throw new ArgumentException("ai.recallDbImage must name a pinned tag, got '" + text + "'.", nameof(RecallDbImage));
                }

                _RecallDbImage = text;
            }
        }

        /// <summary>
        /// Fargate CPU units of the RecallDB task. Default 256. Minimum 256, maximum 4096.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int RecallDbCpu
        {
            get { return _RecallDbCpu; }
            set { _RecallDbCpu = Guard.Range(value, 256, 4096, nameof(RecallDbCpu)); }
        }

        /// <summary>
        /// Memory of the RecallDB task in MiB. Default 1024. Minimum 512, maximum 30720.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int RecallDbMemoryMiB
        {
            get { return _RecallDbMemoryMiB; }
            set { _RecallDbMemoryMiB = Guard.Range(value, 512, 30720, nameof(RecallDbMemoryMiB)); }
        }

        /// <summary>
        /// RecallDB HTTP port (RECALLDB_PORT of docker/recalldb/start.sh). Default 8600. Minimum 1024, maximum 65535.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int RecallDbPort
        {
            get { return _RecallDbPort; }
            set { _RecallDbPort = Guard.Range(value, 1024, 65535, nameof(RecallDbPort)); }
        }

        /// <summary>
        /// RecallDB tasks. Default 1. Minimum 1, maximum 4.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
        public int RecallDbDesiredCount
        {
            get { return _RecallDbDesiredCount; }
            set { _RecallDbDesiredCount = Guard.Range(value, 1, 4, nameof(RecallDbDesiredCount)); }
        }

        /// <summary>
        /// RecallDB database on the deployment's RDS instance, created by the db-bootstrap container. Default recalldb.
        /// </summary>
        /// <exception cref="ArgumentException">The value is not a simple lowercase identifier.</exception>
        public string RecallDbDatabaseName
        {
            get { return _RecallDbDatabaseName; }
            set { _RecallDbDatabaseName = Identifier(value, nameof(RecallDbDatabaseName)); }
        }

        /// <summary>
        /// RecallDB database role, created by the db-bootstrap container. Default recalldb.
        /// </summary>
        /// <exception cref="ArgumentException">The value is not a simple lowercase identifier.</exception>
        public string RecallDbUsername
        {
            get { return _RecallDbUsername; }
            set { _RecallDbUsername = Identifier(value, nameof(RecallDbUsername)); }
        }

        /// <summary>
        /// RecallDB tenant that holds the knowledge collections (TK_RECALLDB_TENANT). Default default, the tenant
        /// RecallDB seeds on first start.
        /// </summary>
        /// <exception cref="ArgumentNullException">The value is empty.</exception>
        public string RecallDbTenant
        {
            get { return _RecallDbTenant; }
            set { _RecallDbTenant = Guard.NotEmpty(value, nameof(RecallDbTenant)).Trim(); }
        }

        /// <summary>
        /// Service discovery name of the RecallDB service in the cluster's private DNS namespace. Default recalldb.
        /// </summary>
        /// <exception cref="ArgumentException">The value is not a DNS label.</exception>
        public string RecallDbServiceName
        {
            get
            {
                return _RecallDbServiceName;
            }
            set
            {
                string text = Guard.NotEmpty(value, nameof(RecallDbServiceName)).Trim();
                if (!Regex.IsMatch(text, "^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$"))
                {
                    throw new ArgumentException("ai.recallDbServiceName must be a DNS label, got '" + text + "'.", nameof(RecallDbServiceName));
                }

                _RecallDbServiceName = text;
            }
        }

        /// <summary>
        /// Origins (https://host[:port]) that may frame /embed/assistants/* in addition to the website itself
        /// ('self'). The website serves the embed page with Content-Security-Policy frame-ancestors 'self' plus these
        /// origins; list every origin any assistant's embed.allowedOrigins uses (the embed page still checks the
        /// embedding origin against the assistant's own list). Default empty (same-origin framing only). Up to 20.
        /// </summary>
        /// <exception cref="ArgumentException">An origin is not https://host[:port], or there are more than 20.</exception>
        public List<string> EmbedFrameAncestors
        {
            get
            {
                return _EmbedFrameAncestors;
            }
            set
            {
                List<string> origins = Guard.NotNull(value, nameof(EmbedFrameAncestors))
                    .Select(o => (o ?? String.Empty).Trim().TrimEnd('/').ToLowerInvariant())
                    .Where(o => o.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (origins.Count > 20) throw new ArgumentException("ai.embedFrameAncestors allows at most 20 origins.", nameof(EmbedFrameAncestors));
                foreach (string origin in origins)
                {
                    if (!_OriginPattern.IsMatch(origin))
                    {
                        throw new ArgumentException("ai.embedFrameAncestors entries must be https://host[:port], got '" + origin + "'.", nameof(EmbedFrameAncestors));
                    }
                }

                _EmbedFrameAncestors = origins;
            }
        }

        /// <summary>
        /// The frame-ancestors source list of the embed page: 'self' followed by <see cref="EmbedFrameAncestors"/>.
        /// </summary>
        public string FrameAncestorsSources
        {
            get
            {
                List<string> sources = new List<string> { "'self'" };
                sources.AddRange(_EmbedFrameAncestors);
                return String.Join(" ", sources);
            }
        }

        /// <summary>
        /// Read the ai section of a deploy/infra.json file: recallDb (boolean), recallDbImage, recallDbCpu,
        /// recallDbMemoryMiB, recallDbDesiredCount, recallDbTenant, and embedFrameAncestors (array of origins). A
        /// missing or non-object section yields the defaults.
        /// </summary>
        /// <param name="root">Root element of the file.</param>
        /// <param name="file">File path, used in error messages.</param>
        /// <returns>Settings.</returns>
        /// <exception cref="ArgumentException">A value is invalid.</exception>
        public static AiSettings FromJson(JsonElement root, string file)
        {
            AiSettings settings = new AiSettings();
            if (root.ValueKind != JsonValueKind.Object) return settings;
            if (!root.TryGetProperty("ai", out JsonElement ai) || ai.ValueKind != JsonValueKind.Object) return settings;

            try
            {
                if (ai.TryGetProperty("recallDb", out JsonElement enabled))
                {
                    if (enabled.ValueKind != JsonValueKind.True && enabled.ValueKind != JsonValueKind.False)
                    {
                        throw new ArgumentException("ai.recallDb must be true or false.");
                    }

                    settings.RecallDbEnabled = enabled.GetBoolean();
                }

                if (ai.TryGetProperty("recallDbImage", out JsonElement image) && image.ValueKind == JsonValueKind.String) settings.RecallDbImage = image.GetString() ?? String.Empty;
                if (ai.TryGetProperty("recallDbTenant", out JsonElement tenant) && tenant.ValueKind == JsonValueKind.String) settings.RecallDbTenant = tenant.GetString() ?? String.Empty;
                if (ai.TryGetProperty("recallDbCpu", out JsonElement cpu) && cpu.ValueKind == JsonValueKind.Number) settings.RecallDbCpu = cpu.GetInt32();
                if (ai.TryGetProperty("recallDbMemoryMiB", out JsonElement memory) && memory.ValueKind == JsonValueKind.Number) settings.RecallDbMemoryMiB = memory.GetInt32();
                if (ai.TryGetProperty("recallDbDesiredCount", out JsonElement count) && count.ValueKind == JsonValueKind.Number) settings.RecallDbDesiredCount = count.GetInt32();
                if (ai.TryGetProperty("embedFrameAncestors", out JsonElement origins) && origins.ValueKind == JsonValueKind.Array)
                {
                    settings.EmbedFrameAncestors = origins.EnumerateArray().Where(o => o.ValueKind == JsonValueKind.String).Select(o => o.GetString() ?? String.Empty).ToList();
                }
            }
            catch (ArgumentException e)
            {
                throw new ArgumentException("Invalid ai section in " + file + ": " + e.Message, e);
            }

            return settings;
        }

        private static string Identifier(string value, string name)
        {
            string text = Guard.NotEmpty(value, name).Trim();
            if (!_IdentifierPattern.IsMatch(text)) throw new ArgumentException(name + " must be a lowercase identifier, got '" + text + "'.", name);
            return text;
        }
    }
}
