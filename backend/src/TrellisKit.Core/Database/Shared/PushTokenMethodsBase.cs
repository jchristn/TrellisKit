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
    /// Shared push token and push delivery methods.
    /// </summary>
    internal abstract class PushTokenMethodsBase : MethodsBase, IPushTokenMethods
    {
        private const string _Columns = "id, userid, sessionid, token, platform, deviceid, devicename, appversion, failurecount, disabledutc, lastsuccessutc, createdutc, lastseenutc";
        private const string _DeliveryColumns = "id, notificationid, pushtokenid, ticketid, status, error, createdutc, receiptcheckedutc";

        protected PushTokenMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<PushToken> UpsertAsync(PushToken pushToken, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(pushToken);
            RequireText(pushToken.Id, nameof(pushToken.Id));
            RequireText(pushToken.Token, nameof(pushToken.Token));
            DataTable table = await QueryAsync("INSERT INTO pushtokens (propertyid, " + _Columns + ") VALUES (" + Pid + ", " + D.Str(pushToken.Id) + ", " + D.Str(pushToken.UserId) + ", " + D.Str(pushToken.SessionId) + ", " +
                D.Str(pushToken.Token) + ", " + D.Enum(pushToken.Platform) + ", " + D.Str(pushToken.DeviceId) + ", " + D.Str(pushToken.DeviceName) + ", " + D.Str(pushToken.AppVersion) + ", 0, NULL, NULL, " +
                D.Ts(pushToken.CreatedUtc) + ", " + D.Ts(pushToken.LastSeenUtc) + ") ON CONFLICT (propertyid, token) DO UPDATE SET userid = excluded.userid, sessionid = excluded.sessionid, platform = excluded.platform, " +
                "deviceid = COALESCE(excluded.deviceid, pushtokens.deviceid), devicename = COALESCE(excluded.devicename, pushtokens.devicename), appversion = COALESCE(excluded.appversion, pushtokens.appversion), failurecount = 0, disabledutc = NULL, lastseenutc = excluded.lastseenutc RETURNING " + _Columns + ";", token).ConfigureAwait(false);
            return Map(table.Rows[0]);
        }

        public async Task<int> TrimAsync(string userId, int keep, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable excess = await QueryAsync("SELECT id FROM (SELECT id, ROW_NUMBER() OVER (ORDER BY lastseenutc DESC, id DESC) AS rn FROM pushtokens WHERE " + Scope() + " AND userid = " + D.Str(userId) +
                ") ranked WHERE rn > " + D.Num(Math.Max(0, keep)) + ";", token).ConfigureAwait(false);
            List<string> ids = excess.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "id")).ToList();
            if (ids.Count == 0) return 0;
            return await DeleteIdsAsync(ids, token).ConfigureAwait(false);
        }

        public async Task<bool> DeleteByValueAsync(string userId, string tokenValue, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            RequireText(tokenValue, nameof(tokenValue));
            string ids = "SELECT id FROM pushtokens WHERE " + Scope() + " AND userid = " + D.Str(userId) + " AND token = " + D.Str(tokenValue);
            List<string> statements = new List<string> { "DELETE FROM pushdeliveries WHERE " + Scope() + " AND pushtokenid IN (" + ids + ");" };
            statements.AddRange(Driver.Metadata.DeleteWhereStatements(TargetTypeEnum.PushToken, ids));
            statements.Add("DELETE FROM pushtokens WHERE " + Scope() + " AND userid = " + D.Str(userId) + " AND token = " + D.Str(tokenValue) + " RETURNING id;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            return await DeleteIdsAsync(new List<string> { id }, token).ConfigureAwait(false) > 0;
        }

        public async Task<int> DeleteByUserAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            string ids = "SELECT id FROM pushtokens WHERE " + Scope() + " AND userid = " + D.Str(userId);
            List<string> statements = new List<string> { "DELETE FROM pushdeliveries WHERE " + Scope() + " AND pushtokenid IN (" + ids + ");" };
            statements.AddRange(Driver.Metadata.DeleteWhereStatements(TargetTypeEnum.PushToken, ids));
            statements.Add("DELETE FROM pushtokens WHERE " + Scope() + " AND userid = " + D.Str(userId) + " RETURNING id;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public async Task<List<PushToken>> EnumerateByUserAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM pushtokens WHERE " + Scope() + " AND userid = " + D.Str(userId) + " ORDER BY lastseenutc DESC, id DESC;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public Task<EnumerationResult<PushToken>> EnumerateForUserAsync(string userId, EnumerationRequest request, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            ArgumentNullException.ThrowIfNull(request);
            string select = String.Join(", ", _Columns.Split(", ").Select(c => "t." + c));
            return EnumerateAsync(request, select, "pushtokens t", new List<string> { "t.userid = " + D.Str(userId) }, Map, token);
        }

        public async Task<List<PushToken>> EnumerateActiveByUsersAsync(IEnumerable<string> userIds, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(userIds);
            List<string> ids = userIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return new List<PushToken>();
            DataTable table = await QueryAsync("SELECT " + String.Join(", ", _Columns.Split(", ").Select(c => "t." + c)) + " FROM pushtokens t JOIN authsessions s ON " + Scope("s") + " AND s.id = t.sessionid WHERE " + Scope("t") + " AND t.userid IN " + D.InList(ids) +
                " AND t.disabledutc IS NULL AND s.revokedutc IS NULL AND s.expiresutc > " + D.Ts(nowUtc) + " ORDER BY t.userid, t.lastseenutc DESC, t.id;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task RecordSuccessAsync(string id, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            await QueryAsync("UPDATE pushtokens SET failurecount = 0, lastsuccessutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
        }

        public async Task RecordFailureAsync(string id, int disableAfter, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            await QueryAsync("UPDATE pushtokens SET failurecount = failurecount + 1, disabledutc = CASE WHEN disabledutc IS NULL AND failurecount + 1 >= " + D.Num(Math.Max(1, disableAfter)) +
                " THEN " + D.Ts(nowUtc) + " ELSE disabledutc END WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
        }

        public async Task<PushToken?> ReadAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM pushtokens WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task CreateDeliveriesAsync(List<PushDelivery> deliveries, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(deliveries);
            if (deliveries.Count == 0) return;
            List<string> statements = new List<string>();
            for (int i = 0; i < deliveries.Count; i += 200)
            {
                statements.Add("INSERT INTO pushdeliveries (propertyid, " + _DeliveryColumns + ") VALUES " + String.Join(", ", deliveries.Skip(i).Take(200).Select(d =>
                    "(" + Pid + ", " + D.Str(d.Id) + ", " + D.Str(d.NotificationId) + ", " + D.Str(d.PushTokenId) + ", " + D.Str(d.TicketId) + ", " + D.Enum(d.Status) + ", " + D.Str(d.Error) + ", " +
                    D.Ts(d.CreatedUtc) + ", " + D.Ts(d.ReceiptCheckedUtc) + ")")) + ";");
            }
            await TransactionAsync(statements, token).ConfigureAwait(false);
        }

        public async Task<List<PushDelivery>> EnumerateDueReceiptsAsync(DateTime createdBeforeUtc, int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT " + _DeliveryColumns + " FROM pushdeliveries WHERE " + Scope() + " AND status = " + D.Enum(PushDeliveryStatusEnum.Sent) + " AND ticketid IS NOT NULL AND receiptcheckedutc IS NULL AND createdutc <= " +
                D.Ts(createdBeforeUtc) + " ORDER BY createdutc, id LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapDelivery).ToList();
        }

        public async Task UpdateDeliveryAsync(PushDelivery delivery, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(delivery);
            RequireText(delivery.Id, nameof(delivery.Id));
            await QueryAsync("UPDATE pushdeliveries SET status = " + D.Enum(delivery.Status) + ", error = " + D.Str(delivery.Error) + ", receiptcheckedutc = " + D.Ts(delivery.ReceiptCheckedUtc) +
                " WHERE " + Scope() + " AND id = " + D.Str(delivery.Id) + ";", token).ConfigureAwait(false);
        }

        public async Task<List<PushDelivery>> EnumerateDeliveriesAsync(string notificationId, CancellationToken token = default)
        {
            RequireText(notificationId, nameof(notificationId));
            DataTable table = await QueryAsync("SELECT " + _DeliveryColumns + " FROM pushdeliveries WHERE " + Scope() + " AND notificationid = " + D.Str(notificationId) + " ORDER BY createdutc, id;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapDelivery).ToList();
        }

        public async Task<int> PruneDeliveriesAsync(DateTime createdBeforeUtc, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("DELETE FROM pushdeliveries WHERE " + Scope() + " AND createdutc < " + D.Ts(createdBeforeUtc) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        private async Task<int> DeleteIdsAsync(List<string> ids, CancellationToken token)
        {
            string list = D.InList(ids);
            List<string> statements = new List<string> { "DELETE FROM pushdeliveries WHERE " + Scope() + " AND pushtokenid IN " + list + ";" };
            statements.AddRange(Driver.Metadata.DeleteWhereStatements(TargetTypeEnum.PushToken, "SELECT id FROM pushtokens WHERE " + Scope() + " AND id IN " + list));
            statements.Add("DELETE FROM pushtokens WHERE " + Scope() + " AND id IN " + list + " RETURNING id;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        private static PushToken Map(DataRow row)
        {
            return new PushToken
            {
                Id = RowReader.GetString(row, "id"),
                UserId = RowReader.GetString(row, "userid"),
                SessionId = RowReader.GetString(row, "sessionid"),
                Token = RowReader.GetString(row, "token"),
                Platform = RowReader.GetEnum(row, "platform", PushPlatformEnum.Ios),
                DeviceId = RowReader.GetNullableString(row, "deviceid"),
                DeviceName = RowReader.GetNullableString(row, "devicename"),
                AppVersion = RowReader.GetNullableString(row, "appversion"),
                FailureCount = RowReader.GetInt(row, "failurecount"),
                DisabledUtc = RowReader.GetNullableDateTime(row, "disabledutc"),
                LastSuccessUtc = RowReader.GetNullableDateTime(row, "lastsuccessutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                LastSeenUtc = RowReader.GetDateTime(row, "lastseenutc")
            };
        }

        private static PushDelivery MapDelivery(DataRow row)
        {
            return new PushDelivery
            {
                Id = RowReader.GetString(row, "id"),
                NotificationId = RowReader.GetString(row, "notificationid"),
                PushTokenId = RowReader.GetString(row, "pushtokenid"),
                TicketId = RowReader.GetNullableString(row, "ticketid"),
                Status = RowReader.GetEnum(row, "status", PushDeliveryStatusEnum.Queued),
                Error = RowReader.GetNullableString(row, "error"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                ReceiptCheckedUtc = RowReader.GetNullableDateTime(row, "receiptcheckedutc")
            };
        }
    }
}
