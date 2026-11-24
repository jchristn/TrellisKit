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
    /// Shared subscription methods.
    /// </summary>
    internal abstract class SubscriptionMethodsBase : MethodsBase, ISubscriptionMethods
    {
        private const string _Columns =
            "id, userid, store, storesubscriptionid, productid, status, currentperiodstartutc, currentperiodendutc, autorenew, environment, " +
            "latesttransactionid, createdutc, updatedutc, lasteventutc";

        protected SubscriptionMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<Subscription> CreateAsync(Subscription subscription, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(subscription);
            Subscription s = subscription;
            string sql = "INSERT INTO subscriptions (propertyid, " + _Columns + ") VALUES (" +
                Pid + ", " + D.Str(s.Id) + ", " + D.Str(s.UserId) + ", " + D.Enum(s.Store) + ", " + D.Str(s.StoreSubscriptionId) + ", " + D.Str(s.ProductId) + ", " +
                D.Enum(s.Status) + ", " + D.Ts(s.CurrentPeriodStartUtc) + ", " + D.Ts(s.CurrentPeriodEndUtc) + ", " + D.Bool(s.AutoRenew) + ", " +
                D.Enum(s.Environment) + ", " + D.Str(s.LatestTransactionId) + ", " + D.Ts(s.CreatedUtc) + ", " + D.Ts(s.UpdatedUtc) + ", " + D.Ts(s.LastEventUtc) + ");";
            await UniqueAsync(sql, "That store subscription", token).ConfigureAwait(false);
            return subscription;
        }

        public Task<Subscription?> ReadAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            return ReadOneAsync("id = " + D.Str(id), token);
        }

        public Task<Subscription?> ReadByStoreIdAsync(StoreEnum store, string storeSubscriptionId, CancellationToken token = default)
        {
            RequireText(storeSubscriptionId, nameof(storeSubscriptionId));
            return ReadOneAsync("store = " + D.Enum(store) + " AND storesubscriptionid = " + D.Str(storeSubscriptionId), token);
        }

        public async Task<List<Subscription>> EnumerateByUserAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM subscriptions WHERE " + Scope() + " AND userid = " + D.Str(userId) + " ORDER BY createdutc DESC, id DESC;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<List<Subscription>> EnumerateByUserAfterAsync(string userId, string? afterId, int limit, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            string after = afterId == null ? String.Empty : " AND id > " + D.Str(afterId);
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM subscriptions" + " WHERE " + Scope() + " AND (userid = " + D.Str(userId) + ")" + after + " ORDER BY id LIMIT " + D.Num(Math.Clamp(limit, 1, 1000)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<Subscription> UpdateAsync(Subscription subscription, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(subscription);
            Subscription s = subscription;
            string updateSql = "UPDATE subscriptions SET userid = " + D.Str(s.UserId) + ", productid = " + D.Str(s.ProductId) + ", status = " + D.Enum(s.Status) +
                ", currentperiodstartutc = " + D.Ts(s.CurrentPeriodStartUtc) + ", currentperiodendutc = " + D.Ts(s.CurrentPeriodEndUtc) +
                ", autorenew = " + D.Bool(s.AutoRenew) + ", environment = " + D.Enum(s.Environment) + ", latesttransactionid = " + D.Str(s.LatestTransactionId) +
                ", updatedutc = " + D.Ts(s.UpdatedUtc) + ", lasteventutc = " + D.Ts(s.LastEventUtc) + " WHERE " + Scope() + " AND id = " + D.Str(s.Id) + ";";
            await QueryAsync(D.SubscriptionUpdateWithMetrics(updateSql, PropertyId, s.Id, s.UpdatedUtc), token).ConfigureAwait(false);
            return subscription;
        }

        public Task<EnumerationResult<Subscription>> EnumerateAsync(EnumerationRequest request, SubscriptionFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = new List<string>();
            if (filter.Store != null) conditions.Add("s.store = " + D.Enum(filter.Store.Value));
            if (filter.Status != null) conditions.Add("s.status = " + D.Enum(filter.Status.Value));
            if (filter.UserId != null) conditions.Add("s.userid = " + D.Str(filter.UserId));
            string select = String.Join(", ", _Columns.Split(',').Select(c => "s." + c.Trim()));
            return EnumerateAsync(request, select, "subscriptions s LEFT JOIN users u ON u.id = s.userid AND " + Scope("u"), conditions, Map, token);
        }

        public async Task<List<Subscription>> EnumerateForReconciliationAsync(int limit, CancellationToken token = default)
        {
            string sql = "SELECT " + _Columns + " FROM subscriptions WHERE " + Scope() + " AND status NOT IN " +
                D.InList(new[] { SubscriptionStatusEnum.Expired.ToString(), SubscriptionStatusEnum.Revoked.ToString(), SubscriptionStatusEnum.Refunded.ToString() }) +
                " ORDER BY CASE WHEN lasteventutc IS NULL THEN 0 ELSE 1 END, lasteventutc, id LIMIT " + D.Num(Math.Max(1, limit)) + ";";
            DataTable table = await QueryAsync(sql, token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        private async Task<Subscription?> ReadOneAsync(string condition, CancellationToken token)
        {
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM subscriptions WHERE " + Scope() + " AND (" + condition + ");", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        private static Subscription Map(DataRow row)
        {
            return new Subscription
            {
                Id = RowReader.GetString(row, "id"),
                UserId = RowReader.GetString(row, "userid"),
                Store = RowReader.GetEnum(row, "store", StoreEnum.Stripe),
                StoreSubscriptionId = RowReader.GetString(row, "storesubscriptionid"),
                ProductId = RowReader.GetString(row, "productid"),
                Status = RowReader.GetEnum(row, "status", SubscriptionStatusEnum.Expired),
                CurrentPeriodStartUtc = RowReader.GetNullableDateTime(row, "currentperiodstartutc"),
                CurrentPeriodEndUtc = RowReader.GetNullableDateTime(row, "currentperiodendutc"),
                AutoRenew = RowReader.GetBool(row, "autorenew"),
                Environment = RowReader.GetEnum(row, "environment", StoreEnvironmentEnum.Production),
                LatestTransactionId = RowReader.GetNullableString(row, "latesttransactionid"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc"),
                LastEventUtc = RowReader.GetNullableDateTime(row, "lasteventutc")
            };
        }
    }
}
