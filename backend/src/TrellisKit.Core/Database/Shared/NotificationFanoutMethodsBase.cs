namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Shared notification fan-out job methods.
    /// </summary>
    internal abstract class NotificationFanoutMethodsBase : MethodsBase, INotificationFanoutMethods
    {
        private const string _Columns = "id, type, sourceid, lastuserid, status, createdcount, claimedby, claimuntilutc, createdutc, completedutc";

        protected NotificationFanoutMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<bool> CreateAsync(NotificationFanout job, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(job);
            RequireText(job.Id, nameof(job.Id));
            RequireText(job.SourceId, nameof(job.SourceId));
            DataTable table = await QueryAsync("INSERT INTO notificationfanouts (propertyid, " + _Columns + ") VALUES (" + Pid + ", " + D.Str(job.Id) + ", " + D.Enum(job.Type) + ", " + D.Str(job.SourceId) + ", NULL, " +
                D.Enum(FanoutStatusEnum.Pending) + ", 0, NULL, NULL, " + D.Ts(job.CreatedUtc) + ", NULL) ON CONFLICT (propertyid, type, sourceid) DO NOTHING RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<NotificationFanout?> ReadAsync(NotificationTypeEnum type, string sourceId, CancellationToken token = default)
        {
            RequireText(sourceId, nameof(sourceId));
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM notificationfanouts WHERE " + Scope() + " AND type = " + D.Enum(type) + " AND sourceid = " + D.Str(sourceId) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<List<NotificationFanout>> ClaimAsync(string holder, DateTime nowUtc, TimeSpan claimDuration, int limit, CancellationToken token = default)
        {
            RequireText(holder, nameof(holder));
            string free = "(claimuntilutc IS NULL OR claimuntilutc < " + D.Ts(nowUtc) + ")";
            string open = "status <> " + D.Enum(FanoutStatusEnum.Done);
            DataTable table = await QueryAsync("UPDATE notificationfanouts SET claimedby = " + D.Str(holder) + ", claimuntilutc = " + D.Ts(nowUtc + claimDuration) + ", status = " + D.Enum(FanoutStatusEnum.Running) +
                " WHERE " + Scope() + " AND id IN (SELECT id FROM notificationfanouts WHERE " + Scope() + " AND " + open + " AND " + free + " ORDER BY createdutc, id LIMIT " + D.Num(Math.Max(1, limit)) + ") AND " + open + " AND " + free +
                " RETURNING " + _Columns + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).OrderBy(j => j.CreatedUtc).ThenBy(j => j.Id, StringComparer.Ordinal).ToList();
        }

        public async Task<bool> RecordProgressAsync(string id, string holder, string lastUserId, int added, DateTime claimUntilUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            RequireText(holder, nameof(holder));
            RequireText(lastUserId, nameof(lastUserId));
            DataTable table = await QueryAsync("UPDATE notificationfanouts SET lastuserid = " + D.Str(lastUserId) + ", createdcount = createdcount + " + D.Num(Math.Max(0, added)) +
                ", claimuntilutc = " + D.Ts(claimUntilUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND claimedby = " + D.Str(holder) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> CompleteAsync(string id, string holder, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            RequireText(holder, nameof(holder));
            DataTable table = await QueryAsync("UPDATE notificationfanouts SET status = " + D.Enum(FanoutStatusEnum.Done) + ", completedutc = " + D.Ts(nowUtc) + ", claimedby = NULL, claimuntilutc = NULL WHERE " + Scope() + " AND id = " +
                D.Str(id) + " AND claimedby = " + D.Str(holder) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        private static NotificationFanout Map(DataRow row)
        {
            return new NotificationFanout
            {
                Id = RowReader.GetString(row, "id"),
                Type = RowReader.GetEnum(row, "type", NotificationTypeEnum.NewFeedPost),
                SourceId = RowReader.GetString(row, "sourceid"),
                LastUserId = RowReader.GetNullableString(row, "lastuserid"),
                Status = RowReader.GetEnum(row, "status", FanoutStatusEnum.Pending),
                CreatedCount = RowReader.GetInt(row, "createdcount"),
                ClaimedBy = RowReader.GetNullableString(row, "claimedby"),
                ClaimUntilUtc = RowReader.GetNullableDateTime(row, "claimuntilutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                CompletedUtc = RowReader.GetNullableDateTime(row, "completedutc")
            };
        }
    }
}
