namespace TrellisKit.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;

    /// <summary>
    /// A driver bound to one property: every method set is bound to the property id and every statement passes
    /// <see cref="PropertyScopeGuard"/> before it runs on the deployment driver's connections. Created only by
    /// <see cref="DatabaseDriverBase.ForProperty"/>; closing or disposing it does nothing (the deployment driver owns
    /// the connections).
    /// </summary>
    internal sealed class PropertyBoundDriver : DatabaseDriverBase
    {
        public override DatabaseTypeEnum DatabaseType
        {
            get { return _Inner.DatabaseType; }
        }

        protected override IReadOnlyList<SchemaMigration> Migrations
        {
            get { return Array.Empty<SchemaMigration>(); }
        }

        private readonly DatabaseDriverBase _Inner;

        internal PropertyBoundDriver(DatabaseDriverBase inner, string propertyId)
        {
            _Inner = inner ?? throw new ArgumentNullException(nameof(inner));
            BoundPropertyId = propertyId;
        }

        public override Task InitializeAsync(CancellationToken token = default)
        {
            return Task.CompletedTask;
        }

        public override Task InitializeAsync(int maxVersion, CancellationToken token = default)
        {
            return Task.CompletedTask;
        }

        public override Task<DataTable> ExecuteQueryAsync(string query, bool isTransaction = false, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(query)) throw new ArgumentNullException(nameof(query));
            PropertyScopeGuard.Verify(query);
            return _Inner.ExecuteQueryAsync(query, isTransaction, token);
        }

        public override Task<DataTable> ExecuteQueriesAsync(IEnumerable<string> queries, bool isTransaction = false, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(queries);
            List<string> list = queries.ToList();
            foreach (string query in list) PropertyScopeGuard.Verify(query);
            return _Inner.ExecuteQueriesAsync(list, isTransaction, token);
        }

        public override Task CloseAsync(CancellationToken token = default)
        {
            return Task.CompletedTask;
        }

        internal override void PopulateMethods(DatabaseDriverBase target, string? propertyId)
        {
            _Inner.PopulateMethods(target, propertyId);
        }

        protected override string CreateMigrationsTableSql()
        {
            throw new NotSupportedException("A property-bound driver does not run migrations.");
        }

        protected override string RecordMigrationSql(SchemaMigration migration, DateTime appliedUtc)
        {
            throw new NotSupportedException("A property-bound driver does not run migrations.");
        }
    }
}
