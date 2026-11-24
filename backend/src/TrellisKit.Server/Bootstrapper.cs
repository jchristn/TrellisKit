namespace TrellisKit.Server
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
    using TrellisKit.Core;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Integrations;
    using TrellisKit.Core.Integrations.Implementations;
    using TrellisKit.Core.Integrations.Interfaces;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Security;
    using TrellisKit.Core.Settings;
    using TrellisKit.Server.Services;
    using TrellisKit.Server.Settings;
    using TrellisKit.Server.Workers;

    /// <summary>
    /// Composition root: loads settings, creates logging, telemetry, the database, services, workers, and the server,
    /// then waits for shutdown or a restart request.
    /// </summary>
    public static class Bootstrapper
    {
        #region Public-Members

        /// <summary>
        /// Attempts to connect to the database at startup before giving up. Default 20. Minimum 1, maximum 1000.
        /// Server databases in containers may take a few seconds to accept connections.
        /// </summary>
        public static int DatabaseConnectAttempts
        {
            get { return _DatabaseConnectAttempts; }
            set { _DatabaseConnectAttempts = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// Delay between database connection attempts in milliseconds. Default 3000. Minimum 100, maximum 60000.
        /// </summary>
        public static int DatabaseConnectDelayMs
        {
            get { return _DatabaseConnectDelayMs; }
            set { _DatabaseConnectDelayMs = Math.Clamp(value, 100, 60000); }
        }

        #endregion

        #region Private-Members

        private static int _DatabaseConnectAttempts = 20;
        private static int _DatabaseConnectDelayMs = 3000;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run the server until stopped.
        /// </summary>
        /// <param name="args">Command-line arguments. args[0], if present, is the settings file path.</param>
        /// <returns>Process exit code: 0 on a normal stop, the restart code after a restart request, 1 on startup failure.</returns>
        public static int Run(string[] args)
        {
            return RunAsync(args).GetAwaiter().GetResult();
        }

        #endregion

        #region Private-Methods

        private static async Task<int> RunAsync(string[] args)
        {
            string settingsPath = ResolveSettingsPath(args);
            SettingsStore store = new SettingsStore(settingsPath);

            AppSettings settings;
            string? generatedToken;
            try
            {
                settings = store.LoadEffective(out generatedToken);
            }
            catch (InvalidOperationException e)
            {
                Console.Error.WriteLine(Constants.EngineName + " server failed to start: " + e.Message);
                return 1;
            }

            // Fail fast on a missing or invalid secrets key outside local development: instances with their own
            // generated keys cannot decrypt each other's values.
            List<string> keyProblems = SecretKeyCheck.Problems(settings.Secrets);
            if (keyProblems.Count > 0)
            {
                Console.Error.WriteLine(Constants.EngineName + " server failed to start: " + String.Join(" ", keyProblems));
                return 1;
            }

            PropertyConfiguration property;
            try
            {
                property = PropertyLoader.Load(PropertyLoader.ResolveDirectory(settings.Property.Directory));
            }
            catch (PropertyConfigurationException e)
            {
                Console.Error.WriteLine(Constants.EngineName + " server failed to start: " + e.Message);
                return 1;
            }

            using (LoggingModule module = CreateLogging(settings.Logging))
            using (ServerLifetime lifetime = new ServerLifetime())
            {
                AppLogger logger = new AppLogger(module);
                logger.Info("[Bootstrapper] starting " + Constants.EngineName + " server (" + settings.Deployment.Mode + ") for property " + property.Id + " (" + property.Name + ") from " + property.Directory + "; settings " + store.FilePath);

                using (TelemetryHost telemetry = TelemetryHost.Start(settings.Telemetry, settings.Deployment.Mode == DeploymentModeEnum.MultiProperty ? "multi" : property.Id, logger))
                {
                    if (generatedToken != null)
                    {
                        logger.Warn("[Bootstrapper] no admin token was configured, so one was generated and saved to " + store.FilePath + ". Admin token (shown once): " + generatedToken);
                    }

                    using (PosixSignalRegistration sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; lifetime.RequestStop(); }))
                    using (PosixSignalRegistration sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx => { ctx.Cancel = true; lifetime.RequestStop(); }))
                    {
                        DatabaseDriverBase? database = await ConnectDatabaseAsync(settings.Database, property.Id, logger, lifetime.Token).ConfigureAwait(false);
                        if (database == null) return 1;

                        await using (database.ConfigureAwait(false))
                        using (SettingsService settingsService = new SettingsService(settings, store, logger))
                        {
                            RequestHistoryCaptureService capture = new RequestHistoryCaptureService(database, settings.RequestHistory, logger);
                            RequestHistoryPruneWorker pruneWorker = new RequestHistoryPruneWorker(database, settings.RequestHistory, logger);

                            using (HttpClient httpClient = CreateHttpClient())
                            await using (pruneWorker.ConfigureAwait(false))
                            using (TrellisKitServer server = new TrellisKitServer(settings, property, database, p => CreateIntegrations(settings, p, httpClient), capture, settingsService, lifetime, logger))
                            {
                                // Each enabled property runs its own workers (leases are per property); the purge worker
                                // and request history pruning are deployment-level.
                                server.Directory.RunWorkers = true;
                                PropertyPurgeWorker purgeWorker = new PropertyPurgeWorker(database, server.Directory, server.Services.Integrations.ObjectStore, settings.Storage.Directory, logger);
                                await using (purgeWorker.ConfigureAwait(false))
                                {
                                    try
                                    {
                                        await server.StartAsync(lifetime.Token).ConfigureAwait(false);
                                    }
                                    catch (InvalidOperationException e)
                                    {
                                        logger.Error("[Bootstrapper] startup check failed: " + e.Message);
                                        Console.Error.WriteLine(Constants.EngineName + " server failed to start: " + e.Message);
                                        return 1;
                                    }
                                    pruneWorker.Start(lifetime.Token);
                                    if (settings.Deployment.Mode == DeploymentModeEnum.MultiProperty) purgeWorker.Start(lifetime.Token);
                                    LogIntegrations(server.Services.Integrations, logger);

                                    try
                                    {
                                        await Task.Delay(Timeout.Infinite, lifetime.Token).ConfigureAwait(false);
                                    }
                                    catch (OperationCanceledException)
                                    {
                                        // Shutdown requested.
                                    }

                                    logger.Info(lifetime.RestartRequested
                                        ? "[Bootstrapper] restarting (exit code " + lifetime.ExitCode + ")"
                                        : "[Bootstrapper] stopping");
                                    server.Stop();
                                }
                            }
                        }
                    }
                }

                return lifetime.ExitCode;
            }
        }

        private static HttpClient CreateHttpClient()
        {
            HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TrellisKit-Server/1.0");
            return client;
        }

        private static IntegrationSet CreateIntegrations(AppSettings settings, PropertyConfiguration property, HttpClient httpClient)
        {
            IntegrationSet integrations = new IntegrationSet(
                new AppleIdentityTokenVerifier(settings.OAuth, httpClient),
                new GoogleIdentityTokenVerifier(settings.OAuth, httpClient),
                new FacebookTokenVerifier(settings.OAuth, httpClient),
                new AppStoreClient(settings.AppStore, httpClient),
                new GooglePlayClient(settings.GooglePlay, httpClient),
                new StripeClient(settings.Stripe, httpClient),
                new SmtpEmailSender(settings.Email, property.Contact.EmailFromName, property.Contact.EmailFromAddress),
                new Pbkdf2PasswordHasher(),
                new AesGcmTokenProtector(() => settings.Auth.TokenEncryptionKey));
            integrations.LinkPreviewFetcher = new HttpLinkPreviewFetcher(settings.LinkPreviews);
            integrations.Push = new ExpoPushSender(settings.Push, httpClient);
            integrations.TxtResolver = new DohTxtResolver(() => settings.Deployment.DomainVerificationResolverUrl);
            ConfigureMedia(integrations, settings, property.Id, httpClient);
            return integrations;
        }

        private static void ConfigureMedia(IntegrationSet set, AppSettings settings, string propertyId, HttpClient httpClient)
        {
            // media.storageMode and media.videoProcessor are Restart settings, so the implementations are chosen once here.
            IObjectStore store = settings.Media.StorageMode == MediaStorageModeEnum.S3
                ? new S3ObjectStore(settings.Media)
                : new LocalObjectStore(settings.Storage, new LocalUploadSigner(() => settings.Auth.TokenEncryptionKey));
            set.ObjectStore = store;
            set.ImageProcessor = new SkiaImageProcessor();
            switch (settings.Media.VideoProcessor)
            {
                case VideoProcessorEnum.MediaConvert:
                    set.VideoTranscoder = new MediaConvertTranscoder(settings.Media);
                    break;
                case VideoProcessorEnum.Ffmpeg:
                    set.VideoTranscoder = new FfmpegTranscoder(store, settings.Media);
                    break;
                default:
                    set.VideoTranscoder = new PassthroughTranscoder(store);
                    break;
            }
            set.MediaDelivery = settings.Media.StorageMode == MediaStorageModeEnum.S3
                ? new CloudFrontCookieSigner(settings.Media, propertyId)
                : new LocalMediaCookieSigner(set.TokenProtector);
            set.SnsVerifier = new SnsMessageVerifier(settings.Media, httpClient);
        }

        private static void LogIntegrations(IntegrationSet integrations, AppLogger logger)
        {
            logger.Info("[Bootstrapper] integrations: apple=" + integrations.Apple.IsConfigured + " google=" + integrations.Google.IsConfigured +
                " facebook=" + integrations.Facebook.IsConfigured + " stripe=" + integrations.Stripe.IsConfigured + " appStore=" + integrations.AppStore.IsConfigured +
                " googlePlay=" + integrations.GooglePlay.IsConfigured + " email=" + integrations.Email.IsConfigured + (integrations.Email.IsConfigured ? String.Empty : " (emails are logged instead of sent)"));
            logger.Info("[Bootstrapper] media: storage=" + integrations.ObjectStore.Mode + " video=" + integrations.VideoTranscoder.Label + " delivery=" + integrations.MediaDelivery.Mode);
            if (integrations.VideoTranscoder.Processor == VideoProcessorEnum.Passthrough)
                logger.Warn("[Bootstrapper] media.videoProcessor is Passthrough: uploaded videos are served as uploaded and their metadata (including location) is NOT stripped. Use Ffmpeg or MediaConvert for real deployments.");
        }

        private static string ResolveSettingsPath(string[] args)
        {
            if (args != null && args.Length > 0 && !String.IsNullOrWhiteSpace(args[0])) return args[0];
            string? fromEnvironment = Environment.GetEnvironmentVariable(Constants.SettingsFileEnvironmentVariable);
            if (!String.IsNullOrWhiteSpace(fromEnvironment)) return fromEnvironment;
            return Constants.DefaultSettingsFilename;
        }

        private static LoggingModule CreateLogging(TrellisKit.Core.Settings.LoggingSettings settings)
        {
            LoggingModule module;
            if (settings.FileLogging)
            {
                Directory.CreateDirectory(settings.LogDirectory);
                module = new LoggingModule(Path.Combine(settings.LogDirectory, settings.LogFilename), FileLoggingMode.FileWithDate, settings.ConsoleLogging);
            }
            else
            {
                module = new LoggingModule();
                module.Settings.EnableConsole = settings.ConsoleLogging;
            }
            return module;
        }

        private static async Task<DatabaseDriverBase?> ConnectDatabaseAsync(DatabaseSettings settings, string migrationPropertyId, AppLogger logger, CancellationToken token)
        {
            for (int attempt = 1; attempt <= _DatabaseConnectAttempts; attempt++)
            {
                try
                {
                    DatabaseDriverBase driver = await DatabaseDriverFactory.CreateAndInitializeAsync(settings, migrationPropertyId, token).ConfigureAwait(false);
                    logger.Info("[Bootstrapper] database " + settings.Type + " ready (schema version " + await driver.GetAppliedSchemaVersionAsync(token).ConfigureAwait(false) + ")");
                    return driver;
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
                catch (Exception e)
                {
                    if (attempt == _DatabaseConnectAttempts)
                    {
                        logger.Error("[Bootstrapper] PostgreSQL at " + settings.Hostname + ":" + settings.Port + " (database " + settings.DatabaseName + ") is unavailable after " + attempt + " attempts; TrellisKit requires PostgreSQL 17 or later with pgvector (for local development run docker compose up -d postgres in the engine docker folder); exiting", e);
                        return null;
                    }
                    logger.Warn("[Bootstrapper] database " + settings.Type + " not ready (attempt " + attempt + " of " + _DatabaseConnectAttempts + "): " + e.Message);
                    try
                    {
                        await Task.Delay(_DatabaseConnectDelayMs, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return null;
                    }
                }
            }
            return null;
        }

        #endregion
    }
}
