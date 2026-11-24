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
    /// Shared subscription event methods.
    /// </summary>
    internal abstract class SubscriptionEventMethodsBase : MethodsBase, ISubscriptionEventMethods
    {
        protected SubscriptionEventMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<bool> TryCreateAsync(SubscriptionEvent subscriptionEvent, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(subscriptionEvent);
            SubscriptionEvent e = subscriptionEvent;
            string sql = "INSERT INTO subscriptionevents (propertyid, id, subscriptionid, userid, store, storeeventid, source, eventtype, receivedutc, processedutc, outcome, rawpayloadjson) VALUES (" +
                Pid + ", " + D.Str(e.Id) + ", " + D.Str(e.SubscriptionId) + ", " + D.Str(e.UserId) + ", " + D.Enum(e.Store) + ", " + D.Str(e.StoreEventId) + ", " +
                D.Enum(e.Source) + ", " + D.Str(e.EventType) + ", " + D.Ts(e.ReceivedUtc) + ", " + D.Ts(e.ProcessedUtc) + ", " + D.Str(e.Outcome) + ", " +
                D.Str(e.RawPayloadJson) + ") ON CONFLICT (propertyid, store, storeeventid) DO NOTHING RETURNING id;";
            DataTable table = await QueryAsync(sql, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<SubscriptionEvent?> ReadByStoreEventIdAsync(StoreEnum store, string storeEventId, CancellationToken token = default)
        {
            RequireText(storeEventId, nameof(storeEventId));
            DataTable table = await QueryAsync("SELECT * FROM subscriptionevents WHERE " + Scope() + " AND store = " + D.Enum(store) + " AND storeeventid = " + D.Str(storeEventId) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task UpdateAsync(SubscriptionEvent subscriptionEvent, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(subscriptionEvent);
            SubscriptionEvent e = subscriptionEvent;
            await QueryAsync("UPDATE subscriptionevents SET subscriptionid = " + D.Str(e.SubscriptionId) + ", userid = " + D.Str(e.UserId) +
                ", eventtype = " + D.Str(e.EventType) + ", processedutc = " + D.Ts(e.ProcessedUtc) + ", outcome = " + D.Str(e.Outcome) +
                " WHERE " + Scope() + " AND id = " + D.Str(e.Id) + ";", token).ConfigureAwait(false);
        }

        public async Task<List<SubscriptionEvent>> EnumerateBySubscriptionAsync(string subscriptionId, int limit, CancellationToken token = default)
        {
            RequireText(subscriptionId, nameof(subscriptionId));
            DataTable table = await QueryAsync("SELECT * FROM subscriptionevents WHERE " + Scope() + " AND subscriptionid = " + D.Str(subscriptionId) +
                " ORDER BY receivedutc DESC, id DESC LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<List<SubscriptionEvent>> EnumerateByUserAsync(string userId, int limit, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("SELECT * FROM subscriptionevents WHERE " + Scope() + " AND userid = " + D.Str(userId) +
                " ORDER BY receivedutc DESC, id DESC LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public Task<EnumerationResult<SubscriptionEvent>> EnumerateBySubscriptionAsync(string subscriptionId, EnumerationRequest request, string? outcome, CancellationToken token = default)
        {
            RequireText(subscriptionId, nameof(subscriptionId));
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = new List<string> { "se.subscriptionid = " + D.Str(subscriptionId) };
            if (!String.IsNullOrWhiteSpace(outcome)) conditions.Add("se.outcome = " + D.Str(outcome.Trim()));
            return EnumerateAsync(request, "se.id, se.subscriptionid, se.userid, se.store, se.storeeventid, se.source, se.eventtype, se.receivedutc, se.processedutc, se.outcome, se.rawpayloadjson",
                "subscriptionevents se", conditions, Map, token);
        }

        private static SubscriptionEvent Map(DataRow row)
        {
            return new SubscriptionEvent
            {
                Id = RowReader.GetString(row, "id"),
                SubscriptionId = RowReader.GetNullableString(row, "subscriptionid"),
                UserId = RowReader.GetNullableString(row, "userid"),
                Store = RowReader.GetEnum(row, "store", StoreEnum.Stripe),
                StoreEventId = RowReader.GetString(row, "storeeventid"),
                Source = RowReader.GetEnum(row, "source", SubscriptionEventSourceEnum.Webhook),
                EventType = RowReader.GetString(row, "eventtype"),
                ReceivedUtc = RowReader.GetDateTime(row, "receivedutc"),
                ProcessedUtc = RowReader.GetNullableDateTime(row, "processedutc"),
                Outcome = RowReader.GetString(row, "outcome"),
                RawPayloadJson = RowReader.GetNullableString(row, "rawpayloadjson")
            };
        }
    }
}
