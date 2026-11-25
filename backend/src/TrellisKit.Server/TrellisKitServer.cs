namespace TrellisKit.Server
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Integrations;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Routes;
    using TrellisKit.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Watson host for the TrellisKit engine API: CORS, authentication hook, OpenAPI, request capture, and route registrars.
    /// </summary>
    public sealed class TrellisKitServer : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Running settings.
        /// </summary>
        public AppSettings Settings { get; }

        /// <summary>
        /// Database driver.
        /// </summary>
        public DatabaseDriverBase Database { get; }

        /// <summary>
        /// Application services of the property loaded from property.directory (the only property in SingleProperty
        /// mode). Per-request services come from <see cref="Directory"/>.
        /// </summary>
        public ApplicationServices Services { get; }

        /// <summary>
        /// Registered properties and per-request property resolution.
        /// </summary>
        public PropertyDirectory Directory { get; }

        /// <summary>
        /// Platform administration (MultiProperty).
        /// </summary>
        public PlatformService Platform { get; }

        /// <summary>
        /// Deployment-level realtime bus (realtime.bus: PostgreSQL LISTEN and NOTIFY by default, in-process otherwise).
        /// </summary>
        public TrellisKit.Server.Services.Realtime.IRealtimeBus RealtimeBus { get; }

        /// <summary>
        /// The WebSocket gateway of this task (GET /api/v1/ws).
        /// </summary>
        public TrellisKit.Server.Services.Realtime.RealtimeGateway Realtime { get; }

        /// <summary>
        /// Watson webserver.
        /// </summary>
        public Webserver Server { get; }

        /// <summary>
        /// Server version, for example "1.0.0".
        /// </summary>
        public string Version { get; }

        /// <summary>
        /// UTC time the host was created.
        /// </summary>
        public DateTime StartedUtc { get; }

        #endregion

        #region Private-Members

        private const string _AllowedMethods = "GET, POST, PUT, PATCH, DELETE, OPTIONS, HEAD";
        private const string _ExposedHeaders = "ETag, Retry-After, X-Continuation-Token, X-Total-Records, X-TK-Visitor-Token";
        private const string _AllowedHeaders = "Content-Type, Authorization, X-Api-Key, X-Client, X-Client-Version, X-TK-Property, X-TK-App, X-TK-Client, X-Client-Platform, X-Client-Update-Id, X-TK-Visitor-Id, X-TK-Visitor-Token, If-Match, If-None-Match";
        private readonly RequestHistoryCaptureService _Capture;
        private readonly SettingsService _SettingsService;
        private readonly ServerLifetime _Lifetime;
        private readonly AppLogger _Logger;
        private bool _Disposed = false;
        private TrellisKit.Server.Workers.RealtimeRegistryPruneWorker? _RealtimePrune = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the host with one integration set shared by every property (tests and single-property
        /// deployments). Call <see cref="Start"/> to begin listening.
        /// </summary>
        /// <param name="settings">Running settings.</param>
        /// <param name="property">Property loaded from property.directory (validated).</param>
        /// <param name="database">Initialized deployment database driver.</param>
        /// <param name="integrations">External integrations (real implementations, or fakes in tests).</param>
        /// <param name="capture">Request capture service.</param>
        /// <param name="settingsService">Settings service.</param>
        /// <param name="lifetime">Server lifetime.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public TrellisKitServer(
            AppSettings settings,
            PropertyConfiguration property,
            DatabaseDriverBase database,
            IntegrationSet integrations,
            RequestHistoryCaptureService capture,
            SettingsService settingsService,
            ServerLifetime lifetime,
            AppLogger logger)
            : this(settings, property, database, SharedIntegrations(integrations), capture, settingsService, lifetime, logger)
        {
        }

        /// <summary>
        /// Instantiate the host. Call <see cref="Start"/> to begin listening.
        /// </summary>
        /// <param name="settings">Running settings.</param>
        /// <param name="property">Property loaded from property.directory (validated). In MultiProperty it is registered on
        /// first start and serves as the deployment's default property.</param>
        /// <param name="database">Initialized deployment database driver.</param>
        /// <param name="integrations">Builds the external integrations of one property.</param>
        /// <param name="capture">Request capture service.</param>
        /// <param name="settingsService">Settings service.</param>
        /// <param name="lifetime">Server lifetime.</param>
        /// <param name="logger">Logger.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public TrellisKitServer(
            AppSettings settings,
            PropertyConfiguration property,
            DatabaseDriverBase database,
            Func<PropertyConfiguration, IntegrationSet> integrations,
            RequestHistoryCaptureService capture,
            SettingsService settingsService,
            ServerLifetime lifetime,
            AppLogger logger)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Database = (database ?? throw new ArgumentNullException(nameof(database))).Deployment;
            _Capture = capture ?? throw new ArgumentNullException(nameof(capture));
            _SettingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _Lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            ArgumentNullException.ThrowIfNull(integrations);
            Directory = new PropertyDirectory(settings, Database, integrations, logger);
            RealtimeBus = CreateRealtimeBus(settings, Database, logger);
            Directory.RealtimeBus = RealtimeBus;
            Realtime = new TrellisKit.Server.Services.Realtime.RealtimeGateway(settings, Directory, RealtimeBus, logger);
            Directory.RealtimeSink = Realtime;
            PropertyRuntime seed = Directory.Prepare(property ?? throw new ArgumentNullException(nameof(property)))!;
            Services = seed.Services;
            Platform = new PlatformService(settings, Database, Directory, _Lifetime, logger);
            _Capture.PropertyOf = ctx => Directory.ResolutionOf(ctx)?.PropertyLabel;
            _Capture.Rest = Settings.Rest;

            Version = ResolveVersion();
            StartedUtc = DateTime.UtcNow;

            WebserverSettings webserverSettings = new WebserverSettings(Settings.Rest.Hostname, Settings.Rest.Port, Settings.Rest.Ssl);
            webserverSettings.Telemetry.Enable = true;
            webserverSettings.Telemetry.EnableMetrics = true;
            webserverSettings.Telemetry.EnableTraces = true;
            webserverSettings.Telemetry.PropagateContext = true;
            // Realtime (docs/PHASE5_CONTRACT.md section 4 and as-built note A): Watson hosts the WebSocket route; every open
            // socket holds one request slot, so the request limit sits above realtime.maxConnectionsPerTask.
            webserverSettings.Telemetry.CaptureWebSocketMetrics = true;
            webserverSettings.WebSockets.Enable = true;
            webserverSettings.WebSockets.MaxMessageSize = Settings.Realtime.MaxInboundFrameBytes;
            webserverSettings.IO.MaxRequests = Math.Max(webserverSettings.IO.MaxRequests, Settings.Realtime.MaxConnectionsPerTask + 1024);
            Server = new Webserver(webserverSettings, DefaultRouteAsync);

            // Watson's defaults allow every origin; CORS is decided per request from Settings.Cors instead.
            Server.Settings.Headers.DefaultHeaders.Remove(WebserverConstants.HeaderAccessControlAllowOrigin);
            Server.Settings.Headers.DefaultHeaders.Remove(WebserverConstants.HeaderAccessControlAllowMethods);
            Server.Settings.Headers.DefaultHeaders.Remove(WebserverConstants.HeaderAccessControlAllowHeaders);
            Server.Settings.Headers.DefaultHeaders.Remove(WebserverConstants.HeaderAccessControlExposeHeaders);
            // Request-only headers have no meaning on a response; Watson's defaults would send them (Host echoed the host).
            foreach (string requestOnly in new[] { "Host", "Accept", "Accept-Language", "Accept-Charset" })
                Server.Settings.Headers.DefaultHeaders.Remove(requestOnly);

            ConfigureServer();
            ConfigureRoutes();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Seed and load the property registry, bootstrap the platform administrator (MultiProperty), and begin
        /// listening.
        /// </summary>
        /// <exception cref="InvalidOperationException">A startup check failed; the message names the problem.</exception>
        public void Start()
        {
            StartAsync(_Lifetime.Token).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Seed and load the property registry, bootstrap the platform administrator (MultiProperty), start the
        /// registry refresh loop and (when <see cref="PropertyDirectory.RunWorkers"/> is set) every enabled property's
        /// workers, and begin listening.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="InvalidOperationException">A startup check failed; the message names the problem.</exception>
        public async Task StartAsync(CancellationToken token = default)
        {
            await Directory.InitializeAsync(token).ConfigureAwait(false);
            if (Settings.Deployment.Mode == DeploymentModeEnum.MultiProperty) await Platform.BootstrapAsync(token).ConfigureAwait(false);
            await Services.Accounts.CreateBootstrapAdminIfNoMembersAsync(token).ConfigureAwait(false);
            Directory.Start(_Lifetime.Token);
            // The bus and gateway outlive the lifetime token so SIGTERM can drain sockets in Stop(); Dispose ends them.
            await RealtimeBus.StartAsync(CancellationToken.None).ConfigureAwait(false);
            Realtime.Start(CancellationToken.None);
            if (Directory.RunWorkers && _RealtimePrune == null)
            {
                _RealtimePrune = new TrellisKit.Server.Workers.RealtimeRegistryPruneWorker(Database, _Logger);
                _RealtimePrune.Start(_Lifetime.Token);
            }
            Server.Start(_Lifetime.Token);
            _Logger.Info("[TrellisKitServer] listening on http" + (Settings.Rest.Ssl ? "s" : String.Empty) + "://" + Settings.Rest.Hostname + ":" + Settings.Rest.Port + " (version " + Version + ", database " + Database.DatabaseType + ")");
        }

        /// <summary>
        /// Stop listening.
        /// </summary>
        public void Stop()
        {
            if (!Server.IsListening) return;
            // Drain realtime sockets first (shutdown, then 1012 over realtime.drainSeconds): Watson's Stop closes every
            // remaining socket with 1000.
            try
            {
                Realtime.DrainAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                _Logger.Warn("[TrellisKitServer] realtime drain failed: " + e.Message);
            }
            // Another caller (a restart request) may have stopped the listener while the sockets drained.
            if (!Server.IsListening) return;
            try
            {
                Server.Stop();
            }
            catch (InvalidOperationException)
            {
                // Already stopped concurrently.
            }
        }

        /// <summary>
        /// Dispose the host.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            Stop();
            Server.Dispose();
            Realtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _RealtimePrune?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            RealtimeBus.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Directory.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        #endregion

        #region Private-Methods

        private static Func<PropertyConfiguration, IntegrationSet> SharedIntegrations(IntegrationSet integrations)
        {
            ArgumentNullException.ThrowIfNull(integrations);
            return property => integrations;
        }

        private static TrellisKit.Server.Services.Realtime.IRealtimeBus CreateRealtimeBus(AppSettings settings, DatabaseDriverBase database, AppLogger logger)
        {
            if (settings.Realtime.Enabled && settings.Realtime.Bus == RealtimeBusEnum.Postgres && database.DatabaseType == DatabaseTypeEnum.Postgresql)
                return new TrellisKit.Server.Services.Realtime.PostgresRealtimeBus(settings.Database, database, logger);
            return new TrellisKit.Server.Services.Realtime.InProcessRealtimeBus();
        }

        private static string ResolveVersion()
        {
            System.Version? version = typeof(TrellisKitServer).Assembly.GetName().Version;
            return version == null ? "0.0.0" : version.ToString(3);
        }

        private void ConfigureServer()
        {
            Server.Routes.AuthenticateRequest = ctx => Directory.For(ctx).Authentication.AuthenticateRequestAsync(ctx);
            RouteHelper.UnhandledExceptionLogger = (ctx, e) => _Logger.Error("[TrellisKitServer] unhandled error in " + ctx.Request.Method + " " + ctx.Request.Url.RawWithoutQuery, e);
            Server.UseOpenApi(openApi =>
            {
                string name = Settings.Deployment.Mode == DeploymentModeEnum.MultiProperty ? Constants.EngineName + " (" + Settings.Deployment.Name + ")" : Services.Property.Name;
                openApi.Info.Title = name + " API";
                openApi.Info.Version = Version;
                openApi.Info.Description = "REST API for " + name + " (" + Constants.EngineName + " engine, " + Settings.Deployment.Mode + "). Every request resolves to one property: X-TK-Property (and X-TK-App from apps), else the request host. Member routes take Authorization: Bearer <accessToken>; admin routes take a SystemAdmin access token or the break-glass admin token; platform routes take a platform administrator token.";
            });

            DescribeDocumentRoute("/openapi.json", "OpenAPI document", "application/json", "The OpenAPI 3 document of every route. Anonymous.");
            DescribeDocumentRoute("/swagger", "Swagger UI", "text/html", "Swagger UI for the OpenAPI document. Anonymous.");

            Server.Routes.Preflight = PreflightAsync;
            Server.Routes.PreRouting = PreRoutingAsync;
            Server.Routes.PostRouting = PostRoutingAsync;
        }

        private bool HasGetRoute(string path)
        {
            if (String.IsNullOrEmpty(path) || Server == null) return false;
            foreach (WatsonWebserver.Core.Routing.RoutingGroup group in new[] { Server.Routes.PreAuthentication, Server.Routes.PostAuthentication })
            {
                if (group.Static.Match(HttpMethod.GET, path, out WatsonWebserver.Core.Routing.StaticRoute? _) != null) return true;
                if (group.Parameter.Match(HttpMethod.GET, path, out System.Collections.Specialized.NameValueCollection _, out WatsonWebserver.Core.Routing.ParameterRoute? _) != null) return true;
                if (group.Dynamic.Match(HttpMethod.GET, path, out WatsonWebserver.Core.Routing.DynamicRoute? _) != null) return true;
            }
            return false;
        }

        private void DescribeDocumentRoute(string path, string summary, string contentType, string description)
        {
            foreach (WatsonWebserver.Core.Routing.RoutingGroup group in new[] { Server.Routes.PreAuthentication, Server.Routes.PostAuthentication })
            {
                WatsonWebserver.Core.Routing.StaticRoute? route = group.Static.Get(HttpMethod.GET, path);
                if (route == null) continue;
                group.Static.Remove(HttpMethod.GET, path);
                group.Static.Add(HttpMethod.GET, path, route.Handler, route.ExceptionHandler,
                    openApiMetadata: Routes.OpenApiExtensions.File(Routes.OpenApiExtensions.Doc(summary, "OpenAPI", description), contentType, summary));
                return;
            }
        }

        private void ConfigureRoutes()
        {
            new HealthRoutes(Database, Version, StartedUtc) { Draining = () => Realtime.IsDraining }.Register(Server);
            new RealtimeRoutes(Realtime).Register(Server);
            new RequestHistoryRoutes(Settings.RequestHistory, Directory).Register(Server);
            new SettingsRoutes(_SettingsService, _Lifetime, _Logger, Services.Audit) { PlatformOnly = Settings.Deployment.Mode == DeploymentModeEnum.MultiProperty }.Register(Server);
            new PropertyRoutes(Directory).Register(Server);
            new ProviderRoutes(Directory).Register(Server);
            new AuthRoutes(Directory).Register(Server);
            new MeRoutes(Directory).Register(Server);
            new UserRoutes(Directory).Register(Server);
            new MediaRoutes(Directory).Register(Server);
            new MembershipRoutes(Directory).Register(Server);
            new SignupCodeRoutes(Directory).Register(Server);
            new WebhookRoutes(Directory).Register(Server);
            new AdminMemberRoutes(Directory).Register(Server);
            new AdminSubscriptionRoutes(Directory).Register(Server);
            new AdminSignupCodeRoutes(Directory).Register(Server);
            new AdminAuditRoutes(Directory).Register(Server);
            new BlockRoutes(Directory).Register(Server);
            new WordFilterRoutes(Directory).Register(Server);
            new ProfileFieldRoutes(Directory).Register(Server);
            new EmailRoutes(Directory).Register(Server);
            new PropertyDomainRoutes(Directory, Platform).Register(Server);
            new PropertyAccessRoutes(Directory).Register(Server);
            new PropertyBrandingRoutes(Directory).Register(Server);
            new DocumentRoutes(Directory).Register(Server);
            new MemberSearchRoutes(Directory).Register(Server);
            new LinkPreviewRoutes(Directory).Register(Server);
            new AdminMediaRoutes(Directory).Register(Server);
            new MetadataRoutes(Directory).Register(Server);
            new AccountExportRoutes(Directory).Register(Server);
            new NotificationRoutes(Directory).Register(Server);
            new EmailUnsubscribeRoutes(Directory).Register(Server);
            new FeedRoutes(Directory).Register(Server);
            new PublicFeedRoutes(Directory).Register(Server);
            new CommentRoutes(Directory).Register(Server);
            new ReactionRoutes(Directory).Register(Server);
            new AdminFeedRoutes(Directory).Register(Server);
            new ReportRoutes(Directory).Register(Server);
            new ModerationRoutes(Directory).Register(Server);
            new AdminModeratorRoutes(Directory).Register(Server);
            new ForumRoutes(Directory).Register(Server);
            new PublicThreadRoutes(Directory).Register(Server);
            new AdminForumRoutes(Directory).Register(Server);
            new LibraryRoutes(Directory).Register(Server);
            new EventRoutes(Directory).Register(Server);
            new DirectoryRoutes(Directory).Register(Server);
            new ProfileMediaRoutes(Directory).Register(Server);
            new PublicPageRoutes(Directory).Register(Server);
            new IdeaRoutes(Directory).Register(Server);
            new AdminIdeaRoutes(Directory).Register(Server);
            new PublicIdeaRoutes(Directory).Register(Server);
            new TicketRoutes(Directory).Register(Server);
            new AdminTicketRoutes(Directory).Register(Server);
            new AdminBroadcastRoutes(Directory).Register(Server);
            new AnnouncementRoutes(Directory).Register(Server);
            new ConversationRoutes(Directory).Register(Server);
            new MessageRoutes(Directory).Register(Server);
            new MessagingSettingsRoutes(Directory).Register(Server);
            new AdminMessagingRoutes(Directory).Register(Server);
            new AdminAiEndpointRoutes(Directory).Register(Server);
            new AdminAiAssistantRoutes(Directory).Register(Server);
            new AdminAiPromptRoutes(Directory).Register(Server);
            new AdminJobRoutes(Directory).Register(Server);
            new AdminMetricsRoutes(Directory).Register(Server);
            new AdminSummaryRoutes(Directory).Register(Server);
            new ProductMetricsRoutes(Directory).Register(Server);
            new AdminAiRoleRoutes(Directory).Register(Server);
            new AdminAiHarnessRoutes(Directory).Register(Server);
            new AdminContentReviewRoutes(Directory).Register(Server);
            new AdminAiAnalyticsRoutes(Directory).Register(Server);
            new AssistantRoutes(Directory).Register(Server);
            new PlatformRoutes(Platform, _SettingsService, _Lifetime).Register(Server);
            new PlatformBrandingRoutes(Platform, _SettingsService).Register(Server);
            new PlatformDocumentRoutes(Platform, _SettingsService).Register(Server);
            new AdminUsageRoutes(Directory).Register(Server);
            new PlatformMetricsRoutes(Platform, new Services.Metrics.PlatformMetricsService(Platform, Settings), _SettingsService).Register(Server);
            new PlatformUsageRoutes(Platform, new Services.Usage.PlatformUsageService(Platform, Settings, _Logger), _SettingsService).Register(Server);
            new UpdateManifestRoutes(Directory).Register(Server);
            new AdminUpdateRoutes(Directory).Register(Server);
            new AdminUpdateKeyRoutes(Directory).Register(Server);
            new AppManifestRoutes(Directory).Register(Server);
            new AdminAppConfigRoutes(Directory).Register(Server);
            new AdminAppContentRoutes(Directory).Register(Server);
        }

        private void ApplyCors(HttpContextBase ctx)
        {
            string? origin = Directory.ResolveAllowedOrigin(ctx.Request.RetrieveHeaderValue("Origin"));
            if (origin == null) return;
            ctx.Response.Headers["Access-Control-Allow-Origin"] = origin;
            ctx.Response.Headers["Access-Control-Allow-Methods"] = _AllowedMethods;
            ctx.Response.Headers["Access-Control-Allow-Headers"] = _AllowedHeaders;
            ctx.Response.Headers["Access-Control-Expose-Headers"] = _ExposedHeaders;
            if (origin != "*")
            {
                ctx.Response.Headers["Access-Control-Allow-Credentials"] = "true";
                ctx.Response.Headers["Vary"] = "Origin";
            }
        }

        private async Task PreflightAsync(HttpContextBase ctx)
        {
            ApplyCors(ctx);
            ctx.Response.Headers["Access-Control-Max-Age"] = "86400";
            ctx.Response.StatusCode = 204;
            await ctx.Response.Send(ctx.Token).ConfigureAwait(false);
        }

        private async Task PreRoutingAsync(HttpContextBase ctx)
        {
            ApplyCors(ctx);
            if (ctx.Request.Method == HttpMethod.HEAD && HasGetRoute(ctx.Request.Url.RawWithoutQuery ?? String.Empty))
            {
                // HEAD is not served; say which method is (405 with Allow, no body) instead of a misleading 404.
                ctx.Response.StatusCode = 405;
                ctx.Response.Headers["Allow"] = "GET";
                await ctx.Response.Send(ctx.Token).ConfigureAwait(false);
                return;
            }
            PropertyResolution resolution = Directory.Resolve(ctx);
            if (resolution.Failed)
            {
                await RouteHelper.SendErrorAsync(ctx, resolution.StatusCode, resolution.Error ?? "PropertyNotFound", resolution.Message ?? "No property matches this request.").ConfigureAwait(false);
                return;
            }
            // GET /api/v1/ws: every pre-upgrade check (as-built note A) runs here, so a refused upgrade gets a normal HTTP
            // error and never reaches Watson's handshake.
            if (resolution.Runtime != null && TrellisKit.Server.Services.Realtime.RealtimeGateway.IsPath(ctx.Request.Url.RawWithoutQuery))
            {
                await Realtime.CheckUpgradeAsync(ctx, resolution.Runtime).ConfigureAwait(false);
                return;
            }
            // Kill switches on anonymous routes (sign-up, public feed); authenticated routes are gated after authorization.
            if (resolution.Runtime != null && AuthorizationService.Classify(ctx.Request.Url.RawWithoutQuery) == RouteGroupEnum.Anonymous)
                await resolution.Runtime.Services.LiveUpdates.GateAsync(ctx, null).ConfigureAwait(false);
        }

        private Task PostRoutingAsync(HttpContextBase ctx)
        {
            ctx.Timestamp.End = DateTime.UtcNow;

            if (Settings.Logging.LogRequests)
            {
                _Logger.Debug(
                    "[TrellisKitServer] [p:" + (Directory.ResolutionOf(ctx)?.PropertyLabel ?? "unresolved") + "] " + ctx.Request.Method + " " + ctx.Request.Url.RawWithQuery + " " + ctx.Response.StatusCode +
                    " (" + (ctx.Timestamp.TotalMs.HasValue ? ctx.Timestamp.TotalMs.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) : "?") + "ms)");
            }

            if (Settings.RequestHistory.Enabled) _Capture.Capture(ctx);
            RecordUsage(ctx);
            return Task.CompletedTask;
        }

        private void RecordUsage(HttpContextBase ctx)
        {
            // Usage metering (docs/USAGE_METERING_CONTRACT.md section 2.5): API requests resolved to a property, except health.
            string path = ctx.Request.Url.RawWithoutQuery ?? String.Empty;
            if (!path.StartsWith("/api/", StringComparison.Ordinal) || path.StartsWith(Constants.ApiPrefix + "/health", StringComparison.Ordinal)) return;
            PropertyRuntime? runtime = Directory.ResolutionOf(ctx)?.Runtime;
            runtime?.Services.Usage.Recorder.RecordRequest(ctx.Response.StatusCode, DateTime.UtcNow);
        }

        private static async Task DefaultRouteAsync(HttpContextBase ctx)
        {
            await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "No route matches " + ctx.Request.Method + " " + ctx.Request.Url.RawWithoutQuery + ".").ConfigureAwait(false);
        }

        #endregion
    }
}
