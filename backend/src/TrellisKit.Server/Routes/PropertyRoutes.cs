namespace TrellisKit.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Security.Cryptography;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Responses;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using static TrellisKit.Server.Routes.OpenApiExtensions;

    /// <summary>
    /// Anonymous property routes: the public property configuration and its brand images.
    /// </summary>
    public class PropertyRoutes
    {
        #region Public-Members

        /// <summary>
        /// Path prefix of served brand assets.
        /// </summary>
        public const string AssetPathPrefix = Constants.ApiPrefix + "/property/assets/";

        /// <summary>
        /// Cache lifetime of brand assets and the property document, in seconds. Default 3600. Minimum 0, maximum 31536000.
        /// </summary>
        public int CacheSeconds
        {
            get { return _CacheSeconds; }
            set { _CacheSeconds = Math.Clamp(value, 0, 31536000); }
        }

        #endregion

        #region Private-Members

        private const string _Tag = "Property";
        private static readonly Dictionary<string, string> _ContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".webp"] = "image/webp",
            [".gif"] = "image/gif",
            [".ico"] = "image/x-icon",
            [".svg"] = "image/svg+xml"
        };

        private readonly PropertyDirectory _Directory;
        private int _CacheSeconds = 3600;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the routes.
        /// </summary>
        /// <param name="directory">Property directory (resolves each request's property services).</param>
        /// <exception cref="ArgumentNullException">The directory is null.</exception>
        public PropertyRoutes(PropertyDirectory directory)
        {
            _Directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="server">Watson server.</param>
        /// <exception cref="ArgumentNullException">The server is null.</exception>
        public void Register(Webserver server)
        {
            ArgumentNullException.ThrowIfNull(server);
            server.Routes.PreAuthentication.Static.Add(HttpMethod.GET, Constants.ApiPrefix + "/property", GetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Public property configuration", _Tag, "Everything in property.json except web; carries { name, brand { colors (the effective palette: light and dark, every key including onAccentSoft), paletteVersion, logoUrl, iconUrl }, domains, membership (null in the Open access model), minimumAge, access { model, anonymousBrowsing, gatedFeatures, defaultAudience, purchasesEnabled, signupCodesEnabled, revision }, profile, profileFields, reactions, features { signupCodes, directory, events, library, messaging, publicSamples, forums, search, ai, aiPlacement }, terminology, updates }. features.search and features.ai are effective (the property switch, which administrators change in the admin portal for AI, and the deployment setting). The ETag changes with the palette version and the AI switch. Anonymous.")
                    .Bounded("PublicProperty", "configuration lists from property.json and the profile field definitions (at most 104 fields; options, reactions, redirects; documents at most 30)"));
            server.Routes.PreAuthentication.Parameter.Add(HttpMethod.GET, AssetPathPrefix + "{file}", GetAssetAsync, RouteHelper.HandleExceptionAsync,
                openApiMetadata: Doc("Read a brand image", _Tag, "A file directly under the property brand/ folder, for example logo.png. Cacheable. Anonymous.")
                    .PathParam("file", "File name, for example logo.png").File("image/*", "Brand image").Resp(304, "Not modified").Resp(404, "NotFound"));
        }

        /// <summary>
        /// Build the public property document.
        /// </summary>
        /// <param name="property">Property.</param>
        /// <param name="baseUrl">Absolute API origin used for asset URLs, for example https://api.example.test.</param>
        /// <returns>Response.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static PublicPropertyResponse Build(PropertyConfiguration property, string baseUrl)
        {
            return Build(property, baseUrl, null);
        }

        /// <summary>
        /// Build the public property document with the server's live update check interval as the default.
        /// </summary>
        /// <param name="property">Property.</param>
        /// <param name="baseUrl">Absolute API origin used for asset URLs.</param>
        /// <param name="defaultCheckIntervalMinutes">Setting updates.checkIntervalMinutes, or null for 15.</param>
        /// <returns>Response.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static PublicPropertyResponse Build(PropertyConfiguration property, string baseUrl, int? defaultCheckIntervalMinutes)
        {
            ArgumentNullException.ThrowIfNull(property);
            ArgumentNullException.ThrowIfNull(baseUrl);
            string origin = baseUrl.TrimEnd('/');
            return new PublicPropertyResponse
            {
                SchemaVersion = property.SchemaVersion,
                Id = property.Id,
                Slug = property.EffectiveSlug,
                Name = property.Name,
                ShortName = property.ShortName,
                Tagline = property.Tagline,
                Description = property.Description,
                Owner = property.Owner,
                Locale = property.Locale,
                Domains = property.Domains,
                Contact = property.Contact,
                Brand = new PropertyBrandView
                {
                    Logo = property.Brand.Logo,
                    Icon = property.Brand.Icon,
                    IconIco = property.Brand.IconIco,
                    LogoUrl = AssetUrl(origin, property.Brand.Logo) ?? String.Empty,
                    IconUrl = AssetUrl(origin, property.Brand.Icon) ?? String.Empty,
                    IconIcoUrl = AssetUrl(origin, property.Brand.IconIco),
                    Colors = BrandPalette.FromDocument(property.Brand.Colors).ToDocument()
                },
                Mobile = property.Mobile,
                Membership = property.Membership,
                MinimumAge = property.Membership?.MinimumAge ?? 18,
                Access = AccessPolicyView.Public(AccessPolicy.FromDocument(property.Access), property.Features.SignupCodes),
                Profile = property.Profile,
                Reactions = property.Reactions,
                Features = PropertyFeaturesView.From(property.Features),
                Terminology = property.Terminology,
                Updates = new PropertyUpdates { CheckIntervalMinutes = property.Updates?.CheckIntervalMinutes ?? defaultCheckIntervalMinutes ?? 15 },
                Directory = DirectoryDefaults(property.DirectorySection),
                Events = EventsDefaults(property.EventsSection),
                Library = LibraryDefaults(property.LibrarySection),
                PublicPages = property.EffectivePublicPages
            };
        }

        /// <summary>
        /// The directory configuration from property.json alone (the database copy, when the directory module runs,
        /// replaces it through <see cref="ApplicationServices.PropertyDecorators"/>).
        /// </summary>
        /// <param name="section">Section, or null for the engine defaults.</param>
        /// <returns>View.</returns>
        public static PublicDirectoryConfig DirectoryDefaults(PropertyDirectorySection? section)
        {
            PropertyDirectorySection s = section ?? new PropertyDirectorySection();
            return new PublicDirectoryConfig
            {
                VenueLabel = s.VenueLabel,
                VenueLabelPlural = s.VenueLabelPlural,
                InstructorLabel = s.InstructorLabel,
                InstructorLabelPlural = s.InstructorLabelPlural,
                SessionLabel = s.SessionLabel,
                SessionLabelPlural = s.SessionLabelPlural,
                Programs = (s.Programs ?? new List<PropertyOptionSeed>()).Where(o => o != null).Select(o => new PublicOption { Key = o.Key, Label = o.Label }).ToList(),
                InstructorRankFieldKey = s.InstructorRankFieldKey,
                Submissions = s.Submissions,
                Claims = s.Claims
            };
        }

        /// <summary>
        /// The events configuration from property.json alone.
        /// </summary>
        /// <param name="section">Section, or null for the engine defaults.</param>
        /// <returns>View.</returns>
        public static PublicEventsConfig EventsDefaults(PropertyEventsSection? section)
        {
            PropertyEventsSection s = section ?? new PropertyEventsSection();
            return new PublicEventsConfig
            {
                Categories = (s.Categories ?? new List<PropertyOptionSeed>()).Where(o => o != null).Select(o => new PublicOption { Key = o.Key, Label = o.Label, Color = o.Color }).ToList(),
                Proposals = s.Proposals
            };
        }

        /// <summary>
        /// The library configuration from property.json alone.
        /// </summary>
        /// <param name="section">Section, or null for the engine defaults.</param>
        /// <returns>View.</returns>
        public static PublicLibraryConfig LibraryDefaults(PropertyLibrarySection? section)
        {
            PropertyLibrarySection s = section ?? new PropertyLibrarySection();
            return new PublicLibraryConfig
            {
                LevelSource = s.LevelSource,
                LevelFieldKey = s.LevelFieldKey,
                Levels = s.LevelSource == "Custom" ? (s.Levels ?? new List<PropertyOptionSeed>()).Where(o => o != null).Select(o => new PublicOption { Key = o.Key, Label = o.Label, Color = o.Color }).ToList() : new List<PublicOption>()
            };
        }

        #endregion

        #region Private-Methods

        private async Task GetAsync(HttpContextBase ctx)
        {
            ApplicationServices services = _Directory.For(ctx);
            PublicPropertyResponse response = Build(services.Property.Property, RequestOrigin(ctx), services.Settings.Updates.CheckIntervalMinutes);
            // Brand colors are the property's effective palette (seeded from property.json, edited in the admin portal).
            BrandPalette palette = services.Branding.Current;
            response.Brand.Colors = palette.ToDocument();
            response.Brand.PaletteVersion = palette.Version;
            // The engine palette presets members may choose (docs/TRELLISKIT.md section 2.1); the brand palette stays the default.
            MemberPaletteChoices memberChoices = services.Branding.MemberChoices;
            response.Brand.Presets = memberChoices.Allowed();
            // features.ai is the capability clients gate on: the property's AI switch (seeded from property.json, switched
            // in the admin portal) and the deployment switch ai.enabled.
            response.ProductEventsEnabled = services.Metrics.Configuration.Enabled && services.Metrics.Configuration.ProductEventsEnabled;
            response.Features.Ai = services.AiSwitch.Enabled && services.Settings.Ai.Enabled;
            response.Features.AiPlacement = services.Settings.Ai.MobilePlacement.ToString();
            // features.search is the capability clients gate on: property.json switch and the deployment switch search.enabled.
            response.Features.Search = response.Features.Search && services.Settings.Search.Enabled;
            response.Ideas = new PublicIdeasView
            {
                MemberSubmissions = services.Settings.Ideas.MemberSubmissions,
                PublicBoardEnabled = services.Settings.Ideas.PublicBoardEnabled,
                VoteOnClosed = services.Settings.Ideas.VoteOnClosed
            };
            AccessPolicy access = services.Access.Current;
            response.Access = AccessPolicyView.Public(access, services.Property.Property.Features.SignupCodes);
            if (!access.PurchasesEnabled) response.Membership = null;
            IReadOnlyList<ProfileField> fields = await services.ProfileFields.AllAsync(ctx.Token).ConfigureAwait(false);
            response.ProfileFields = PublicFields(fields);
            response.Profile = services.Settings.ProfileFields.LegacyFieldsMode == LegacyFieldsModeEnum.Off ? null : LegacyProfile(services.Property.Property.Profile, fields);
            foreach (Func<PublicPropertyResponse, CancellationToken, Task> decorator in services.PropertyDecorators) await decorator(response, ctx.Token).ConfigureAwait(false);
            // Documents (docs/TRELLISKIT.md section 14): the live current versions, so an admin publish shows without a rebuild.
            response.Documents = services.Documents.Summaries();
            string revision = await services.ProfileFields.RevisionAsync(ctx.Token).ConfigureAwait(false);
            string document = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(services.Property.Property.Id + "|" + services.Property.Property.Name + "|" + System.Text.Json.JsonSerializer.Serialize(services.Property.Property)))).Substring(0, 16).ToLowerInvariant();
            string etag = "\"v" + document + "-f" + revision + "-a" + access.Model + access.Revision + "-u" + (response.Updates?.CheckIntervalMinutes ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture) + "-i" + (response.Features.Ai ? response.Features.AiPlacement : "off") + "-p" + Phase4Hash(response) + "-b" + palette.Version.ToString(System.Globalization.CultureInfo.InvariantCulture) + "m" + memberChoices.Mode + memberChoices.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                "-d" + response.Ideas.MemberSubmissions + (response.Ideas.PublicBoardEnabled ? "p" : String.Empty) + (response.Ideas.VoteOnClosed ? "c" : String.Empty) + "-k" + services.Documents.Revision() + "\"";
            ctx.Response.Headers["Cache-Control"] = "public, max-age=" + Math.Min(_CacheSeconds, 300);
            ctx.Response.Headers["ETag"] = etag;
            ctx.Response.Headers["Vary"] = "Host, X-TK-Property";
            if (String.Equals(ctx.Request.RetrieveHeaderValue("If-None-Match"), etag, StringComparison.Ordinal))
            {
                ctx.Response.StatusCode = 304;
                await ctx.Response.Send(ctx.Token).ConfigureAwait(false);
                return;
            }
            await RouteHelper.SendJsonAsync(ctx, 200, response).ConfigureAwait(false);
        }

        /// <summary>
        /// A short hash of the Phase 4 sections (directory, events, library), so edits to programs, categories, levels,
        /// or the map configuration change the property ETag.
        /// </summary>
        /// <param name="response">Decorated response.</param>
        /// <returns>Hex prefix.</returns>
        public static string Phase4Hash(PublicPropertyResponse response)
        {
            ArgumentNullException.ThrowIfNull(response);
            string json = System.Text.Json.JsonSerializer.Serialize(response.Directory) + "|" + System.Text.Json.JsonSerializer.Serialize(response.Events) + "|" + System.Text.Json.JsonSerializer.Serialize(response.Library);
            return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json))).Substring(0, 12).ToLowerInvariant();
        }

        /// <summary>
        /// The client definitions: enabled fields whose visibility is not Staff, built-ins included, in profile order.
        /// </summary>
        /// <param name="fields">Every definition.</param>
        /// <returns>Client definitions.</returns>
        public static List<PublicProfileField> PublicFields(IEnumerable<ProfileField> fields)
        {
            ArgumentNullException.ThrowIfNull(fields);
            return fields.Where(f => f.Enabled && f.Visibility != ProfileVisibilityEnum.Staff).OrderBy(f => f.ProfileOrder).ThenBy(f => f.CreatedUtc).Select(f => new PublicProfileField
            {
                Key = f.Key,
                BuiltIn = f.BuiltIn,
                Label = f.Label,
                HelpText = f.HelpText,
                Placeholder = f.Placeholder,
                Type = f.Type,
                Options = f.Options,
                Rank = f.Rank,
                SubFields = f.SubFields,
                Validation = f.Validation,
                Required = f.Validation.Required,
                CollectAt = f.CollectAt,
                EditableBy = f.EditableBy,
                Visibility = f.Visibility,
                ShowOnProfile = f.ShowOnProfile,
                ProfileOrder = f.ProfileOrder,
                ShowOnCard = f.ShowOnCard,
                Searchable = f.Searchable,
                Filterable = f.Filterable
            }).ToList();
        }

        /// <summary>
        /// The deprecated profile section generated from the seeded fields (docs/MULTIPROPERTY_CONTRACT.md section 9.14):
        /// rank, affiliation, goals, and rankHistory take their labels, options, and enabled state from the fields with
        /// those keys; a section without a field is reported disabled.
        /// </summary>
        /// <param name="document">The document's version 1 section (labels of sections without a field).</param>
        /// <param name="fields">Every definition.</param>
        /// <returns>Section.</returns>
        public static PropertyProfile LegacyProfile(PropertyProfile? document, IEnumerable<ProfileField> fields)
        {
            ArgumentNullException.ThrowIfNull(fields);
            List<ProfileField> list = fields.ToList();
            ProfileField? Find(string key, ProfileFieldTypeEnum type)
            {
                return list.FirstOrDefault(f => !f.BuiltIn && f.Key == key && f.Type == type);
            }

            PropertyProfile source = document ?? new PropertyProfile();
            PropertyProfile profile = new PropertyProfile();
            ProfileField? rank = Find("rank", ProfileFieldTypeEnum.Rank);
            profile.Rank = rank == null ? new PropertyRankField { Label = source.Rank?.Label ?? String.Empty } : new PropertyRankField
            {
                Enabled = rank.Enabled,
                Label = rank.Label,
                LevelLabel = rank.Rank?.LevelLabel ?? String.Empty,
                MaxLevel = rank.Rank?.MaxLevel ?? 0,
                Values = rank.Options.Where(o => !o.Retired).Select(o => new PropertyRankValue { Key = o.Key, Label = o.Label, Color = o.Color ?? String.Empty }).ToList()
            };
            ProfileField? affiliation = Find("affiliation", ProfileFieldTypeEnum.Text);
            profile.Affiliation = new PropertyAffiliationField { Enabled = affiliation?.Enabled ?? false, Label = affiliation?.Label ?? source.Affiliation?.Label ?? String.Empty };
            ProfileField? goals = Find("goals", ProfileFieldTypeEnum.MultiSelect);
            profile.Goals = goals == null ? new PropertyGoalsField { Label = source.Goals?.Label ?? String.Empty } : new PropertyGoalsField
            {
                Enabled = goals.Enabled,
                Label = goals.Label,
                Values = goals.Options.Where(o => !o.Retired).Select(o => new PropertyGoalValue { Key = o.Key, Label = o.Label, FreeText = o.FreeText }).ToList()
            };
            ProfileField? history = Find("rankHistory", ProfileFieldTypeEnum.History);
            profile.RankHistory = history == null ? new PropertyRankHistoryField { Label = source.RankHistory?.Label ?? String.Empty } : new PropertyRankHistoryField
            {
                Enabled = history.Enabled,
                Label = history.Label,
                AwardedByLabel = history.SubFields.FirstOrDefault(s => s.Key == "awardedBy")?.Label ?? String.Empty,
                MaxEntries = history.Validation.MaxEntries ?? 12
            };
            return profile;
        }

        private async Task GetAssetAsync(HttpContextBase ctx)
        {
            string file = RouteHelper.Param(ctx, "file");
            string? path = PropertyLoader.ResolveAsset(_Directory.For(ctx).Property.Property, file);
            string extension = path == null ? String.Empty : Path.GetExtension(path);
            if (path == null || !_ContentTypes.TryGetValue(extension, out string? contentType))
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Brand asset not found.").ConfigureAwait(false);
                return;
            }

            byte[] data = await File.ReadAllBytesAsync(path, ctx.Token).ConfigureAwait(false);
            string etag = "\"" + Convert.ToHexString(SHA256.HashData(data)).Substring(0, 32).ToLowerInvariant() + "\"";
            ctx.Response.Headers["Cache-Control"] = "public, max-age=" + _CacheSeconds;
            ctx.Response.Headers["ETag"] = etag;
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            if (String.Equals(ctx.Request.RetrieveHeaderValue("If-None-Match"), etag, StringComparison.Ordinal))
            {
                ctx.Response.StatusCode = 304;
                await ctx.Response.Send(ctx.Token).ConfigureAwait(false);
                return;
            }
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = contentType;
            await ctx.Response.Send(data, ctx.Token).ConfigureAwait(false);
        }

        private string RequestOrigin(HttpContextBase ctx)
        {
            // The response is cacheable, so request headers must not steer its absolute URLs: X-Forwarded-Host is
            // ignored, and the Host header is used only when it names a configured domain (https) or a loopback host
            // (local development, http unless the server terminates TLS). Anything else gets https://{domains.api}.
            PropertyContext context = _Directory.For(ctx).Property;
            PropertyConfiguration property = context.Property;
            string fallback = "https://" + property.Domains.Api;
            string? host = First(ctx.Request.RetrieveHeaderValue("Host"));
            if (String.IsNullOrWhiteSpace(host) || host.IndexOfAny(new[] { '/', '\\', ' ', '@' }) >= 0) return fallback;
            string name = HostName(host);
            if (IsLoopback(name)) return (_Directory.For(ctx).Settings.Rest.Ssl ? "https" : "http") + "://" + host;
            List<string> configured = new List<string> { property.Domains.Api, property.Domains.Web, property.Domains.Media };
            if (Uri.TryCreate(context.PublicBaseUrl, UriKind.Absolute, out Uri? web)) configured.Add(web.Host);
            if (configured.Any(d => !String.IsNullOrWhiteSpace(d) && String.Equals(d.Trim(), name, StringComparison.OrdinalIgnoreCase))) return "https://" + name;
            return fallback;
        }

        private static string HostName(string host)
        {
            if (host.StartsWith("[", StringComparison.Ordinal))
            {
                int close = host.IndexOf(']');
                return close > 0 ? host.Substring(1, close - 1) : host;
            }
            int colon = host.LastIndexOf(':');
            return colon > 0 ? host.Substring(0, colon) : host;
        }

        private static bool IsLoopback(string name)
        {
            if (String.Equals(name, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
            return IPAddress.TryParse(name, out IPAddress? address) && IPAddress.IsLoopback(address);
        }

        private static string? First(string? header)
        {
            if (String.IsNullOrWhiteSpace(header)) return null;
            string first = header.Split(',')[0].Trim();
            return first.Length == 0 ? null : first.ToLowerInvariant();
        }

        private static string? AssetUrl(string origin, string? brandPath)
        {
            string? file = PropertyLoader.AssetFileName(brandPath);
            return file == null ? null : origin + AssetPathPrefix + Uri.EscapeDataString(file);
        }

        #endregion
    }
}
