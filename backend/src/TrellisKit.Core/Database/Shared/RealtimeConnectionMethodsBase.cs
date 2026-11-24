namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Models;

    /// <summary>
    /// Shared realtime connection registry methods. The deployment-wide prune and drain statements carry no property
    /// predicate on purpose (reviewed allow-list) and run only on the deployment driver.
    /// </summary>
    internal abstract class RealtimeConnectionMethodsBase : MethodsBase, IRealtimeConnectionMethods
    {
        private const string _Columns = "id, userid, sessionid, taskid, client, appstate, activeconversationid, connectedutc, lastseenutc";

        protected RealtimeConnectionMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task UpsertAsync(RealtimeConnectionRecord record, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(record);
            RequireText(record.Id, nameof(record.Id));
            RequireText(record.UserId, nameof(record.UserId));
            RequireText(record.SessionId, nameof(record.SessionId));
            RequireText(record.TaskId, nameof(record.TaskId));
            await QueryAsync("INSERT INTO realtimeconnections (propertyid, " + _Columns + ") VALUES (" + Pid + ", " + D.Str(record.Id) + ", " + D.Str(record.UserId) + ", " + D.Str(record.SessionId) + ", " +
                D.Str(record.TaskId) + ", " + D.Str(record.Client) + ", " + D.Str(record.AppState) + ", " + D.Str(record.ActiveConversationId) + ", " + D.Ts(record.ConnectedUtc) + ", " + D.Ts(record.LastSeenUtc) +
                ") ON CONFLICT (id) DO UPDATE SET appstate = excluded.appstate, activeconversationid = excluded.activeconversationid, lastseenutc = excluded.lastseenutc WHERE realtimeconnections.propertyid = " + Pid + ";", token).ConfigureAwait(false);
        }

        public async Task<int> TouchAsync(IEnumerable<string> ids, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            List<string> list = ids.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            int touched = 0;
            for (int i = 0; i < list.Count; i += 500)
            {
                DataTable table = await QueryAsync("UPDATE realtimeconnections SET lastseenutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id IN " + D.InList(list.Skip(i).Take(500)) + " RETURNING id;", token).ConfigureAwait(false);
                touched += table.Rows.Count;
            }
            return touched;
        }

        public async Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("DELETE FROM realtimeconnections WHERE " + Scope() + " AND id = " + D.Str(id) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<List<RealtimeConnectionRecord>> EnumerateRecentByUserAsync(string userId, DateTime seenSinceUtc, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM realtimeconnections WHERE " + Scope() + " AND userid = " + D.Str(userId) + " AND lastseenutc >= " + D.Ts(seenSinceUtc) +
                " ORDER BY lastseenutc DESC, id DESC LIMIT 100;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<int> DeleteForTaskAsync(string taskId, CancellationToken token = default)
        {
            RequireText(taskId, nameof(taskId));
            // Deployment-wide (reviewed allow-list): one task's sockets span every property it serves.
            DataTable table = await QueryAsync("DELETE FROM realtimeconnections WHERE taskid = " + D.Str(taskId) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public async Task<int> PruneStaleAsync(DateTime seenBeforeUtc, int limit, CancellationToken token = default)
        {
            // Deployment-wide (reviewed allow-list): rows of tasks that died without draining, for every property.
            DataTable table = await QueryAsync("DELETE FROM realtimeconnections WHERE id IN (SELECT id FROM realtimeconnections WHERE lastseenutc < " + D.Ts(seenBeforeUtc) +
                " ORDER BY lastseenutc LIMIT " + D.Num(Math.Max(1, limit)) + ") RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        private RealtimeConnectionRecord Map(DataRow row)
        {
            return new RealtimeConnectionRecord
            {
                Id = RowReader.GetString(row, "id"),
                PropertyId = BoundPropertyId ?? String.Empty,
                UserId = RowReader.GetString(row, "userid"),
                SessionId = RowReader.GetString(row, "sessionid"),
                TaskId = RowReader.GetString(row, "taskid"),
                Client = RowReader.GetString(row, "client"),
                AppState = RowReader.GetString(row, "appstate"),
                ActiveConversationId = RowReader.GetNullableString(row, "activeconversationid"),
                ConnectedUtc = RowReader.GetDateTime(row, "connectedutc"),
                LastSeenUtc = RowReader.GetDateTime(row, "lastseenutc")
            };
        }
    }
}
