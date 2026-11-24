namespace Test.Shared.Suites
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
    using Test.Shared.Fakes;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Settings;

    /// <summary>
    /// A running server on a free loopback port, backed by an isolated test database and settings file.
    /// </summary>
    internal sealed class HttpFixture : IAsyncDisposable
    {
        internal const string AdminToken = "tk_http_fixture_admin_token_000000000001";

        internal AppSettings Settings { get; }

        internal ServerLifetime Lifetime { get; }

        internal HttpClient Client { get; }

        internal string BaseUrl { get; }

        internal FakeIntegrations Fakes { get; }

        /// <summary>
        /// Property the helpers act in: sent as X-TK-Property on every request and used for Database and Services.
        /// Null (default) sends no header and uses the sample property (SingleProperty tests).
        /// </summary>
        internal string? PropertyId { get; set; } = null;

        /// <summary>
        /// Host header sent on every request, or null for the loopback address.
        /// </summary>
        internal string? HostHeader { get; set; } = null;

        internal ApplicationServices Services
        {
            get { return PropertyId == null ? _Server.Services : (_Server.Directory.Find(PropertyId) ?? throw new InvalidOperationException("Unknown property " + PropertyId)).Services; }
        }

        internal TrellisKit.Core.Database.DatabaseDriverBase Database
        {
            get { return PropertyId == null ? _Database.Driver : _Database.Deployment.ForProperty(PropertyId); }
        }

        internal TrellisKit.Core.Database.DatabaseDriverBase Raw
        {
            get { return _Database.Deployment; }
        }

        internal TrellisKitServer Server
        {
            get { return _Server; }
        }

        internal const string BootstrapAdminEmail = "boss@example.com";

        private readonly TestDatabase _Database;
        private readonly SettingsStore _Store;
        private readonly SettingsService _SettingsService;
        private readonly LoggingModule _Module;
        private readonly TrellisKitServer _Server;
        private bool _OwnsDatabase = true;

        private HttpFixture(AppSettings settings, ServerLifetime lifetime, TestDatabase database, SettingsStore store, SettingsService settingsService, LoggingModule module, TrellisKitServer server, FakeIntegrations fakes)
        {
            Fakes = fakes;
            Settings = settings;
            Lifetime = lifetime;
            _Database = database;
            _Store = store;
            _SettingsService = settingsService;
            _Module = module;
            _Server = server;
            BaseUrl = "http://127.0.0.1:" + settings.Rest.Port;
            Client = new HttpClient { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(15) };
        }

        internal PropertyConfiguration Property
        {
            get { return _Server.Services.Property.Property; }
        }

        internal static async Task<HttpFixture> StartAsync(CancellationToken token, Action<PropertyConfiguration>? customizeProperty = null, Action<AppSettings>? customizeSettings = null)
        {
            PropertyConfiguration property = TestProperty.LoadSample();
            // The Phase 1 and 2 suites were written for the FullyGated paywall, so the fixture pins it; the sample
            // property itself is PartiallyGated, and AccessModelSuite covers every model (customizeProperty overrides).
            property.Access = new PropertyAccess { Model = TrellisKit.Core.Enums.AccessModelEnum.FullyGated };
            customizeProperty?.Invoke(property);
            TestDatabase database = await TestDatabase.CreateAsync(token).ConfigureAwait(false);
            int port = FreePort();
            SettingsStore store = new SettingsStore(
                Path.Combine(TestEnvironment.TempRoot, "http-settings-" + Guid.NewGuid().ToString("N") + ".json"),
                name => name == "TK_ADMIN_TOKEN" ? AdminToken : name == "TK_REST_PORT" ? port.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
            AppSettings settings = store.LoadEffective(out string? _);
            settings.Database = database.Settings;
            settings.Telemetry.Enabled = false;
            settings.Stripe.PriceId = "price_test_annual";
            settings.Auth.BootstrapAdminEmail = BootstrapAdminEmail;
            settings.Storage.Directory = Path.Combine(TestEnvironment.TempRoot, "objects-" + Guid.NewGuid().ToString("N"));
            customizeSettings?.Invoke(settings);
            FakeIntegrations fakes = new FakeIntegrations(settings.Auth, settings.Storage, settings.Media);

            LoggingModule module = TestLogging.CreateModule();
            AppLogger logger = new AppLogger(module);
            ServerLifetime lifetime = new ServerLifetime { RestartDelayMs = 50 };
            SettingsService settingsService = new SettingsService(settings, store, logger);
            TrellisKitServer server = new TrellisKitServer(
                settings,
                property,
                database.Driver,
                fakes.Set,
                new RequestHistoryCaptureService(database.Driver, settings.RequestHistory, logger),
                settingsService,
                lifetime,
                logger);
            server.Start();

            HttpFixture fixture = new HttpFixture(settings, lifetime, database, store, settingsService, module, server, fakes);
            await fixture.WaitUntilListeningAsync(token).ConfigureAwait(false);
            return fixture;
        }

        /// <summary>
        /// A second server instance on its own port against the primary's database schema, settings values, encryption
        /// keys, and fake integrations (two ECS tasks in one test, docs/PHASE5_CONTRACT.md section 4.11). Each instance
        /// has its own realtime bus listener. Disposing the peer leaves the shared database to the primary.
        /// </summary>
        internal static async Task<HttpFixture> StartPeerAsync(HttpFixture primary, CancellationToken token, Action<AppSettings>? customizeSettings = null)
        {
            int port = FreePort();
            SettingsStore store = new SettingsStore(
                Path.Combine(TestEnvironment.TempRoot, "http-settings-" + Guid.NewGuid().ToString("N") + ".json"),
                name => name == "TK_ADMIN_TOKEN" ? AdminToken : name == "TK_REST_PORT" ? port.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
            AppSettings settings = store.LoadEffective(out string? _);
            settings.Database = primary.Settings.Database;
            settings.Deployment = primary.Settings.Deployment;
            settings.Platform = primary.Settings.Platform;
            settings.Realtime = primary.Settings.Realtime;
            settings.Telemetry.Enabled = false;
            settings.Stripe.PriceId = primary.Settings.Stripe.PriceId;
            settings.Auth.BootstrapAdminEmail = BootstrapAdminEmail;
            settings.Auth.TokenEncryptionKey = primary.Settings.Auth.TokenEncryptionKey;
            settings.Secrets.EncryptionKey = primary.Settings.Secrets.EncryptionKey;
            settings.Storage.Directory = primary.Settings.Storage.Directory;
            customizeSettings?.Invoke(settings);
            LoggingModule module = TestLogging.CreateModule();
            AppLogger logger = new AppLogger(module);
            ServerLifetime lifetime = new ServerLifetime { RestartDelayMs = 50 };
            SettingsService settingsService = new SettingsService(settings, store, logger);
            TrellisKitServer server = new TrellisKitServer(
                settings,
                primary.Property,
                primary._Database.Driver,
                primary.Fakes.Set,
                new RequestHistoryCaptureService(primary._Database.Driver, settings.RequestHistory, logger),
                settingsService,
                lifetime,
                logger);
            server.Start();
            HttpFixture peer = new HttpFixture(settings, lifetime, primary._Database, store, settingsService, module, server, primary.Fakes) { _OwnsDatabase = false, PropertyId = primary.PropertyId };
            await peer.WaitUntilListeningAsync(token).ConfigureAwait(false);
            return peer;
        }

        internal HttpRequestMessage Request(HttpMethod method, string path, bool authenticated)
        {
            HttpRequestMessage request = new HttpRequestMessage(method, path);
            if (authenticated) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + AdminToken);
            Decorate(request);
            return request;
        }

        /// <summary>
        /// Add the current property header and host to a request unless the caller set them.
        /// </summary>
        internal void Decorate(HttpRequestMessage request)
        {
            if (PropertyId != null && !request.Headers.Contains("X-TK-Property"))
            {
                request.Headers.TryAddWithoutValidation("X-TK-Property", PropertyId);
                // Apps send their registered app id with the property (mobile requests require it in MultiProperty).
                PropertyRuntime? runtime = _Server.Directory.Find(PropertyId);
                string? app = runtime?.Apps.FirstOrDefault(a => a.Enabled)?.AppId;
                if (app != null && !request.Headers.Contains("X-TK-App")) request.Headers.TryAddWithoutValidation("X-TK-App", app);
            }
            if (HostHeader != null && request.Headers.Host == null) request.Headers.Host = HostHeader;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            _Server.Dispose();
            _SettingsService.Dispose();
            Lifetime.Dispose();
            if (!_OwnsDatabase)
            {
                _Module.Dispose();
                File.Delete(_Store.FilePath);
                return;
            }
            await _Database.DisposeAsync().ConfigureAwait(false);
            _Module.Dispose();
            try
            {
                File.Delete(_Store.FilePath);
                if (Directory.Exists(Settings.Storage.Directory)) Directory.Delete(Settings.Storage.Directory, true);
                if (Property.Directory.StartsWith(TestEnvironment.TempRoot, StringComparison.Ordinal)) TestProperty.Delete(Property.Directory);
            }
            catch (IOException)
            {
                // Temp file cleanup is best-effort.
            }
        }

        private async Task WaitUntilListeningAsync(CancellationToken token)
        {
            for (int attempt = 0; attempt < 50; attempt++)
            {
                try
                {
                    using (HttpResponseMessage response = await Client.GetAsync("/api/v1/health", token).ConfigureAwait(false))
                    {
                        if (response.StatusCode == HttpStatusCode.OK) return;
                    }
                }
                catch (HttpRequestException)
                {
                    // Not listening yet.
                }
                await Task.Delay(100, token).ConfigureAwait(false);
            }
            throw new InvalidOperationException("Test server did not start on " + BaseUrl + ".");
        }

        private static int FreePort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
