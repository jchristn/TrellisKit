namespace TrellisKit.Core.Database
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;

    /// <summary>
    /// Abstract base class for database drivers. Exposes domain-specific method interfaces and runs versioned,
    /// tracked migrations. Providers supply connection handling and provider-specific SQL.
    /// The driver created by <see cref="DatabaseDriverFactory"/> is the deployment driver: it owns the connections and
    /// exposes the deployment-level methods (<see cref="Registry"/>, <see cref="PlatformAdmins"/>, unprefixed
    /// <see cref="JobLeases"/>). Property-owned data is reached only through <see cref="ForProperty"/>, which returns a
    /// driver whose every method set is bound to that property (docs/MULTIPROPERTY_CONTRACT.md section 4.3) and whose
    /// statements pass <see cref="PropertyScopeGuard"/>. On the deployment driver the property-owned method sets are
    /// unbound and throw <see cref="PropertyScopeException"/>.
    /// Thread safety: drivers are safe to share across requests.
    /// </summary>
    public abstract class DatabaseDriverBase : IDisposable, IAsyncDisposable
    {
        #region Public-Members

        /// <summary>
        /// Database provider.
        /// </summary>
        public abstract DatabaseTypeEnum DatabaseType { get; }

        /// <summary>
        /// Request history methods.
        /// </summary>
        public IRequestHistoryMethods RequestHistory { get; protected internal set; } = null!;

        /// <summary>
        /// Member account methods.
        /// </summary>
        public IUserMethods Users { get; protected internal set; } = null!;

        /// <summary>
        /// Password credential methods.
        /// </summary>
        public ICredentialMethods Credentials { get; protected internal set; } = null!;

        /// <summary>
        /// Linked external sign-in methods.
        /// </summary>
        public IExternalIdentityMethods ExternalIdentities { get; protected internal set; } = null!;

        /// <summary>
        /// Sign-in session methods.
        /// </summary>
        public IAuthSessionMethods Sessions { get; protected internal set; } = null!;

        /// <summary>
        /// Refresh token methods.
        /// </summary>
        public IRefreshTokenMethods RefreshTokens { get; protected internal set; } = null!;

        /// <summary>
        /// Email verification and password reset token methods.
        /// </summary>
        public IOneTimeTokenMethods OneTimeTokens { get; protected internal set; } = null!;

        /// <summary>
        /// Role assignment methods.
        /// </summary>
        public IRoleAssignmentMethods RoleAssignments { get; protected internal set; } = null!;

        /// <summary>
        /// Rank history entry methods.
        /// </summary>
        public IRankHistoryMethods RankHistory { get; protected internal set; } = null!;

        /// <summary>
        /// Public landing page methods (profile pages, slug checks, sitemap queries).
        /// </summary>
        public IPublicPageMethods PublicPages { get; protected internal set; } = null!;

        /// <summary>
        /// Member goal methods.
        /// </summary>
        public IUserGoalMethods Goals { get; protected internal set; } = null!;

        /// <summary>
        /// Profile field definition methods.
        /// </summary>
        public IProfileFieldMethods ProfileFields { get; protected internal set; } = null!;

        /// <summary>
        /// Profile value methods.
        /// </summary>
        public IProfileValueMethods ProfileValues { get; protected internal set; } = null!;

        /// <summary>
        /// Per-property setting methods (integration settings and encrypted secrets).
        /// </summary>
        public IPropertySettingMethods PropertySettings { get; protected internal set; } = null!;

        /// <summary>
        /// Access model configuration methods (docs/ACCESS_MODELS_CONTRACT.md section 1).
        /// </summary>
        public IPropertyAccessMethods PropertyAccess { get; protected internal set; } = null!;

        /// <summary>
        /// Secrets key rotation methods (encrypted property settings and email payloads).
        /// </summary>
        public ISecretRotationMethods SecretRotation { get; protected internal set; } = null!;

        /// <summary>
        /// Administrative email methods.
        /// </summary>
        public IEmailMethods Email { get; protected internal set; } = null!;

        /// <summary>
        /// Store subscription methods.
        /// </summary>
        public ISubscriptionMethods Subscriptions { get; protected internal set; } = null!;

        /// <summary>
        /// Subscription event methods.
        /// </summary>
        public ISubscriptionEventMethods SubscriptionEvents { get; protected internal set; } = null!;

        /// <summary>
        /// Membership entitlement methods.
        /// </summary>
        public IEntitlementMethods Entitlements { get; protected internal set; } = null!;

        /// <summary>
        /// Sign-up code methods.
        /// </summary>
        public ISignupCodeMethods SignupCodes { get; protected internal set; } = null!;

        /// <summary>
        /// Sign-up code redemption methods.
        /// </summary>
        public ISignupCodeRedemptionMethods SignupCodeRedemptions { get; protected internal set; } = null!;

        /// <summary>
        /// Audit record methods.
        /// </summary>
        public IAuditMethods Audit { get; protected internal set; } = null!;

        /// <summary>
        /// Label and tag methods shared by every object type.
        /// </summary>
        public IMetadataMethods Metadata { get; protected internal set; } = null!;

        /// <summary>
        /// Asynchronous account export job methods.
        /// </summary>
        public IAccountExportMethods AccountExports { get; protected internal set; } = null!;

        /// <summary>
        /// Media attachment and variant methods.
        /// </summary>
        public IMediaMethods Media { get; protected internal set; } = null!;

        /// <summary>
        /// Member block methods.
        /// </summary>
        public IBlockMethods Blocks { get; protected internal set; } = null!;

        /// <summary>
        /// Word filter term and hit methods.
        /// </summary>
        public IWordFilterMethods WordFilter { get; protected internal set; } = null!;

        /// <summary>
        /// Link preview cache methods.
        /// </summary>
        public ILinkPreviewMethods LinkPreviews { get; protected internal set; } = null!;

        /// <summary>
        /// Member search (display name words and prefix queries) methods.
        /// </summary>
        public IMemberSearchMethods MemberSearch { get; protected internal set; } = null!;

        /// <summary>
        /// Notification center methods.
        /// </summary>
        public INotificationMethods Notifications { get; protected internal set; } = null!;

        /// <summary>
        /// Notification preference and notification hours methods.
        /// </summary>
        public INotificationPreferenceMethods NotificationPreferences { get; protected internal set; } = null!;

        /// <summary>
        /// Push token and push delivery methods.
        /// </summary>
        public IPushTokenMethods PushTokens { get; protected internal set; } = null!;

        /// <summary>
        /// Notification fan-out job methods.
        /// </summary>
        public INotificationFanoutMethods NotificationFanouts { get; protected internal set; } = null!;

        /// <summary>
        /// Category email send (idempotency) methods.
        /// </summary>
        public IEmailSendMethods EmailSends { get; protected internal set; } = null!;

        /// <summary>
        /// Feed post and feed post revision methods.
        /// </summary>
        public IFeedPostMethods FeedPosts { get; protected internal set; } = null!;

        /// <summary>
        /// Comment and comment revision methods.
        /// </summary>
        public ICommentMethods Comments { get; protected internal set; } = null!;

        /// <summary>
        /// Reaction and reaction count methods.
        /// </summary>
        public IReactionMethods Reactions { get; protected internal set; } = null!;

        /// <summary>
        /// Mention methods.
        /// </summary>
        public IMentionMethods Mentions { get; protected internal set; } = null!;

        /// <summary>
        /// Report methods.
        /// </summary>
        public IReportMethods Reports { get; protected internal set; } = null!;

        /// <summary>
        /// Moderation action methods.
        /// </summary>
        public IModerationActionMethods ModerationActions { get; protected internal set; } = null!;

        /// <summary>
        /// Per-user posting permission methods.
        /// </summary>
        public IUserPermissionMethods UserPermissions { get; protected internal set; } = null!;

        /// <summary>
        /// Forum, forum follow, forum read state, forum mute, and forum moderator methods (Phase 3).
        /// </summary>
        public IForumMethods Forums { get; protected internal set; } = null!;

        /// <summary>
        /// Forum thread, revision, thread follow, read state, and trending methods (Phase 3).
        /// </summary>
        public IThreadMethods Threads { get; protected internal set; } = null!;

        /// <summary>
        /// Poll, option, and vote methods (Phase 3).
        /// </summary>
        public IPollMethods Polls { get; protected internal set; } = null!;

        /// <summary>
        /// Library data access (Phase 4): series, lessons, progress, downloads, and document text.
        /// </summary>
        public ILibraryMethods Library { get; protected internal set; } = null!;

        /// <summary>
        /// Events data access (Phase 4): events, occurrences, RSVPs, reminders, categories, and calendar feeds.
        /// </summary>
        public IEventMethods Events { get; protected internal set; } = null!;

        /// <summary>Profile gallery storage, bound to the current property.</summary>
        public IProfileMediaMethods ProfileMedia { get; protected internal set; } = null!;

        /// <summary>Verified ranks, promotions, requirements, and lineage, bound to the property.</summary>
        public IRankMethods Ranks { get; protected internal set; } = null!;

        /// <summary>Curricula, items, and member progress, bound to the property.</summary>
        public ICurriculumMethods Curricula { get; protected internal set; } = null!;

        /// <summary>Attendance check-ins, bound to the property.</summary>
        public IAttendanceMethods Attendance { get; protected internal set; } = null!;

        /// <summary>Saves and private collections, bound to the current property.</summary>
        public IFavoriteMethods Favorites { get; protected internal set; } = null!;

        /// <summary>Directory listing storage, bound to the property.</summary>
        public IDirectoryMethods DirectoryListings { get; protected internal set; } = null!;

        /// <summary>
        /// Ideas board methods (Phase 5).
        /// </summary>
        public IIdeaMethods Ideas { get; protected internal set; } = null!;

        /// <summary>
        /// Support ticket, ticket message, and ticket type methods (Phase 5).
        /// </summary>
        public ISupportMethods Support { get; protected internal set; } = null!;

        /// <summary>
        /// Broadcast, segment, audience, and delivery methods (Phase 5).
        /// </summary>
        public IBroadcastMethods Broadcasts { get; protected internal set; } = null!;

        /// <summary>
        /// Direct conversation, member state, messaging settings, contact, push and email state, and lapse methods (Phase 5).
        /// </summary>
        public IConversationMethods Conversations { get; protected internal set; } = null!;

        /// <summary>
        /// Direct message, history, search, report snapshot, retention, and export methods (Phase 5).
        /// </summary>
        public IMessageMethods Messages { get; protected internal set; } = null!;

        /// <summary>
        /// Search index queue methods (Phase 3).
        /// </summary>
        public ISearchQueueMethods SearchQueue { get; protected internal set; } = null!;

        /// <summary>
        /// AI models and assistants methods (docs/AI_CONTRACT.md).
        /// </summary>
        public IAiMethods Ai { get; protected internal set; } = null!;

        /// <summary>
        /// Usage metering methods (docs/USAGE_METERING_CONTRACT.md).
        /// </summary>
        public IUsageMethods Usage { get; protected internal set; } = null!;

        /// <summary>
        /// Brand palette methods (docs/TRELLISKIT.md section 2.1).
        /// </summary>
        public IBrandPaletteMethods BrandPalettes { get; protected internal set; } = null!;

        /// <summary>
        /// Property document methods: versions and acceptances (docs/TRELLISKIT.md section 14).
        /// </summary>
        public IPropertyDocumentMethods Documents { get; protected internal set; } = null!;

        /// <summary>
        /// Property AI switch methods (docs/TRELLISKIT.md section 9.5).
        /// </summary>
        public IPropertyAiMethods PropertyAi { get; protected internal set; } = null!;

        /// <summary>
        /// Singleton job lease methods.
        /// </summary>
        public IJobLeaseMethods JobLeases { get; protected internal set; } = null!;

        /// <summary>
        /// Job engine methods: definitions, runs, and items (docs/INTEGRATION.md).
        /// </summary>
        public IJobMethods Jobs { get; protected internal set; } = null!;

        /// <summary>
        /// Content changelog methods: rows written by the content triggers and their Terms of Service review
        /// (docs/INTEGRATION.md content review contract).
        /// </summary>
        public IContentChangeMethods ContentChanges { get; protected internal set; } = null!;


        /// <summary>
        /// Live update (code update) methods.
        /// </summary>
        public ILiveUpdateMethods LiveUpdates { get; protected internal set; } = null!;

        /// <summary>
        /// Feature flag, kill switch, and app content version methods.
        /// </summary>
        public IAppConfigMethods AppConfig { get; protected internal set; } = null!;

        /// <summary>
        /// Remote strings, banners, remote assets, and the string catalog (docs/LIVE_UPDATES_CONTRACT.md section 14).
        /// </summary>
        public IAppContentMethods AppContent { get; protected internal set; } = null!;

        /// <summary>
        /// Realtime connection registry methods (docs/PHASE5_CONTRACT.md section 4.11).
        /// </summary>
        public IRealtimeConnectionMethods RealtimeConnections { get; protected internal set; } = null!;

        /// <summary>
        /// Property registry (deployment-level). On a property-bound driver this is the deployment driver's registry.
        /// </summary>
        public IPropertyRegistryMethods Registry { get; protected internal set; } = null!;

        /// <summary>
        /// Platform administrators and their sessions (deployment-level). On a property-bound driver this is the
        /// deployment driver's.
        /// </summary>
        public IPlatformAdminMethods PlatformAdmins { get; protected internal set; } = null!;

        /// <summary>
        /// Property id this driver is bound to, or null for the deployment driver.
        /// </summary>
        public string? BoundPropertyId { get; protected internal set; } = null;

        /// <summary>
        /// The deployment driver (this instance when it is the deployment driver).
        /// </summary>
        public DatabaseDriverBase Deployment
        {
            get { return _Deployment ?? this; }
        }

        /// <summary>
        /// Property id substituted for the {{propertyId}} placeholder when migrations run (the property that owns
        /// pre-existing rows when an older database gains property scoping). Default null, which substitutes the empty
        /// string (a fresh database has no rows to backfill).
        /// </summary>
        public string? MigrationPropertyId { get; set; } = null;

        /// <summary>
        /// Highest migration version this build knows about.
        /// </summary>
        public int LatestSchemaVersion
        {
            get { return Migrations.Count == 0 ? 0 : Migrations.Max(m => m.Version); }
        }

        #endregion

        #region Private-Members

        private bool _Disposed = false;
        private DatabaseDriverBase? _Deployment = null;
        private readonly ConcurrentDictionary<string, DatabaseDriverBase> _Bound = new ConcurrentDictionary<string, DatabaseDriverBase>(StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Initialize the driver: prepare the store and apply pending migrations.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public virtual async Task InitializeAsync(CancellationToken token = default)
        {
            await PrepareAsync(token).ConfigureAwait(false);
            await ApplyMigrationsAsync(Int32.MaxValue, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Initialize the driver, applying pending migrations only up to a version. Used to build a database at an
        /// earlier schema version (for example to test upgrades); call <see cref="InitializeAsync(CancellationToken)"/>
        /// afterwards to apply the rest.
        /// </summary>
        /// <param name="maxVersion">Highest migration version to apply. Minimum 0.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public virtual async Task InitializeAsync(int maxVersion, CancellationToken token = default)
        {
            await PrepareAsync(token).ConfigureAwait(false);
            await ApplyMigrationsAsync(Math.Max(0, maxVersion), token).ConfigureAwait(false);
        }

        /// <summary>
        /// Execute a single query.
        /// </summary>
        /// <param name="query">Query text.</param>
        /// <param name="isTransaction">Whether to run inside a transaction.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result table (empty for statements without results).</returns>
        /// <exception cref="ArgumentNullException">The query is null or empty.</exception>
        public abstract Task<DataTable> ExecuteQueryAsync(string query, bool isTransaction = false, CancellationToken token = default);

        /// <summary>
        /// Execute multiple queries in order, optionally in one transaction. Returns the last statement's result.
        /// </summary>
        /// <param name="queries">Queries.</param>
        /// <param name="isTransaction">Whether to run inside one transaction.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result table of the last statement.</returns>
        /// <exception cref="ArgumentNullException">The query list is null.</exception>
        public abstract Task<DataTable> ExecuteQueriesAsync(IEnumerable<string> queries, bool isTransaction = false, CancellationToken token = default);

        /// <summary>
        /// Verify the database is reachable.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a trivial query succeeds.</returns>
        public virtual async Task<bool> PingAsync(CancellationToken token = default)
        {
            try
            {
                await ExecuteQueryAsync("SELECT 1;", false, token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Read the highest applied migration version.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Version, or 0 when none are applied.</returns>
        public async Task<int> GetAppliedSchemaVersionAsync(CancellationToken token = default)
        {
            IReadOnlyCollection<int> applied = await GetAppliedVersionsAsync(token).ConfigureAwait(false);
            return applied.Count == 0 ? 0 : applied.Max();
        }

        /// <summary>
        /// A driver whose every property-owned method set is bound to one property. Cached per property; the
        /// returned driver shares this driver's connections and needs no disposal.
        /// </summary>
        /// <param name="propertyId">Property id, or the reserved platform or unresolved pseudo-ids (platform audit
        /// records, platform labels and tags, and request history only).</param>
        /// <returns>Property-bound driver.</returns>
        /// <exception cref="PropertyScopeException">The id is malformed or another reserved id.</exception>
        public DatabaseDriverBase ForProperty(string propertyId)
        {
            if (_Deployment != null) return _Deployment.ForProperty(propertyId);
            if (!PropertyIds.IsValid(propertyId) && propertyId != PropertyIds.Platform && propertyId != PropertyIds.Unresolved)
                throw new PropertyScopeException(PropertyIds.IsReserved(propertyId) ? "reserved_id" : "invalid_id", "Invalid property id for a data binding: " + propertyId + ".");
            return _Bound.GetOrAdd(propertyId, id =>
            {
                PropertyBoundDriver bound = new PropertyBoundDriver(this, id);
                bound._Deployment = this;
                PopulateMethods(bound, id);
                bound.Registry = Registry;
                bound.PlatformAdmins = PlatformAdmins;
                return bound;
            });
        }

        /// <summary>
        /// Close the driver and release pooled resources.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public abstract Task CloseAsync(CancellationToken token = default);

        /// <summary>
        /// Dispose the driver.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Dispose the driver asynchronously.
        /// </summary>
        /// <returns>Task.</returns>
        public async ValueTask DisposeAsync()
        {
            await CloseAsync().ConfigureAwait(false);
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Members

        /// <summary>
        /// Ordered migrations for this provider.
        /// </summary>
        protected abstract IReadOnlyList<SchemaMigration> Migrations { get; }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Create the provider's method sets on a driver, bound to a property id (null for the deployment driver's
        /// unbound sets). Deployment-level sets (registry, platform administrators) are created only when the id is null.
        /// </summary>
        /// <param name="target">Driver to populate (this driver, or a property-bound driver sharing its connections).</param>
        /// <param name="propertyId">Bound property id, or null.</param>
        internal abstract void PopulateMethods(DatabaseDriverBase target, string? propertyId);

        /// <summary>
        /// Provider-specific preparation before migrations (for example creating directories or setting pragmas).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        protected virtual Task PrepareAsync(CancellationToken token)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// SQL that creates the schema_migrations table if it does not exist.
        /// </summary>
        /// <returns>SQL.</returns>
        protected abstract string CreateMigrationsTableSql();

        /// <summary>
        /// SQL that records an applied migration.
        /// </summary>
        /// <param name="migration">Migration.</param>
        /// <param name="appliedUtc">Applied time (UTC).</param>
        /// <returns>SQL.</returns>
        protected abstract string RecordMigrationSql(SchemaMigration migration, DateTime appliedUtc);

        /// <summary>
        /// Release managed and unmanaged resources.
        /// </summary>
        /// <param name="disposing">True when called from Dispose.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private async Task ApplyMigrationsAsync(int maxVersion, CancellationToken token)
        {
            await ExecuteQueryAsync(CreateMigrationsTableSql(), false, token).ConfigureAwait(false);
            IReadOnlyCollection<int> applied = await GetAppliedVersionsAsync(token).ConfigureAwait(false);

            foreach (SchemaMigration migration in Migrations.OrderBy(m => m.Version))
            {
                token.ThrowIfCancellationRequested();
                if (applied.Contains(migration.Version)) continue;
                if (migration.Version > maxVersion) break;

                string placeholder = "'" + (MigrationPropertyId ?? String.Empty).Replace("'", "''") + "'";
                List<string> statements = migration.Statements.Select(st => st.Replace(SchemaMigration.PropertyIdPlaceholder, placeholder, StringComparison.Ordinal)).ToList();
                statements.Add(RecordMigrationSql(migration, DateTime.UtcNow));
                await ExecuteQueriesAsync(statements, true, token).ConfigureAwait(false);
            }
        }

        private async Task<IReadOnlyCollection<int>> GetAppliedVersionsAsync(CancellationToken token)
        {
            await ExecuteQueryAsync(CreateMigrationsTableSql(), false, token).ConfigureAwait(false);
            DataTable table = await ExecuteQueryAsync("SELECT version FROM schema_migrations;", false, token).ConfigureAwait(false);
            HashSet<int> versions = new HashSet<int>();
            foreach (DataRow row in table.Rows)
            {
                versions.Add(RowReader.GetInt(row, "version"));
            }
            return versions;
        }

        #endregion
    }
}
