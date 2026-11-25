namespace Test.Shared
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;

    /// <summary>
    /// An initialized, isolated PostgreSQL schema for one test case. <see cref="Driver"/> is bound to the sample
    /// property (the property every single-property test server serves); <see cref="Deployment"/> is the deployment
    /// driver. Disposing it drops the schema.
    /// </summary>
    public sealed class TestDatabase : IAsyncDisposable
    {
        #region Public-Members

        /// <summary>
        /// Settings used to create the driver.
        /// </summary>
        public DatabaseSettings Settings { get; }

        /// <summary>
        /// Driver bound to the sample property.
        /// </summary>
        public DatabaseDriverBase Driver { get; }

        /// <summary>
        /// Deployment driver (registry, platform administrators, unscoped statements).
        /// </summary>
        public DatabaseDriverBase Deployment { get; }

        #endregion

        #region Private-Members

        // Historic migrations inspect PostgreSQL catalogs; serialize schema DDL so independent test schemas
        // cannot deadlock on catalog index inspection. Test actions themselves still execute concurrently.
        private static readonly SemaphoreSlim _SchemaDdl = new SemaphoreSlim(1, 1);
        private static readonly SemaphoreSlim _ServerLock = new SemaphoreSlim(1, 1);
        private static bool _ServerReady = false;

        #endregion

        #region Constructors-and-Factories

        private TestDatabase(DatabaseSettings settings, DatabaseDriverBase deployment)
        {
            Settings = settings;
            Deployment = deployment;
            Driver = deployment.ForProperty(TestProperty.SampleId);
        }

        /// <summary>
        /// Create and initialize an isolated database.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Test database.</returns>
        public static async Task<TestDatabase> CreateAsync(CancellationToken token)
        {
            await EnsureServerAsync(token).ConfigureAwait(false);
            DatabaseSettings settings = TestEnvironment.CreateDatabaseSettings();
            await _SchemaDdl.WaitAsync(token).ConfigureAwait(false);
            try
            {
                DatabaseDriverBase driver = await DatabaseDriverFactory.CreateAndInitializeAsync(settings, TestProperty.SampleId, token).ConfigureAwait(false);
                return new TestDatabase(settings, driver);
            }
            finally { _SchemaDdl.Release(); }
        }

        /// <summary>
        /// Create an isolated database with migrations applied only up to a version, to test upgrades. Pre-existing
        /// rows are owned by the sample property when property scoping is applied later.
        /// </summary>
        /// <param name="version">Highest migration version to apply.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Test database.</returns>
        public static async Task<TestDatabase> CreateAtVersionAsync(int version, CancellationToken token)
        {
            await EnsureServerAsync(token).ConfigureAwait(false);
            DatabaseSettings settings = TestEnvironment.CreateDatabaseSettings();
            await _SchemaDdl.WaitAsync(token).ConfigureAwait(false);
            DatabaseDriverBase? driver = null;
            try
            {
                driver = DatabaseDriverFactory.Create(settings);
                driver.MigrationPropertyId = TestProperty.SampleId;
                await driver.InitializeAsync(version, token).ConfigureAwait(false);
                return new TestDatabase(settings, driver);
            }
            catch
            {
                if (driver != null) await driver.DisposeAsync().ConfigureAwait(false);
                throw;
            }
            finally { _SchemaDdl.Release(); }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Drop the schema.
        /// </summary>
        /// <returns>Task.</returns>
        public async ValueTask DisposeAsync()
        {
            if (!String.IsNullOrEmpty(Settings.Schema))
            {
                await _SchemaDdl.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                try
                {
                    await Deployment.ExecuteQueryAsync("DROP SCHEMA IF EXISTS " + Settings.Schema + " CASCADE;", false, CancellationToken.None).ConfigureAwait(false);
                    await Deployment.ExecuteQueryAsync("DROP SCHEMA IF EXISTS " + Settings.Schema + "_search CASCADE;", false, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Best-effort cleanup; CI containers are discarded anyway.
                }
                finally { _SchemaDdl.Release(); }
            }

            await Deployment.DisposeAsync().ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static async Task EnsureServerAsync(CancellationToken token)
        {
            if (_ServerReady) return;
            await _ServerLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_ServerReady) return;
                DatabaseSettings settings = TestEnvironment.CreateDatabaseSettings();
                settings.Schema = null;
                DatabaseDriverBase driver = DatabaseDriverFactory.Create(settings);
                await using (driver.ConfigureAwait(false))
                {
                    try
                    {
                        // pgvector lives in public so dropping a test schema never drops the extension.
                        await driver.ExecuteQueryAsync("CREATE EXTENSION IF NOT EXISTS vector SCHEMA public;", false, token).ConfigureAwait(false);
                        // pg_trgm backs the library search trigram indexes (migration 135); the migration skips them without it.
                        await driver.ExecuteQueryAsync("CREATE EXTENSION IF NOT EXISTS pg_trgm SCHEMA public;", false, token).ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        throw new InvalidOperationException("The backend tests need PostgreSQL 17 or later with pgvector at " + settings.Hostname + ":" + settings.Port +
                            " (database " + settings.DatabaseName + "; set TK_TEST_PG_HOST, TK_TEST_PG_PORT, TK_TEST_PG_DATABASE, TK_TEST_PG_USERNAME, TK_TEST_PG_PASSWORD). " + e.Message, e);
                    }
                }
                _ServerReady = true;
            }
            finally
            {
                _ServerLock.Release();
            }
        }

        #endregion
    }
}
