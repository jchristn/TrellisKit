namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Integrations;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Security;
    using TrellisKit.Core.Settings;
    using WatsonWebserver.Core;

    /// <summary>
    /// The registered properties of this deployment and the per-request property resolution
    /// (docs/MULTIPROPERTY_CONTRACT.md sections 1, 2, and 8). Holds one <see cref="PropertyRuntime"/> per registered,
    /// non-deleted property, the host and app maps, and the binding of each request to its resolution. Both deployment
    /// modes run this code: SingleProperty is a deployment with exactly one registered property and resolution that
    /// always picks it.
    /// Thread safety: maps are replaced as whole objects on reload; resolution and lookups are safe to call
    /// concurrently. Reloads are serialized.
    /// </summary>
    public sealed class PropertyDirectory : IAsyncDisposable
    {
        #region Public-Members

        /// <summary>
        /// Header that names the property (apps and websites).
        /// </summary>
        public const string PropertyHeader = "X-TK-Property";

        /// <summary>
        /// Header that names the app (iOS bundle id or Android package).
        /// </summary>
        public const string AppHeader = "X-TK-App";

        /// <summary>
        /// Optional client header (web, mobile, or api) for the content changelog; X-Client is used when absent.
        /// </summary>
        public const string ClientHeader = "X-TK-Client";

        /// <summary>
        /// Deployment mode (fixed at startup).
        /// </summary>
        public DeploymentModeEnum Mode { get; }

        /// <summary>
        /// The property loaded from property.directory (the only property in SingleProperty mode), or null in a
        /// MultiProperty deployment started without a property folder.
        /// </summary>
        public PropertyRuntime? Default
        {
            get { return _DefaultId == null ? null : Find(_DefaultId); }
        }

        /// <summary>
        /// Every runtime (non-deleted registered properties), in id order.
        /// </summary>
        public IReadOnlyList<PropertyRuntime> Runtimes
        {
            get { return _Runtimes.Values.OrderBy(r => r.Id, StringComparer.Ordinal).ToList(); }
        }

        /// <summary>
        /// Whether runtimes start their background workers (the server process sets this; tests run workers by hand).
        /// Default false.
        /// </summary>
        public bool RunWorkers { get; set; } = false;

        /// <summary>
        /// The deployment-level realtime bus assigned to every property's services as it is built, or null to keep each
        /// property's default in-process bus (services built outside a server).
        /// </summary>
        public TrellisKit.Server.Services.Realtime.IRealtimeBus? RealtimeBus { get; set; } = null;

        /// <summary>
        /// This task's realtime gateway (the in-process fast path) assigned to every property's realtime module, or null.
        /// </summary>
        public TrellisKit.Server.Services.Realtime.IRealtimeLocalSink? RealtimeSink { get; set; } = null;

        /// <summary>
        /// Deployment driver.
        /// </summary>
        public DatabaseDriverBase Database
        {
            get { return _Database; }
        }

        #endregion

        #region Private-Members

        private readonly AppSettings _Settings;
        private readonly DatabaseDriverBase _Database;
        private readonly Func<PropertyConfiguration, IntegrationSet> _Integrations;
        private readonly AppLogger _Logger;
        private readonly SemaphoreSlim _ReloadLock = new SemaphoreSlim(1, 1);
        private readonly ConditionalWeakTable<HttpContextBase, PropertyResolution> _Bindings = new ConditionalWeakTable<HttpContextBase, PropertyResolution>();
        private Dictionary<string, PropertyRuntime> _Runtimes = new Dictionary<string, PropertyRuntime>(StringComparer.Ordinal);
        private Dictionary<string, string> _Hosts = new Dictionary<string, string>(StringComparer.Ordinal);
        private Dictionary<string, string> _ClaimedHosts = new Dictionary<string, string>(StringComparer.Ordinal);
        private Dictionary<string, string> _AppsByKey = new Dictionary<string, string>(StringComparer.Ordinal);
        private HashSet<string> _WebOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string? _DefaultId = null;
        private PropertyConfiguration? _Seed = null;
        private DateTime? _LastChange = null;
        private CancellationToken _WorkerToken = CancellationToken.None;
        private CancellationTokenSource? _RefreshSource = null;
        private Task? _RefreshLoop = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the directory. Call <see cref="InitializeAsync"/> before serving requests.
        /// </summary>
        /// <param name="settings">Running settings.</param>
        /// <param name="database">Deployment driver (initialized).</param>
        /// <param name="integrations">Builds the integrations of one property.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public PropertyDirectory(AppSettings settings, DatabaseDriverBase database, Func<PropertyConfiguration, IntegrationSet> integrations, AppLogger logger)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Database = (database ?? throw new ArgumentNullException(nameof(database))).Deployment;
            _Integrations = integrations ?? throw new ArgumentNullException(nameof(integrations));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            Mode = settings.Deployment.Mode;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the runtime of the property loaded from property.directory before the registry is read, so its
        /// services exist immediately (the server builds its OpenAPI document and tests read services before start).
        /// </summary>
        /// <param name="seed">Property loaded from property.directory, or null.</param>
        /// <returns>The seed runtime, or null.</returns>
        public PropertyRuntime? Prepare(PropertyConfiguration? seed)
        {
            _Seed = seed;
            if (seed == null) return null;
            if (_Runtimes.TryGetValue(seed.Id, out PropertyRuntime? existing)) return existing;
            PropertyRecord record = new PropertyRecord { Id = seed.Id, Slug = seed.EffectiveSlug, Name = seed.Name, Status = PropertyStatusEnum.Enabled };
            PropertyRuntime runtime = Build(record, seed);
            runtime.Domains = DomainsOf(seed, DateTime.UtcNow);
            runtime.Apps = AppsOf(seed, DateTime.UtcNow);
            Dictionary<string, PropertyRuntime> runtimes = new Dictionary<string, PropertyRuntime>(_Runtimes, StringComparer.Ordinal) { [seed.Id] = runtime };
            _DefaultId = seed.Id;
            Publish(runtimes);
            return runtime;
        }

        /// <summary>
        /// Seed and check the registry, then load every registered property (docs/MULTIPROPERTY_CONTRACT.md sections 1
        /// and 8): the folder property is registered on first start (and re-synchronized when its property.json
        /// changed), SingleProperty refuses to start with more than one property or a different one, and
        /// MultiProperty requires platform hosts.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="InvalidOperationException">A startup check failed; the message names the problem.</exception>
        public async Task InitializeAsync(CancellationToken token = default)
        {
            if (Mode == DeploymentModeEnum.MultiProperty && _Settings.Platform.Hosts.Count == 0)
                throw new InvalidOperationException("platform.hosts must name at least one host in MultiProperty mode.");

            List<PropertyRecord> live = await _Database.Registry.ReadLiveAsync(token).ConfigureAwait(false);
            if (Mode == DeploymentModeEnum.SingleProperty)
            {
                if (live.Count > 1)
                    throw new InvalidOperationException("Deployment mode is SingleProperty but " + live.Count + " properties are registered; set deployment.mode to MultiProperty.");
                if (_Seed != null && live.Count == 1 && live[0].Id != _Seed.Id)
                    throw new InvalidOperationException("property.directory holds property " + _Seed.Id + " but the database holds property " + live[0].Id + ".");
                if (_Seed == null) throw new InvalidOperationException("SingleProperty mode requires property.directory.");
            }

            if (_Seed != null) await SeedAsync(_Seed, token).ConfigureAwait(false);
            await ReloadAsync(token).ConfigureAwait(false);
            foreach (PropertyRuntime runtime in _Runtimes.Values.Where(r => r.IsEnabled))
                await MetricAnnotations.RecordAsync(runtime.Services, "Deploy", "startup-" + Guid.NewGuid().ToString("N"), "Service started", token).ConfigureAwait(false);
        }

        /// <summary>
        /// Start the registry refresh loop (changes made by another instance arrive within
        /// deployment.propertyCacheSeconds) and, when <see cref="RunWorkers"/> is set, each enabled property's workers.
        /// </summary>
        /// <param name="token">Lifetime token.</param>
        public void Start(CancellationToken token)
        {
            _WorkerToken = token;
            if (RunWorkers)
                foreach (PropertyRuntime runtime in _Runtimes.Values.Where(r => r.IsEnabled)) runtime.StartWorkers(token);
            if (_RefreshLoop != null) return;
            _RefreshSource = CancellationTokenSource.CreateLinkedTokenSource(token);
            CancellationToken loopToken = _RefreshSource.Token;
            _RefreshLoop = Task.Run(() => RefreshLoopAsync(loopToken));
        }

        /// <summary>
        /// Reload the registry now (after a platform change): new properties get runtimes, status changes apply,
        /// deleted properties are dropped.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task ReloadAsync(CancellationToken token = default)
        {
            await _ReloadLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                List<PropertyRecord> live = await _Database.Registry.ReadLiveAsync(token).ConfigureAwait(false);
                Dictionary<string, PropertyRuntime> runtimes = new Dictionary<string, PropertyRuntime>(StringComparer.Ordinal);
                List<PropertyRuntime> dropped = _Runtimes.Values.Where(r => !live.Any(l => l.Id == r.Id)).ToList();
                foreach (PropertyRecord record in live)
                {
                    PropertyRuntime? runtime;
                    if (_Runtimes.TryGetValue(record.Id, out PropertyRuntime? existing))
                    {
                        runtime = existing;
                        runtime.Record = record;
                    }
                    else
                    {
                        PropertyConfiguration? configuration = record.Id == _Seed?.Id ? _Seed : ParseDocument(record);
                        if (configuration == null) continue;
                        runtime = Build(record, configuration);
                    }
                    runtime.Domains = await _Database.Registry.ReadDomainsAsync(record.Id, token).ConfigureAwait(false);
                    runtime.Apps = await _Database.Registry.ReadAppsAsync(record.Id, token).ConfigureAwait(false);
                    runtimes[record.Id] = runtime;
                    // Profile fields: seeded on the first load; in SingleProperty the folder's profile field section is
                    // merged again when it changed on disk (resolved decision 15: the folder wins on change).
                    bool folder = Mode == DeploymentModeEnum.SingleProperty && record.Id == _Seed?.Id;
                    try
                    {
                        await runtime.Services.ProfileFields.EnsureSeededAsync(record, runtime.Configuration, folder, token).ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        _Logger.Error("[PropertyDirectory] [p:" + record.Id + "] profile field seeding failed: " + e.Message);
                    }
                    // Access model: seeded on the first load and re-read on every reload (edits on another instance touch
                    // the registry); in SingleProperty the folder's access section wins when it changed on disk.
                    try
                    {
                        await runtime.Services.Access.EnsureSeededAsync(folder, token).ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        _Logger.Error("[PropertyDirectory] [p:" + record.Id + "] access model seeding failed: " + e.Message);
                    }
                    // Brand palette and AI switch (docs/TRELLISKIT.md sections 2.1 and 9.5): seeded on the first load and
                    // re-read on every reload; in SingleProperty the folder's brand.colors and features.ai win when they
                    // changed on disk.
                    try
                    {
                        await runtime.Services.Branding.EnsureSeededAsync(folder, token).ConfigureAwait(false);
                        await runtime.Services.AiSwitch.EnsureSeededAsync(folder, token).ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        _Logger.Error("[PropertyDirectory] [p:" + record.Id + "] brand palette or AI switch seeding failed: " + e.Message);
                    }
                    // Property documents (docs/TRELLISKIT.md section 14): seeded on the first load and re-read on every
                    // reload; in SingleProperty the folder's documents win when their version, title, or file changed on disk.
                    try
                    {
                        await runtime.Services.Documents.EnsureSeededAsync(folder, token).ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        _Logger.Error("[PropertyDirectory] [p:" + record.Id + "] document seeding failed: " + e.Message);
                    }
                }
                _LastChange = await _Database.Registry.ReadLatestChangeAsync(token).ConfigureAwait(false);
                Publish(runtimes);
                foreach (PropertyRuntime runtime in dropped) await runtime.DisposeAsync().ConfigureAwait(false);
                foreach (PropertyRuntime runtime in runtimes.Values)
                {
                    if (!RunWorkers || _WorkerToken == CancellationToken.None) continue;
                    // Workers run only for enabled properties; a disabled or deleting property's workers stop until it is
                    // enabled again (store webhooks still update billing state for disabled properties).
                    if (runtime.IsEnabled) runtime.StartWorkers(_WorkerToken);
                    else if (runtime.WorkersRunning) await runtime.StopWorkersAsync().ConfigureAwait(false);
                }
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.PlatformActions, "action", "registry.reload");
            }
            finally
            {
                _ReloadLock.Release();
            }
        }

        /// <summary>
        /// Find a runtime by property id (any status that is not Deleted).
        /// </summary>
        /// <param name="propertyId">Property id.</param>
        /// <returns>Runtime, or null.</returns>
        public PropertyRuntime? Find(string? propertyId)
        {
            if (String.IsNullOrEmpty(propertyId)) return null;
            return _Runtimes.TryGetValue(propertyId, out PropertyRuntime? runtime) ? runtime : null;
        }

        /// <summary>
        /// Find the property that registered an app (store webhooks resolve by bundle id or package).
        /// </summary>
        /// <param name="platform">Platform.</param>
        /// <param name="appId">Bundle id or package.</param>
        /// <returns>Runtime, or null.</returns>
        public PropertyRuntime? FindByApp(AppPlatformEnum platform, string? appId)
        {
            if (String.IsNullOrWhiteSpace(appId)) return null;
            return _AppsByKey.TryGetValue(AppKey(platform, appId), out string? id) ? Find(id) : null;
        }

        /// <summary>
        /// Find the property that registered a host, verified or not (conflict checks).
        /// </summary>
        /// <param name="host">Host.</param>
        /// <returns>Property id, or null.</returns>
        public string? FindClaimedHost(string? host)
        {
            string h = NormalizeHost(host);
            return h.Length > 0 && _ClaimedHosts.TryGetValue(h, out string? id) ? id : null;
        }

        /// <summary>
        /// Find the property that registered a host.
        /// </summary>
        /// <param name="host">Host (any case, port allowed).</param>
        /// <returns>Property id, or null.</returns>
        public string? FindHost(string? host)
        {
            string normalized = NormalizeHost(host);
            return normalized.Length > 0 && _Hosts.TryGetValue(normalized, out string? id) ? id : null;
        }

        /// <summary>
        /// Resolve a request (docs/MULTIPROPERTY_CONTRACT.md section 2.2, first match wins) and bind the result to it.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <returns>Resolution (bound to the context).</returns>
        public PropertyResolution Resolve(HttpContextBase ctx)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            PropertyResolution resolution = ResolveCore(ctx);
            _Bindings.AddOrUpdate(ctx, resolution);
            TrellisKitTelemetry.Increment(TrellisKitTelemetry.PropertyResolutions, "source", resolution.Source.ToString().ToLowerInvariant(), "outcome", resolution.Outcome);
            return resolution;
        }

        /// <summary>
        /// The resolution bound to a request, or null.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <returns>Resolution, or null.</returns>
        public PropertyResolution? ResolutionOf(HttpContextBase ctx)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            return _Bindings.TryGetValue(ctx, out PropertyResolution? resolution) ? resolution : null;
        }

        /// <summary>
        /// The service graph of the property a request resolved to.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <returns>Services.</returns>
        /// <exception cref="PropertyScopeException">The request did not resolve to a property (a routing bug).</exception>
        public ApplicationServices For(HttpContextBase ctx)
        {
            PropertyRuntime? runtime = ResolutionOf(ctx)?.Runtime;
            if (runtime == null)
            {
                TrellisKitTelemetry.Increment(TrellisKitTelemetry.IsolationViolations, "kind", "missing_scope");
                throw new PropertyScopeException("missing_scope", "The request " + ctx.Request.Method + " " + ctx.Request.Url.RawWithoutQuery + " is not bound to a property.");
            }
            // Synchronous on purpose: the ambient telemetry property set here flows into the caller's remaining awaits, so
            // every metric the handler records carries trelliskit.property.
            TrellisKitTelemetry.CurrentProperty = runtime.Id;
            Attribute(ctx);
            return runtime.Services;
        }

        /// <summary>
        /// Set the content changelog attribution of a request (docs/INTEGRATION.md content review contract): the signed-in
        /// member and the client (X-TK-Client, else X-Client, else the User-Agent). Synchronous on purpose, like the
        /// telemetry property in <see cref="For"/>, so it flows into the handler's remaining awaits and the database
        /// driver copies it into every writing transaction.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <exception cref="ArgumentNullException">ctx is null.</exception>
        public static void Attribute(HttpContextBase ctx)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            string? actor = (ctx.Metadata as RequestContext)?.UserId;
            ContentChangeClientEnum client = ContentChangeContext.ClientFrom(ctx.Request.RetrieveHeaderValue(ClientHeader), ctx.Request.RetrieveHeaderValue("X-Client"),
                ctx.Request.RetrieveHeaderValue("User-Agent"));
            ContentChangeContext? current = ContentChangeContext.Current;
            if (current != null && current.Matches(actor, client)) return;
            ContentChangeContext.Current = new ContentChangeContext(actor, client);
        }

        /// <summary>
        /// The browser origin to allow for a request, or null: cors.allowedOrigins plus https://host for every Web and
        /// Alias domain of every enabled property.
        /// </summary>
        /// <param name="origin">Origin header.</param>
        /// <returns>Allowed origin, or null.</returns>
        public string? ResolveAllowedOrigin(string? origin)
        {
            string? allowed = _Settings.Cors.ResolveAllowedOrigin(origin);
            if (allowed != null || String.IsNullOrWhiteSpace(origin)) return allowed;
            string normalized = origin.Trim().TrimEnd('/');
            return _WebOrigins.Contains(normalized) ? normalized : null;
        }

        /// <summary>
        /// Normalize a host: lowercase, port removed, trailing dot removed, first value of a comma-separated list.
        /// </summary>
        /// <param name="host">Raw host.</param>
        /// <returns>Normalized host, or empty.</returns>
        public static string NormalizeHost(string? host)
        {
            if (String.IsNullOrWhiteSpace(host)) return String.Empty;
            string value = host.Split(',')[0].Trim().ToLowerInvariant();
            if (value.StartsWith("[", StringComparison.Ordinal))
            {
                int close = value.IndexOf(']');
                return close > 0 ? value.Substring(0, close + 1) : value;
            }
            int colon = value.IndexOf(':');
            if (colon >= 0) value = value.Substring(0, colon);
            return value.TrimEnd('.');
        }

        /// <summary>
        /// The domains a property document declares: web, api, media, and redirects. Seeded and platform-registered
        /// domains are verified on registration.
        /// </summary>
        /// <param name="property">Property.</param>
        /// <param name="nowUtc">Now.</param>
        /// <returns>Domains.</returns>
        public static List<PropertyDomain> DomainsOf(PropertyConfiguration property, DateTime nowUtc)
        {
            ArgumentNullException.ThrowIfNull(property);
            List<PropertyDomain> domains = new List<PropertyDomain>();
            void Add(string? host, DomainKindEnum kind)
            {
                string h = NormalizeHost(host);
                if (h.Length == 0 || domains.Any(d => d.Host == h)) return;
                domains.Add(new PropertyDomain { Id = IdGenerator.Generate(Constants.PropertyDomainIdPrefix), PropertyId = property.Id, Host = h, Kind = kind, VerifiedUtc = nowUtc, CreatedUtc = nowUtc });
            }
            if (property.Domains != null)
            {
                Add(property.Domains.Web, DomainKindEnum.Web);
                Add(property.Domains.Api, DomainKindEnum.Api);
                Add(property.Domains.Media, DomainKindEnum.Media);
                foreach (string redirect in property.Domains.Redirects ?? new List<string>()) Add(redirect, DomainKindEnum.Redirect);
            }
            return domains.Take(20).ToList();
        }

        /// <summary>
        /// The apps a property document declares: mobile.bundleId (iOS) and mobile.androidPackage (Android).
        /// </summary>
        /// <param name="property">Property.</param>
        /// <param name="nowUtc">Now.</param>
        /// <returns>Apps.</returns>
        public static List<PropertyApp> AppsOf(PropertyConfiguration property, DateTime nowUtc)
        {
            ArgumentNullException.ThrowIfNull(property);
            List<PropertyApp> apps = new List<PropertyApp>();
            if (!String.IsNullOrWhiteSpace(property.Mobile?.BundleId))
                apps.Add(new PropertyApp { Id = IdGenerator.Generate(Constants.PropertyAppIdPrefix), PropertyId = property.Id, Platform = AppPlatformEnum.Ios, AppId = property.Mobile.BundleId.Trim(), CreatedUtc = nowUtc, UpdatedUtc = nowUtc });
            if (!String.IsNullOrWhiteSpace(property.Mobile?.AndroidPackage))
                apps.Add(new PropertyApp { Id = IdGenerator.Generate(Constants.PropertyAppIdPrefix), PropertyId = property.Id, Platform = AppPlatformEnum.Android, AppId = property.Mobile.AndroidPackage.Trim(), CreatedUtc = nowUtc, UpdatedUtc = nowUtc });
            return apps;
        }

        /// <summary>
        /// Build a runtime for a property configuration (services bound to its id).
        /// </summary>
        /// <param name="record">Registry record.</param>
        /// <param name="configuration">Configuration.</param>
        /// <returns>Runtime (not registered in the maps).</returns>
        public PropertyRuntime Build(PropertyRecord record, PropertyConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(record);
            ArgumentNullException.ThrowIfNull(configuration);
            ApplicationServices services = new ApplicationServices(_Settings, configuration, _Database.ForProperty(configuration.Id), _Integrations(configuration), _Logger);
            if (RealtimeBus != null) services.RealtimeBus = RealtimeBus;
            services.Realtime.LocalSink = RealtimeSink;
            return new PropertyRuntime(record, configuration, services, _Settings, _Logger);
        }

        /// <summary>
        /// Stop every runtime's workers and the refresh loop.
        /// </summary>
        /// <returns>Task.</returns>
        public async ValueTask DisposeAsync()
        {
            if (_RefreshSource != null)
            {
                _RefreshSource.Cancel();
                if (_RefreshLoop != null)
                {
                    try
                    {
                        await _RefreshLoop.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected on shutdown.
                    }
                }
                _RefreshSource.Dispose();
                _RefreshSource = null;
                _RefreshLoop = null;
            }
            foreach (PropertyRuntime runtime in _Runtimes.Values) await runtime.DisposeAsync().ConfigureAwait(false);
            _ReloadLock.Dispose();
        }

        #endregion

        #region Private-Methods

        private PropertyResolution ResolveCore(HttpContextBase ctx)
        {
            string path = (ctx.Request.Url.RawWithoutQuery ?? String.Empty).TrimEnd('/');
            PropertyRuntime? single = Mode == DeploymentModeEnum.SingleProperty ? Default : null;

            // 1. Special routes resolve on their own (deployment-level, or webhooks that resolve from verified content).
            if (ctx.Request.Method == HttpMethod.OPTIONS || IsSpecial(path))
                return new PropertyResolution { Source = PropertyResolutionSourceEnum.Special, Runtime = single };

            // Platform routes: the platform realm, only on a platform host, only in MultiProperty.
            if (StartsWith(path, Constants.ApiPrefix + "/platform"))
            {
                if (Mode == DeploymentModeEnum.SingleProperty)
                    return PropertyResolution.Fail(PropertyResolutionSourceEnum.Platform, 404, "NotAvailable", "Platform routes exist only in MultiProperty mode.", "not_found");
                if (!_Settings.Platform.IsPlatformHost(RequestHost(ctx))) return PropertyResolution.NotFound(PropertyResolutionSourceEnum.Platform);
                return new PropertyResolution { Source = PropertyResolutionSourceEnum.Platform, IsPlatform = true };
            }

            string host = RequestHost(ctx);
            string? hostProperty = host.Length > 0 && _Hosts.TryGetValue(host, out string? hp) ? hp : null;
            string? header = ctx.Request.RetrieveHeaderValue(PropertyHeader);
            string? app = ctx.Request.RetrieveHeaderValue(AppHeader);
            header = String.IsNullOrWhiteSpace(header) ? null : header.Trim();
            app = String.IsNullOrWhiteSpace(app) ? null : app.Trim();

            // 2. X-TK-Property.
            if (header != null)
            {
                PropertyRuntime? named = Find(header);
                if (named == null || !named.IsEnabled || (single != null && named.Id != single.Id)) return PropertyResolution.NotFound(PropertyResolutionSourceEnum.Header);
                if (hostProperty != null && hostProperty != named.Id)
                    return PropertyResolution.Fail(PropertyResolutionSourceEnum.Header, 400, "PropertyMismatch", "X-TK-Property and the request host name different properties.", "mismatch");
                return WithApp(named, PropertyResolutionSourceEnum.Header, app, ctx);
            }

            // 3. Host matches a property domain.
            if (hostProperty != null)
            {
                PropertyRuntime? byHost = Find(hostProperty);
                if (byHost == null || !byHost.IsEnabled) return PropertyResolution.NotFound(PropertyResolutionSourceEnum.Host);
                return WithApp(byHost, PropertyResolutionSourceEnum.Host, app, ctx);
            }

            // 5. SingleProperty: the one property.
            if (single != null)
            {
                if (!single.IsEnabled) return PropertyResolution.NotFound(PropertyResolutionSourceEnum.Single);
                return WithApp(single, PropertyResolutionSourceEnum.Single, app, ctx);
            }

            // 6. Nothing matched.
            return PropertyResolution.NotFound(PropertyResolutionSourceEnum.Host);
        }

        private PropertyResolution WithApp(PropertyRuntime runtime, PropertyResolutionSourceEnum source, string? app, HttpContextBase ctx)
        {
            if (app != null)
            {
                if (!runtime.Apps.Any(a => a.Enabled && String.Equals(a.AppId, app, StringComparison.OrdinalIgnoreCase)))
                    return PropertyResolution.Fail(source, 403, "AppNotRegistered", "X-TK-App is not a registered app of this property.", "app_not_registered");
            }
            else if (Mode == DeploymentModeEnum.MultiProperty && _Settings.Deployment.RequireAppHeader &&
                String.Equals(ctx.Request.RetrieveHeaderValue("X-Client"), "mobile", StringComparison.OrdinalIgnoreCase))
            {
                return PropertyResolution.Fail(source, 403, "AppNotRegistered", "Mobile requests must name a registered app in X-TK-App.", "app_not_registered");
            }
            return new PropertyResolution { Runtime = runtime, Source = source, AppId = app };
        }

        private string RequestHost(HttpContextBase ctx)
        {
            string? raw = null;
            if (_Settings.Deployment.TrustForwardedHost) raw = ctx.Request.RetrieveHeaderValue("X-Forwarded-Host");
            if (String.IsNullOrWhiteSpace(raw)) raw = ctx.Request.RetrieveHeaderValue("Host");
            return NormalizeHost(raw);
        }

        private static bool IsSpecial(string path)
        {
            return StartsWith(path, Constants.ApiPrefix + "/health") || StartsWith(path, Constants.ApiPrefix + "/webhooks") ||
                String.Equals(path, "/openapi.json", StringComparison.OrdinalIgnoreCase) || String.Equals(path, "/swagger", StringComparison.OrdinalIgnoreCase);
        }

        private static bool StartsWith(string path, string prefix)
        {
            return path.Equals(prefix, StringComparison.OrdinalIgnoreCase) || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static string AppKey(AppPlatformEnum platform, string appId)
        {
            return platform + ":" + appId.Trim().ToLowerInvariant();
        }

        private void Publish(Dictionary<string, PropertyRuntime> runtimes)
        {
            Dictionary<string, string> hosts = new Dictionary<string, string>(StringComparer.Ordinal);
            Dictionary<string, string> claimed = new Dictionary<string, string>(StringComparer.Ordinal);
            Dictionary<string, string> apps = new Dictionary<string, string>(StringComparer.Ordinal);
            HashSet<string> origins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PropertyRuntime runtime in runtimes.Values)
            {
                foreach (PropertyDomain domain in runtime.Domains)
                {
                    claimed[domain.Host] = runtime.Id;
                    // Resolved decision 6: a domain a property administrator added is routed only after its DNS TXT
                    // ownership check passed; platform-registered and folder domains are verified on registration.
                    if (domain.VerifiedUtc == null) continue;
                    hosts[domain.Host] = runtime.Id;
                    if (runtime.IsEnabled && (domain.Kind == DomainKindEnum.Web || domain.Kind == DomainKindEnum.Alias)) origins.Add("https://" + domain.Host);
                }
                foreach (PropertyApp app in runtime.Apps.Where(a => a.Enabled)) apps[AppKey(app.Platform, app.AppId)] = runtime.Id;
            }
            _Runtimes = runtimes;
            _Hosts = hosts;
            _ClaimedHosts = claimed;
            _AppsByKey = apps;
            _WebOrigins = origins;
        }

        private async Task SeedAsync(PropertyConfiguration seed, CancellationToken token)
        {
            DateTime now = DateTime.UtcNow;
            string document = ReadDocument(seed);
            string hash = Hash(document);
            PropertyRecord? existing = await _Database.Registry.ReadAsync(seed.Id, token).ConfigureAwait(false);
            if (existing == null)
            {
                if (Mode == DeploymentModeEnum.MultiProperty && await _Database.Registry.CountLiveAsync(token).ConfigureAwait(false) >= _Settings.Deployment.MaxProperties)
                {
                    _Logger.Warn("[PropertyDirectory] property " + seed.Id + " from property.directory was not registered: deployment.maxProperties reached");
                    return;
                }
                PropertyRecord record = new PropertyRecord
                {
                    Id = seed.Id,
                    Slug = seed.EffectiveSlug,
                    Name = seed.Name,
                    Status = PropertyStatusEnum.Enabled,
                    DocumentJson = document,
                    SourceDirectory = String.IsNullOrEmpty(seed.Directory) ? null : seed.Directory,
                    DirectoryHash = hash,
                    EnabledUtc = now,
                    CreatedUtc = now,
                    UpdatedUtc = now
                };
                await _Database.Registry.CreateAsync(record, DomainsOf(seed, now), AppsOf(seed, now), token).ConfigureAwait(false);
                _Logger.Info("[PropertyDirectory] registered property " + seed.Id + " (" + seed.Name + ", slug " + record.Slug + ") from " + seed.Directory);
                return;
            }
            if (existing.Status == PropertyStatusEnum.Deleted || existing.Status == PropertyStatusEnum.Deleting)
            {
                if (Mode == DeploymentModeEnum.SingleProperty)
                    throw new InvalidOperationException("Property " + seed.Id + " in property.directory was deleted from this database.");
                return;
            }

            // Folder changes win when property.json changed on disk (resolved decision 15); otherwise stored edits stand.
            bool changed = existing.DirectoryHash != hash || existing.SourceDirectory != (String.IsNullOrEmpty(seed.Directory) ? null : seed.Directory);
            if (!changed) return;
            existing.Name = seed.Name;
            existing.Slug = seed.EffectiveSlug;
            existing.DocumentJson = document;
            existing.SourceDirectory = String.IsNullOrEmpty(seed.Directory) ? null : seed.Directory;
            existing.DirectoryHash = hash;
            existing.UpdatedUtc = now;
            await _Database.Registry.UpdateAsync(existing, token).ConfigureAwait(false);
            if (Mode == DeploymentModeEnum.SingleProperty)
                await _Database.Registry.ReplaceDomainsAndAppsAsync(seed.Id, DomainsOf(seed, now), AppsOf(seed, now), now, token).ConfigureAwait(false);
            _Logger.Info("[PropertyDirectory] property " + seed.Id + " synchronized from " + seed.Directory);
        }

        private PropertyConfiguration? ParseDocument(PropertyRecord record)
        {
            try
            {
                PropertyConfiguration configuration = PropertyLoader.Parse(record.DocumentJson, "registry property " + record.Id);
                List<string> problems = PropertyValidator.Validate(configuration);
                if (problems.Count > 0 || configuration.Id != record.Id)
                {
                    _Logger.Error("[PropertyDirectory] property " + record.Id + " has an invalid stored document and is not served: " + String.Join("; ", problems));
                    return null;
                }
                configuration.Directory = record.SourceDirectory ?? String.Empty;
                return configuration;
            }
            catch (PropertyConfigurationException e)
            {
                _Logger.Error("[PropertyDirectory] property " + record.Id + " has an unreadable stored document and is not served: " + e.Message);
                return null;
            }
        }

        private static string ReadDocument(PropertyConfiguration seed)
        {
            if (!String.IsNullOrEmpty(seed.Directory))
            {
                string file = Path.Combine(seed.Directory, PropertyLoader.PropertyFileName);
                if (File.Exists(file)) return File.ReadAllText(file);
            }
            return System.Text.Json.JsonSerializer.Serialize(seed);
        }

        private static string Hash(string text)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        }

        private async Task RefreshLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_Settings.Deployment.PropertyCacheSeconds), token).ConfigureAwait(false);
                    DateTime? latest = await _Database.Registry.ReadLatestChangeAsync(token).ConfigureAwait(false);
                    if (latest != _LastChange) await ReloadAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception e)
                {
                    _Logger.Warn("[PropertyDirectory] registry refresh failed: " + e.Message);
                }
            }
        }

        #endregion
    }
}
