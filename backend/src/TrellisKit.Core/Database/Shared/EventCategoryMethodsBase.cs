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
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    internal abstract class EventCategoryMethodsBase : MethodsBase, IEventCategoryMethods
    {
        protected EventCategoryMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task CreateAsync(EventCategory c, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(c);
            RequireText(c.Id, nameof(c.Id));
            await UniqueAsync("INSERT INTO eventcategories (propertyid, id, key, label, color, sortorder, isretired, createdutc, updatedutc) VALUES (" + Pid + ", " + D.Str(c.Id) + ", " + D.Str(c.Key) + ", " +
                D.Str(c.Label) + ", " + D.Str(c.Color) + ", " + D.Num(c.SortOrder) + ", " + D.Bool(c.IsRetired) + ", " + D.Ts(c.CreatedUtc) + ", " + D.Ts(c.UpdatedUtc) + ");", "Category", token).ConfigureAwait(false);
        }

        public async Task<EventCategory?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) return null;
            DataTable table = await QueryAsync("SELECT * FROM eventcategories WHERE " + Scope() + " AND id = " + D.Str(id.Trim()) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<List<EventCategory>> ListAllAsync(CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT * FROM eventcategories WHERE " + Scope() + " ORDER BY sortorder, id LIMIT 1000;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public Task<EnumerationResult<EventCategory>> EnumerateAsync(bool includeRetired, EnumerationRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = new List<string>();
            if (!includeRetired) conditions.Add("c.isretired = FALSE");
            return EnumerateAsync(request, "c.*", "eventcategories c", conditions, Map, token);
        }

        public async Task UpdateAsync(EventCategory c, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(c);
            await QueryAsync("UPDATE eventcategories SET label = " + D.Str(c.Label) + ", color = " + D.Str(c.Color) + ", sortorder = " + D.Num(c.SortOrder) + ", isretired = " + D.Bool(c.IsRetired) +
                ", updatedutc = " + D.Ts(c.UpdatedUtc) + " WHERE " + Scope() + " AND id = " + D.Str(c.Id) + ";", token).ConfigureAwait(false);
        }

        public async Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("DELETE FROM eventcategories WHERE " + Scope() + " AND id = " + D.Str(id) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> InUseAsync(string key, CancellationToken token = default)
        {
            RequireText(key, nameof(key));
            long count = await CountAsync("SELECT COUNT(*) AS cnt FROM events WHERE " + Scope() + " AND deletedutc IS NULL AND lower(categorykey) = " + D.Str(key.ToLowerInvariant()) + ";", token).ConfigureAwait(false);
            return count > 0;
        }

        public async Task<int> SetOrderAsync(List<string> ids, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            if (ids.Count == 0) return 0;
            List<string> statements = ids.Select((id, i) => "UPDATE eventcategories SET sortorder = " + D.Num(i) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) + ";").ToList();
            statements.Add("SELECT COUNT(*) AS cnt FROM eventcategories WHERE " + Scope() + " AND id IN " + D.InList(ids) + ";");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? 0 : (int)RowReader.GetLong(table.Rows[0], "cnt");
        }

        public async Task<string?> ReadSourceHashAsync(CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT sourcehash FROM eventconfig WHERE " + Scope() + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return RowReader.GetNullableString(table.Rows[0], "sourcehash") ?? String.Empty;
        }

        public async Task SeedAsync(List<EventCategory> categories, string sourceHash, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(categories);
            List<string> statements = new List<string>();
            foreach (EventCategory c in categories)
            {
                statements.Add("INSERT INTO eventcategories (propertyid, id, key, label, color, sortorder, isretired, createdutc, updatedutc) VALUES (" + Pid + ", " + D.Str(c.Id) + ", " + D.Str(c.Key) + ", " +
                    D.Str(c.Label) + ", " + D.Str(c.Color) + ", " + D.Num(c.SortOrder) + ", FALSE, " + D.Ts(nowUtc) + ", " + D.Ts(nowUtc) + ") ON CONFLICT (propertyid, (lower(key))) DO UPDATE SET label = excluded.label, " +
                    "color = excluded.color, isretired = FALSE, updatedutc = excluded.updatedutc;");
            }
            statements.Add("INSERT INTO eventconfig (propertyid, sourcehash, updatedutc) VALUES (" + Pid + ", " + D.Str(sourceHash) + ", " + D.Ts(nowUtc) + ") ON CONFLICT (propertyid) DO UPDATE SET sourcehash = excluded.sourcehash, updatedutc = excluded.updatedutc;");
            await TransactionAsync(statements, token).ConfigureAwait(false);
        }

        private static EventCategory Map(DataRow row)
        {
            return new EventCategory
            {
                Id = RowReader.GetString(row, "id"),
                Key = RowReader.GetString(row, "key"),
                Label = RowReader.GetString(row, "label"),
                Color = RowReader.GetNullableString(row, "color"),
                SortOrder = RowReader.GetInt(row, "sortorder"),
                IsRetired = RowReader.GetBool(row, "isretired"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }
    }
}
