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
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Shared entitlement methods.
    /// </summary>
    internal abstract class EntitlementMethodsBase : MethodsBase, IEntitlementMethods
    {
        protected EntitlementMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<Entitlement> CreateAsync(Entitlement entitlement, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(entitlement);
            await QueryAsync(Insert(entitlement, D, Pid) + ";", token).ConfigureAwait(false);
            return entitlement;
        }

        public async Task<Entitlement?> ReadAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("SELECT * FROM entitlements WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<Entitlement?> ReadBySourceAsync(MembershipSourceEnum source, string sourceId, CancellationToken token = default)
        {
            RequireText(sourceId, nameof(sourceId));
            DataTable table = await QueryAsync("SELECT * FROM entitlements WHERE " + Scope() + " AND source = " + D.Enum(source) + " AND sourceid = " + D.Str(sourceId) +
                " ORDER BY createdutc DESC LIMIT 1;", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<List<Entitlement>> EnumerateByUserAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("SELECT * FROM entitlements WHERE " + Scope() + " AND userid = " + D.Str(userId) + " ORDER BY createdutc DESC, id DESC;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<List<Entitlement>> EnumerateByUserAfterAsync(string userId, string? afterId, int limit, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            string after = afterId == null ? String.Empty : " AND id > " + D.Str(afterId);
            DataTable table = await QueryAsync("SELECT * FROM entitlements" + " WHERE " + Scope() + " AND (userid = " + D.Str(userId) + ")" + after + " ORDER BY id LIMIT " + D.Num(Math.Clamp(limit, 1, 1000)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<List<Entitlement>> EnumerateByUsersAsync(IEnumerable<string> userIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(userIds);
            List<string> ids = userIds.ToList();
            if (ids.Count == 0) return new List<Entitlement>();
            DataTable table = await QueryAsync("SELECT * FROM entitlements WHERE " + Scope() + " AND userid IN " + D.InList(ids) + " ORDER BY createdutc DESC, id DESC;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<Entitlement> UpdateAsync(Entitlement entitlement, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(entitlement);
            Entitlement e = entitlement;
            await QueryAsync("UPDATE entitlements SET startsutc = " + D.Ts(e.StartsUtc) + ", endsutc = " + D.Ts(e.EndsUtc) + ", revokedutc = " + D.Ts(e.RevokedUtc) +
                ", revokereason = " + D.Str(e.RevokeReason) + ", reason = " + D.Str(e.Reason) + ", updatedutc = " + D.Ts(e.UpdatedUtc) +
                " WHERE " + Scope() + " AND id = " + D.Str(e.Id) + ";", token).ConfigureAwait(false);
            return entitlement;
        }

        public Task<long> CountActiveUsersAsync(DateTime nowUtc, MembershipSourceEnum? source, CancellationToken token = default)
        {
            string sql = "SELECT COUNT(*) AS cnt FROM users WHERE " + Scope() + " AND isdeleted = " + D.Bool(false) + " AND " +
                ActiveEntitlementExists("users.id", nowUtc, source == null ? null : "source = " + D.Enum(source.Value)) + ";";
            return CountAsync(sql, token);
        }

        public Task<long> CountFreeOnlyUsersAsync(DateTime nowUtc, CancellationToken token = default)
        {
            string paid = "source = " + D.Enum(MembershipSourceEnum.Subscription);
            string sql = "SELECT COUNT(*) AS cnt FROM users WHERE " + Scope() + " AND isdeleted = " + D.Bool(false) + " AND " +
                ActiveEntitlementExists("users.id", nowUtc, null) + " AND NOT " + ActiveEntitlementExists("users.id", nowUtc, paid) + ";";
            return CountAsync(sql, token);
        }

        public async Task<Dictionary<StoreEnum, long>> CountActiveUsersByStoreAsync(DateTime nowUtc, CancellationToken token = default)
        {
            Dictionary<StoreEnum, long> counts = new Dictionary<StoreEnum, long>();
            foreach (StoreEnum store in System.Enum.GetValues<StoreEnum>())
            {
                string condition = "source = " + D.Enum(MembershipSourceEnum.Subscription) +
                    " AND ae.sourceid IN (SELECT s.id FROM subscriptions s WHERE " + Scope("s") + " AND s.store = " + D.Enum(store) + ")";
                counts[store] = await CountAsync("SELECT COUNT(*) AS cnt FROM users WHERE " + Scope() + " AND isdeleted = " + D.Bool(false) + " AND " +
                    ActiveEntitlementExists("users.id", nowUtc, condition) + ";", token).ConfigureAwait(false);
            }
            return counts;
        }

        public Task<long> CountLapsedUsersAsync(DateTime fromUtc, DateTime nowUtc, CancellationToken token = default)
        {
            string sql = "SELECT COUNT(*) AS cnt FROM users WHERE " + Scope() + " AND isdeleted = " + D.Bool(false) +
                " AND EXISTS (SELECT 1 FROM entitlements le WHERE " + Scope("le") + " AND le.userid = users.id AND (" +
                "(le.endsutc IS NOT NULL AND le.endsutc >= " + D.Ts(fromUtc) + " AND le.endsutc <= " + D.Ts(nowUtc) + ") OR " +
                "(le.revokedutc IS NOT NULL AND le.revokedutc >= " + D.Ts(fromUtc) + " AND le.revokedutc <= " + D.Ts(nowUtc) + ")))" +
                " AND NOT " + ActiveEntitlementExists("users.id", nowUtc, null) + ";";
            return CountAsync(sql, token);
        }

        internal static string Insert(Entitlement e, SqlDialect d, string pid)
        {
            return "INSERT INTO entitlements (propertyid, id, userid, source, sourceid, startsutc, endsutc, revokedutc, revokereason, reason, granttype, grantedbyuserid, createdutc, updatedutc) VALUES (" +
                pid + ", " + d.Str(e.Id) + ", " + d.Str(e.UserId) + ", " + d.Enum(e.Source) + ", " + d.Str(e.SourceId) + ", " + d.Ts(e.StartsUtc) + ", " + d.Ts(e.EndsUtc) + ", " +
                d.Ts(e.RevokedUtc) + ", " + d.Str(e.RevokeReason) + ", " + d.Str(e.Reason) + ", " + d.Enum(e.GrantType) + ", " + d.Str(e.GrantedByUserId) + ", " + d.Ts(e.CreatedUtc) + ", " + d.Ts(e.UpdatedUtc) + ")";
        }

        public Task<EnumerationResult<Entitlement>> EnumerateAsync(EnumerationRequest request, EntitlementFilter filter, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            RequireText(filter.UserId, nameof(filter.UserId));
            string n = D.Ts(nowUtc);
            List<string> conditions = new List<string> { "en.userid = " + D.Str(filter.UserId) };
            if (filter.Source != null) conditions.Add("en.source = " + D.Enum(filter.Source.Value));
            if (filter.Status != null)
            {
                switch (filter.Status.Value)
                {
                    case EntitlementStatusEnum.Revoked:
                        conditions.Add("en.revokedutc IS NOT NULL");
                        break;
                    case EntitlementStatusEnum.Scheduled:
                        conditions.Add("en.revokedutc IS NULL AND en.startsutc > " + n);
                        break;
                    case EntitlementStatusEnum.Expired:
                        conditions.Add("en.revokedutc IS NULL AND en.startsutc <= " + n + " AND en.endsutc IS NOT NULL AND en.endsutc <= " + n);
                        break;
                    default:
                        conditions.Add("en.revokedutc IS NULL AND en.startsutc <= " + n + " AND (en.endsutc IS NULL OR en.endsutc > " + n + ")");
                        break;
                }
            }
            return EnumerateAsync(request, "en.id, en.userid, en.source, en.sourceid, en.startsutc, en.endsutc, en.revokedutc, en.revokereason, en.reason, en.granttype, en.grantedbyuserid, en.createdutc, en.updatedutc",
                "entitlements en", conditions, Map, token);
        }

        internal static Entitlement Map(DataRow row)
        {
            return new Entitlement
            {
                Id = RowReader.GetString(row, "id"),
                UserId = RowReader.GetString(row, "userid"),
                Source = RowReader.GetEnum(row, "source", MembershipSourceEnum.Subscription),
                SourceId = RowReader.GetString(row, "sourceid"),
                StartsUtc = RowReader.GetDateTime(row, "startsutc"),
                EndsUtc = RowReader.GetNullableDateTime(row, "endsutc"),
                RevokedUtc = RowReader.GetNullableDateTime(row, "revokedutc"),
                RevokeReason = RowReader.GetNullableString(row, "revokereason"),
                Reason = RowReader.GetNullableString(row, "reason"),
                GrantType = RowReader.GetNullableEnum<GrantTypeEnum>(row, "granttype"),
                GrantedByUserId = RowReader.GetNullableString(row, "grantedbyuserid"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }
    }
}
