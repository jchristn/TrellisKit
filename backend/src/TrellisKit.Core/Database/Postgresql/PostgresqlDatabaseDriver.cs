namespace TrellisKit.Core.Database.Postgresql
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using Npgsql;
    using TrellisKit.Core.Database.Postgresql.Implementations;
    using TrellisKit.Core.Database.Postgresql.Queries;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Observability;

    /// <summary>
    /// PostgreSQL driver. Used in production.
    /// Thread safety: each call opens a pooled connection, so calls run concurrently without serialization.
    /// </summary>
    public class PostgresqlDatabaseDriver : DatabaseDriverBase
    {
        #region Public-Members

        /// <inheritdoc />
        public override DatabaseTypeEnum DatabaseType
        {
            get { return DatabaseTypeEnum.Postgresql; }
        }

        #endregion

        #region Private-Members

        private const string _DbSystem = "postgresql";
        private readonly DatabaseSettings _Settings;
        private readonly NpgsqlDataSource _DataSource;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the driver.
        /// </summary>
        /// <param name="settings">Database settings.</param>
        /// <exception cref="ArgumentNullException">Settings are null.</exception>
        public PostgresqlDatabaseDriver(DatabaseSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            _Settings = settings;

            NpgsqlConnectionStringBuilder builder = new NpgsqlConnectionStringBuilder
            {
                Host = settings.Hostname,
                Port = settings.Port,
                Database = settings.DatabaseName,
                Username = settings.Username,
                Password = settings.Password,
                CommandTimeout = settings.CommandTimeoutSeconds,
                Pooling = true,
                MaxPoolSize = settings.MaxPoolSize
            };
            if (!String.IsNullOrWhiteSpace(settings.Schema)) builder.SearchPath = settings.Schema;

            _DataSource = NpgsqlDataSource.Create(builder.ToString());
            PopulateMethods(this, null);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The connection string of a dedicated, unpooled connection with the same host, credentials, and search path
        /// as the driver (the realtime bus LISTEN connection, docs/PHASE5_CONTRACT.md section 4.11).
        /// </summary>
        /// <param name="settings">Database settings.</param>
        /// <returns>Connection string.</returns>
        /// <exception cref="ArgumentNullException">Settings are null.</exception>
        public static string DedicatedConnectionString(DatabaseSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            NpgsqlConnectionStringBuilder builder = new NpgsqlConnectionStringBuilder
            {
                Host = settings.Hostname,
                Port = settings.Port,
                Database = settings.DatabaseName,
                Username = settings.Username,
                Password = settings.Password,
                CommandTimeout = settings.CommandTimeoutSeconds,
                Pooling = false,
                KeepAlive = 15
            };
            if (!String.IsNullOrWhiteSpace(settings.Schema)) builder.SearchPath = settings.Schema;
            return builder.ToString();
        }

        /// <inheritdoc />
        public override async Task<DataTable> ExecuteQueryAsync(string query, bool isTransaction = false, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(query)) throw new ArgumentNullException(nameof(query));
            return await ExecuteQueriesAsync(new string[] { query }, isTransaction, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public override async Task<DataTable> ExecuteQueriesAsync(IEnumerable<string> queries, bool isTransaction = false, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(queries);
            ObjectDisposedException.ThrowIf(_Disposed, this);

            using (Activity? activity = TrellisKitTelemetry.ActivitySource.StartActivity("db.query", ActivityKind.Client))
            {
                activity?.SetTag("db.system", _DbSystem);
                Stopwatch stopwatch = Stopwatch.StartNew();
                Exception? failure = null;

                try
                {
                    DataTable result = new DataTable();
                    List<string> statements = queries as List<string> ?? new List<string>(queries);
                    ContentChangeContext? attribution = ContentChangeContext.Current;
                    bool attribute = attribution != null && ContentChangeContext.MayWrite(statements);
                    await using (NpgsqlConnection connection = await _DataSource.OpenConnectionAsync(token).ConfigureAwait(false))
                    {
                        NpgsqlTransaction? transaction = isTransaction
                            ? await connection.BeginTransactionAsync(token).ConfigureAwait(false)
                            : null;
                        try
                        {
                            // Content changelog attribution (owner requirement R3): transaction-local inside a transaction;
                            // otherwise session-level and cleared in finally, so a pooled connection never carries it on.
                            if (attribute) await AttributeAsync(connection, transaction, attribution, token).ConfigureAwait(false);
                            foreach (string query in statements)
                            {
                                if (String.IsNullOrWhiteSpace(query)) continue;
                                token.ThrowIfCancellationRequested();
                                await using (NpgsqlCommand command = connection.CreateCommand())
                                {
                                    command.CommandText = query;
                                    command.CommandTimeout = _Settings.CommandTimeoutSeconds;
                                    if (transaction != null) command.Transaction = transaction;
                                    await using (NpgsqlDataReader reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                                    {
                                        result = new DataTable();
                                        result.Load(reader);
                                    }
                                }
                            }

                            if (transaction != null) await transaction.CommitAsync(token).ConfigureAwait(false);
                        }
                        catch
                        {
                            if (transaction != null) await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                            throw;
                        }
                        finally
                        {
                            if (transaction != null) await transaction.DisposeAsync().ConfigureAwait(false);
                            else if (attribute) await ClearAttributionAsync(connection).ConfigureAwait(false);
                        }
                    }
                    return result;
                }
                catch (Exception e)
                {
                    failure = e;
                    throw;
                }
                finally
                {
                    TrellisKitTelemetry.RecordDatabaseQuery(activity, _DbSystem, stopwatch.Elapsed, failure);
                }
            }
        }

        /// <inheritdoc />
        public override async Task CloseAsync(CancellationToken token = default)
        {
            if (_Disposed) return;
            await _DataSource.DisposeAsync().ConfigureAwait(false);
        }


        /// <summary>
        /// Create the PostgreSQL method sets on a driver, bound to a property id (null for this deployment driver).
        /// </summary>
        /// <param name="target">Driver to populate.</param>
        /// <param name="propertyId">Bound property id, or null.</param>
        internal override void PopulateMethods(DatabaseDriverBase target, string? propertyId)
        {
            ArgumentNullException.ThrowIfNull(target);
            target.RequestHistory = new RequestHistoryMethods(target, propertyId);
            target.Users = new UserMethods(target, propertyId);
            target.Credentials = new CredentialMethods(target, propertyId);
            target.ExternalIdentities = new ExternalIdentityMethods(target, propertyId);
            target.Sessions = new AuthSessionMethods(target, propertyId);
            target.RefreshTokens = new RefreshTokenMethods(target, propertyId);
            target.OneTimeTokens = new OneTimeTokenMethods(target, propertyId);
            target.RoleAssignments = new RoleAssignmentMethods(target, propertyId);
            target.RankHistory = new RankHistoryMethods(target, propertyId);
            target.PublicPages = new PublicPageMethods(target, propertyId);
            target.Goals = new UserGoalMethods(target, propertyId);
            target.ProfileFields = new ProfileFieldMethods(target, propertyId);
            target.ProfileValues = new ProfileValueMethods(target, propertyId);
            target.PropertySettings = new PropertySettingMethods(target, propertyId);
            target.PropertyAccess = new PropertyAccessMethods(target, propertyId);
            target.SecretRotation = new SecretRotationMethods(target, propertyId);
            target.Email = new EmailMethods(target, propertyId);
            target.Subscriptions = new SubscriptionMethods(target, propertyId);
            target.SubscriptionEvents = new SubscriptionEventMethods(target, propertyId);
            target.Entitlements = new EntitlementMethods(target, propertyId);
            target.SignupCodes = new SignupCodeMethods(target, propertyId);
            target.SignupCodeRedemptions = new SignupCodeRedemptionMethods(target, propertyId);
            target.Audit = new AuditMethods(target, propertyId);
            target.Metadata = new MetadataMethods(target, propertyId);
            target.AccountExports = new AccountExportMethods(target, propertyId);
            target.Media = new MediaMethods(target, propertyId);
            target.ProfileMedia = new ProfileMediaMethods(target, propertyId);
            target.Ranks = new RankMethods(target, propertyId);
            target.Curricula = new CurriculumMethods(target, propertyId);
            target.Attendance = new AttendanceMethods(target, propertyId);
            target.Blocks = new BlockMethods(target, propertyId);
            target.WordFilter = new WordFilterMethods(target, propertyId);
            target.LinkPreviews = new LinkPreviewMethods(target, propertyId);
            target.MemberSearch = new MemberSearchMethods(target, propertyId);
            target.Notifications = new NotificationMethods(target, propertyId);
            target.NotificationPreferences = new NotificationPreferenceMethods(target, propertyId);
            target.PushTokens = new PushTokenMethods(target, propertyId);
            target.NotificationFanouts = new NotificationFanoutMethods(target, propertyId);
            target.EmailSends = new EmailSendMethods(target, propertyId);
            target.FeedPosts = new FeedPostMethods(target, propertyId);
            target.Comments = new CommentMethods(target, propertyId);
            target.Reactions = new ReactionMethods(target, propertyId);
            target.Mentions = new MentionMethods(target, propertyId);
            target.Reports = new ReportMethods(target, propertyId);
            target.ModerationActions = new ModerationActionMethods(target, propertyId);
            target.UserPermissions = new UserPermissionMethods(target, propertyId);
            target.Forums = new ForumMethods(target, propertyId);
            target.Threads = new ThreadMethods(target, propertyId);
            target.Polls = new PollMethods(target, propertyId);
            target.Library = new LibraryMethods(target, propertyId);
            target.Events = new EventMethods(target, propertyId);
            target.DirectoryListings = new DirectoryMethods(target, propertyId);
            target.Ideas = new IdeaMethods(target, propertyId);
            target.Support = new SupportMethods(target, propertyId);
            target.Broadcasts = new BroadcastMethods(target, propertyId);
            target.Conversations = new ConversationMethods(target, propertyId);
            target.Messages = new MessageMethods(target, propertyId);
            target.SearchQueue = new SearchQueueMethods(target, propertyId);
            target.JobLeases = new JobLeaseMethods(target, propertyId);
            target.Jobs = new JobMethods(target, propertyId);
            target.ContentChanges = new ContentChangeMethods(target, propertyId);
            target.LiveUpdates = new LiveUpdateMethods(target, propertyId);
            target.AppConfig = new AppConfigMethods(target, propertyId);
            target.AppContent = new AppContentMethods(target, propertyId);
            target.Ai = new AiMethods(target, propertyId);
            target.Usage = new UsageMethods(target, propertyId);
            target.BrandPalettes = new BrandPaletteMethods(target, propertyId);
            target.PropertyAi = new PropertyAiMethods(target, propertyId);
            target.Documents = new PropertyDocumentMethods(target, propertyId);
            target.RealtimeConnections = new RealtimeConnectionMethods(target, propertyId);
            if (propertyId != null) return;
            target.Registry = new PropertyRegistryMethods(target, PropertyIds.Platform);
            target.PlatformAdmins = new PlatformAdminMethods(target, PropertyIds.Platform);
        }

        #endregion

        #region Protected-Members

        /// <inheritdoc />
        protected override IReadOnlyList<SchemaMigration> Migrations
        {
            get { return PostgresqlSchema.Migrations; }
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task PrepareAsync(CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(_Settings.Schema)) return;
            string schema = _Settings.Schema.Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(schema, "^[A-Za-z_][A-Za-z0-9_]{0,62}$"))
                throw new ArgumentException("Schema name must be a simple identifier: " + schema + ".");
            await ExecuteQueryAsync("CREATE SCHEMA IF NOT EXISTS " + schema + ";", false, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        protected override string CreateMigrationsTableSql()
        {
            return PostgresqlSchema.CreateMigrationsTable();
        }

        /// <inheritdoc />
        protected override string RecordMigrationSql(SchemaMigration migration, DateTime appliedUtc)
        {
            return PostgresqlSchema.RecordMigration(migration, appliedUtc);
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (_Disposed) return;
            _Disposed = true;
            if (disposing) _DataSource.Dispose();
            base.Dispose(disposing);
        }

        #endregion

        #region Private-Methods

        private async Task AttributeAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, ContentChangeContext? attribution, CancellationToken token)
        {
            if (attribution == null) return;
            await using (NpgsqlCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT set_config('" + ContentChangeContext.ActorSetting + "', @actor, @local), set_config('" + ContentChangeContext.ClientSetting + "', @client, @local);";
                command.CommandTimeout = _Settings.CommandTimeoutSeconds;
                command.Parameters.AddWithValue("actor", attribution.ActorUserId ?? String.Empty);
                command.Parameters.AddWithValue("client", attribution.Client.ToString());
                command.Parameters.AddWithValue("local", transaction != null);
                if (transaction != null) command.Transaction = transaction;
                await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
        }

        private async Task ClearAttributionAsync(NpgsqlConnection connection)
        {
            try
            {
                await using (NpgsqlCommand command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT set_config('" + ContentChangeContext.ActorSetting + "', '', false), set_config('" + ContentChangeContext.ClientSetting + "', '', false);";
                    command.CommandTimeout = _Settings.CommandTimeoutSeconds;
                    await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                // A broken connection is discarded by the pool, and the pool's reset clears session settings anyway.
            }
        }

        #endregion
    }
}
