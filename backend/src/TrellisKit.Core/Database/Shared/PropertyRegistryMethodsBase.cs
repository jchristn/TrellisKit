namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Shared property registry methods. Bound to the platform realm so the labels and tags of properties are stored
    /// and filtered under it; the registry tables themselves are deployment-level.
    /// </summary>
    internal abstract class PropertyRegistryMethodsBase : MethodsBase, IPropertyRegistryMethods
    {
        private const string _Columns = "id, slug, name, status, documentjson, sourcedirectory, directoryhash, enabledutc, disabledutc, disabledreason, deletescheduledutc, purgestartedutc, purgedutc, createdutc, updatedutc";
        private const string _ReadColumns = _Columns + ", profilefieldsseededutc, profilefieldshash";
        private const string _Duplicate = "A property with that id or slug, or a domain or app id registered to another property,";
        private const int _LiveCap = 500;

        protected override bool ScopesCollectionTable
        {
            get { return false; }
        }

        protected PropertyRegistryMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task CreateAsync(PropertyRecord record, List<PropertyDomain> domains, List<PropertyApp> apps, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(record);
            ArgumentNullException.ThrowIfNull(domains);
            ArgumentNullException.ThrowIfNull(apps);
            List<string> statements = new List<string>
            {
                "INSERT INTO properties (" + _Columns + ") VALUES (" + D.Str(record.Id) + ", " + D.Str(record.Slug) + ", " + D.Str(record.Name) + ", " +
                D.Enum(record.Status) + ", " + D.Str(record.DocumentJson) + ", " + D.Str(record.SourceDirectory) + ", " + D.Str(record.DirectoryHash) + ", " +
                D.Ts(record.EnabledUtc) + ", " + D.Ts(record.DisabledUtc) + ", " + D.Str(record.DisabledReason) + ", " + D.Ts(record.DeleteScheduledUtc) + ", " +
                D.Ts(record.PurgeStartedUtc) + ", " + D.Ts(record.PurgedUtc) + ", " + D.Ts(record.CreatedUtc) + ", " + D.Ts(record.UpdatedUtc) + ");"
            };
            statements.AddRange(domains.Select(d => InsertDomain(record.Id, d)));
            statements.AddRange(apps.Select(a => InsertApp(record.Id, a)));
            await UniqueTransactionAsync(statements, _Duplicate, token).ConfigureAwait(false);
        }

        public async Task<PropertyRecord?> ReadAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("SELECT " + _ReadColumns + " FROM properties WHERE id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<List<PropertyRecord>> ReadLiveAsync(CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT " + _ReadColumns + " FROM properties WHERE status <> " + D.Enum(PropertyStatusEnum.Deleted) + " ORDER BY id LIMIT " + D.Num(_LiveCap) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public Task<EnumerationResult<PropertyRecord>> EnumerateAsync(EnumerationRequest request, PropertyStatusEnum? status, string? host, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = new List<string>();
            conditions.Add(status == null ? "p.status <> " + D.Enum(PropertyStatusEnum.Deleted) : "p.status = " + D.Enum(status.Value));
            if (!String.IsNullOrWhiteSpace(host))
                conditions.Add("EXISTS (SELECT 1 FROM propertydomains pd WHERE pd.propertyid = p.id AND lower(pd.host) = lower(" + D.Str(host.Trim()) + "))");
            string select = String.Join(", ", _ReadColumns.Split(", ").Select(c => "p." + c));
            return EnumerateAsync(request, select, "properties p", conditions, Map, token);
        }

        public Task<long> CountLiveAsync(CancellationToken token = default)
        {
            return CountAsync("SELECT COUNT(*) AS cnt FROM properties WHERE status <> " + D.Enum(PropertyStatusEnum.Deleted) + ";", token);
        }

        public async Task UpdateAsync(PropertyRecord record, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(record);
            RequireText(record.Id, nameof(record.Id));
            await UniqueAsync("UPDATE properties SET slug = " + D.Str(record.Slug) + ", name = " + D.Str(record.Name) + ", status = " + D.Enum(record.Status) +
                ", documentjson = " + D.Str(record.DocumentJson) + ", sourcedirectory = " + D.Str(record.SourceDirectory) + ", directoryhash = " + D.Str(record.DirectoryHash) +
                ", enabledutc = " + D.Ts(record.EnabledUtc) + ", disabledutc = " + D.Ts(record.DisabledUtc) + ", disabledreason = " + D.Str(record.DisabledReason) +
                ", deletescheduledutc = " + D.Ts(record.DeleteScheduledUtc) + ", purgestartedutc = " + D.Ts(record.PurgeStartedUtc) + ", purgedutc = " + D.Ts(record.PurgedUtc) +
                ", updatedutc = " + D.Ts(record.UpdatedUtc) + " WHERE id = " + D.Str(record.Id) + ";", _Duplicate, token).ConfigureAwait(false);
        }

        public async Task<List<PropertyDomain>> ReadDomainsAsync(string propertyId, CancellationToken token = default)
        {
            RequireText(propertyId, nameof(propertyId));
            DataTable table = await QueryAsync("SELECT id, propertyid, host, kind, verifiedutc, source, verificationtoken, verificationcheckedutc, verificationerror, createdutc FROM propertydomains WHERE propertyid = " + D.Str(propertyId) + " ORDER BY host LIMIT 20;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapDomain).ToList();
        }

        public async Task<List<PropertyApp>> ReadAppsAsync(string propertyId, CancellationToken token = default)
        {
            RequireText(propertyId, nameof(propertyId));
            DataTable table = await QueryAsync("SELECT id, propertyid, platform, appid, storeappid, enabled, createdutc, updatedutc FROM propertyapps WHERE propertyid = " + D.Str(propertyId) + " ORDER BY platform, appid LIMIT 10;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapApp).ToList();
        }

        public async Task ReplaceDomainsAndAppsAsync(string propertyId, List<PropertyDomain>? domains, List<PropertyApp>? apps, DateTime updatedUtc, CancellationToken token = default)
        {
            RequireText(propertyId, nameof(propertyId));
            List<string> statements = new List<string>();
            if (domains != null)
            {
                statements.Add("DELETE FROM propertydomains WHERE propertyid = " + D.Str(propertyId) + ";");
                statements.AddRange(domains.Select(d => InsertDomain(propertyId, d)));
            }
            if (apps != null)
            {
                statements.Add("DELETE FROM propertyapps WHERE propertyid = " + D.Str(propertyId) + ";");
                statements.AddRange(apps.Select(a => InsertApp(propertyId, a)));
            }
            statements.Add("UPDATE properties SET updatedutc = " + D.Ts(updatedUtc) + " WHERE id = " + D.Str(propertyId) + ";");
            await UniqueTransactionAsync(statements, _Duplicate, token).ConfigureAwait(false);
        }

        public async Task<DateTime?> ReadLatestChangeAsync(CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT MAX(updatedutc) AS latest FROM properties;", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : RowReader.GetNullableDateTime(table.Rows[0], "latest");
        }

        public async Task<string?> FindMediaJobPropertyAsync(string jobId, CancellationToken token = default)
        {
            RequireText(jobId, nameof(jobId));
            DataTable table = await QueryAsync("SELECT propertyid FROM mediaattachments WHERE processorjobid = " + D.Str(jobId) + " LIMIT 1;", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : RowReader.GetNullableString(table.Rows[0], "propertyid");
        }

        public async Task<List<PropertyRecord>> ReadPurgeDueAsync(DateTime nowUtc, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT " + _ReadColumns + " FROM properties WHERE status = " + D.Enum(PropertyStatusEnum.Deleting) +
                " AND deletescheduledutc <= " + D.Ts(nowUtc) + " ORDER BY deletescheduledutc LIMIT 25;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<int> PurgeBatchAsync(string table, string propertyId, int batchSize, CancellationToken token = default)
        {
            RequireText(propertyId, nameof(propertyId));
            if (!PropertyTables.IsOwned(table)) throw new ArgumentException("Not a property-owned table: " + table + ".", nameof(table));
            int size = Math.Clamp(batchSize, 1, 10000);
            string sql = "DELETE FROM " + table + " WHERE propertyid = " + D.Str(propertyId) + " AND ctid IN (SELECT ctid FROM " + table +
                " WHERE propertyid = " + D.Str(propertyId) + " LIMIT " + D.Num(size) + ") RETURNING 1 AS n;";
            DataTable result = await QueryAsync(sql, token).ConfigureAwait(false);
            return result.Rows.Count;
        }

        public Task<long> CountMembersAsync(string propertyId, CancellationToken token = default)
        {
            RequireText(propertyId, nameof(propertyId));
            return CountAsync("SELECT COUNT(*) AS cnt FROM users WHERE propertyid = " + D.Str(propertyId) + " AND isdeleted = " + D.Bool(false) + ";", token);
        }

        private string InsertDomain(string propertyId, PropertyDomain d)
        {
            return "INSERT INTO propertydomains (id, propertyid, host, kind, verifiedutc, source, verificationtoken, verificationcheckedutc, verificationerror, createdutc) VALUES (" + D.Str(d.Id) + ", " + D.Str(propertyId) + ", " +
                D.Str(d.Host.Trim().ToLowerInvariant()) + ", " + D.Enum(d.Kind) + ", " + D.Ts(d.VerifiedUtc) + ", " + D.Str(String.IsNullOrEmpty(d.Source) ? "Platform" : d.Source) + ", " +
                D.Str(d.VerificationToken) + ", " + D.Ts(d.VerificationCheckedUtc) + ", " + D.Str(d.VerificationError) + ", " + D.Ts(d.CreatedUtc) + ");";
        }

        private string InsertApp(string propertyId, PropertyApp a)
        {
            return "INSERT INTO propertyapps (id, propertyid, platform, appid, storeappid, enabled, createdutc, updatedutc) VALUES (" + D.Str(a.Id) + ", " + D.Str(propertyId) + ", " +
                D.Enum(a.Platform) + ", " + D.Str(a.AppId.Trim()) + ", " + D.Str(a.StoreAppId) + ", " + D.Bool(a.Enabled) + ", " + D.Ts(a.CreatedUtc) + ", " + D.Ts(a.UpdatedUtc) + ");";
        }

        private static PropertyRecord Map(DataRow row)
        {
            return new PropertyRecord
            {
                Id = RowReader.GetString(row, "id"),
                Slug = RowReader.GetString(row, "slug"),
                Name = RowReader.GetString(row, "name"),
                Status = Enum.TryParse(RowReader.GetString(row, "status"), out PropertyStatusEnum status) ? status : PropertyStatusEnum.Disabled,
                DocumentJson = RowReader.GetString(row, "documentjson"),
                SourceDirectory = RowReader.GetNullableString(row, "sourcedirectory"),
                DirectoryHash = RowReader.GetNullableString(row, "directoryhash"),
                EnabledUtc = RowReader.GetNullableDateTime(row, "enabledutc"),
                DisabledUtc = RowReader.GetNullableDateTime(row, "disabledutc"),
                DisabledReason = RowReader.GetNullableString(row, "disabledreason"),
                DeleteScheduledUtc = RowReader.GetNullableDateTime(row, "deletescheduledutc"),
                PurgeStartedUtc = RowReader.GetNullableDateTime(row, "purgestartedutc"),
                PurgedUtc = RowReader.GetNullableDateTime(row, "purgedutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc"),
                ProfileFieldsSeededUtc = RowReader.GetNullableDateTime(row, "profilefieldsseededutc"),
                ProfileFieldsHash = RowReader.GetNullableString(row, "profilefieldshash")
            };
        }

        public async Task MarkProfileFieldsSeededAsync(string propertyId, string? hash, DateTime seededUtc, CancellationToken token = default)
        {
            RequireText(propertyId, nameof(propertyId));
            await QueryAsync("UPDATE properties SET profilefieldsseededutc = COALESCE(profilefieldsseededutc, " + D.Ts(seededUtc) + "), profilefieldshash = " + D.Str(hash) + " WHERE id = " + D.Str(propertyId) + ";", token).ConfigureAwait(false);
        }

        private static PropertyDomain MapDomain(DataRow row)
        {
            return new PropertyDomain
            {
                Id = RowReader.GetString(row, "id"),
                PropertyId = RowReader.GetString(row, "propertyid"),
                Host = RowReader.GetString(row, "host"),
                Kind = Enum.TryParse(RowReader.GetString(row, "kind"), out DomainKindEnum kind) ? kind : DomainKindEnum.Alias,
                VerifiedUtc = RowReader.GetNullableDateTime(row, "verifiedutc"),
                Source = RowReader.GetNullableString(row, "source") ?? "Platform",
                VerificationToken = RowReader.GetNullableString(row, "verificationtoken"),
                VerificationCheckedUtc = RowReader.GetNullableDateTime(row, "verificationcheckedutc"),
                VerificationError = RowReader.GetNullableString(row, "verificationerror"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
        }

        public async Task UpdateDomainVerificationAsync(PropertyDomain domain, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(domain);
            await TransactionAsync(new List<string>
            {
                "UPDATE propertydomains SET verifiedutc = " + D.Ts(domain.VerifiedUtc) + ", verificationcheckedutc = " + D.Ts(domain.VerificationCheckedUtc) + ", verificationerror = " + D.Str(domain.VerificationError) +
                    " WHERE id = " + D.Str(domain.Id) + " AND propertyid = " + D.Str(domain.PropertyId) + ";",
                "UPDATE properties SET updatedutc = " + D.Ts(DateTime.UtcNow) + " WHERE id = " + D.Str(domain.PropertyId) + ";"
            }, token).ConfigureAwait(false);
        }

        private static PropertyApp MapApp(DataRow row)
        {
            return new PropertyApp
            {
                Id = RowReader.GetString(row, "id"),
                PropertyId = RowReader.GetString(row, "propertyid"),
                Platform = Enum.TryParse(RowReader.GetString(row, "platform"), out AppPlatformEnum platform) ? platform : AppPlatformEnum.Ios,
                AppId = RowReader.GetString(row, "appid"),
                StoreAppId = RowReader.GetNullableString(row, "storeappid"),
                Enabled = RowReader.GetBool(row, "enabled"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        public async Task<bool> SlugInUseAsync(string slug, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(slug)) return false;
            return await CountAsync("SELECT COUNT(*) AS cnt FROM properties WHERE lower(slug) = lower(" + D.Str(slug.Trim()) + ");", token).ConfigureAwait(false) > 0;
        }
    }
}
