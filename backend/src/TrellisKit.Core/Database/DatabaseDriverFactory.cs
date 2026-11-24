namespace TrellisKit.Core.Database
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Postgresql;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// Composition root for the data layer.
    /// </summary>
    public static class DatabaseDriverFactory
    {
        #region Public-Methods

        /// <summary>
        /// Create a driver for the configured provider. The driver is not initialized.
        /// </summary>
        /// <param name="settings">Database settings.</param>
        /// <returns>Driver.</returns>
        /// <exception cref="ArgumentNullException">Settings are null.</exception>
        /// <exception cref="ArgumentException">The provider is not supported.</exception>
        public static DatabaseDriverBase Create(DatabaseSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            switch (settings.Type)
            {
                case DatabaseTypeEnum.Postgresql:
                    return new PostgresqlDatabaseDriver(settings);
                default:
                    throw new ArgumentException("Unsupported database type: " + settings.Type.ToString() + ".", nameof(settings));
            }
        }

        /// <summary>
        /// Create a driver and apply migrations.
        /// </summary>
        /// <param name="settings">Database settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Initialized driver.</returns>
        public static Task<DatabaseDriverBase> CreateAndInitializeAsync(DatabaseSettings settings, CancellationToken token = default)
        {
            return CreateAndInitializeAsync(settings, null, token);
        }

        /// <summary>
        /// Create a driver and apply migrations, substituting a property id for the migration placeholder (the
        /// property that owns rows created before property scoping).
        /// </summary>
        /// <param name="settings">Database settings.</param>
        /// <param name="migrationPropertyId">Property id that owns pre-existing rows, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Initialized driver.</returns>
        public static async Task<DatabaseDriverBase> CreateAndInitializeAsync(DatabaseSettings settings, string? migrationPropertyId, CancellationToken token = default)
        {
            DatabaseDriverBase driver = Create(settings);
            driver.MigrationPropertyId = migrationPropertyId;
            try
            {
                await driver.InitializeAsync(token).ConfigureAwait(false);
                return driver;
            }
            catch
            {
                await driver.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        #endregion
    }
}
