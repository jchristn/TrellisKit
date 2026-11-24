namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Models;

    internal abstract class GeocodeCacheMethodsBase : MethodsBase, IGeocodeCacheMethods
    {
        protected GeocodeCacheMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<GeocodeCacheEntry?> ReadAsync(string provider, string queryHash, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(provider, nameof(provider));
            RequireText(queryHash, nameof(queryHash));
            DataTable table = await QueryAsync("SELECT * FROM geocoderesults WHERE " + Scope() + " AND provider = " + D.Str(provider) + " AND queryhash = " + D.Str(queryHash) + " AND expiresutc > " + D.Ts(nowUtc) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            DataRow row = table.Rows[0];
            return new GeocodeCacheEntry
            {
                Id = RowReader.GetString(row, "id"),
                QueryHash = RowReader.GetString(row, "queryhash"),
                Provider = RowReader.GetString(row, "provider"),
                Lat = DirectorySql.NullableDouble(row, "lat"),
                Lng = DirectorySql.NullableDouble(row, "lng"),
                TimeZone = RowReader.GetNullableString(row, "timezone"),
                Formatted = RowReader.GetNullableString(row, "formatted"),
                Found = RowReader.GetBool(row, "found"),
                ExpiresUtc = RowReader.GetDateTime(row, "expiresutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
        }

        public async Task WriteAsync(GeocodeCacheEntry entry, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(entry);
            RequireText(entry.Id, nameof(entry.Id));
            await QueryAsync("INSERT INTO geocoderesults (propertyid, id, queryhash, provider, lat, lng, timezone, formatted, found, expiresutc, createdutc) VALUES (" + Pid + ", " + D.Str(entry.Id) + ", " + D.Str(entry.QueryHash) + ", " +
                D.Str(entry.Provider) + ", " + DirectorySql.NullableNum(D, entry.Lat) + ", " + DirectorySql.NullableNum(D, entry.Lng) + ", " + D.Str(entry.TimeZone) + ", " + D.Str(entry.Formatted) + ", " + D.Bool(entry.Found) + ", " +
                D.Ts(entry.ExpiresUtc) + ", " + D.Ts(entry.CreatedUtc) + ") ON CONFLICT (propertyid, provider, queryhash) DO UPDATE SET lat = EXCLUDED.lat, lng = EXCLUDED.lng, timezone = EXCLUDED.timezone, " +
                "formatted = EXCLUDED.formatted, found = EXCLUDED.found, expiresutc = EXCLUDED.expiresutc, createdutc = EXCLUDED.createdutc;", token).ConfigureAwait(false);
        }

        public async Task<int> PruneAsync(DateTime nowUtc, int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("DELETE FROM geocoderesults WHERE " + Scope() + " AND id IN (SELECT e.id FROM geocoderesults e WHERE " + Scope("e") + " AND e.expiresutc <= " + D.Ts(nowUtc) +
                " ORDER BY e.expiresutc LIMIT " + D.Num(Math.Max(1, limit)) + ") RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public Task<long> CountAsync(CancellationToken token = default)
        {
            return CountAsync("SELECT COUNT(*) AS cnt FROM geocoderesults WHERE " + Scope() + ";", token);
        }
    }
}
