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

    internal abstract class EventReminderMethodsBase : MethodsBase, IEventReminderMethods
    {
        protected EventReminderMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<List<EventReminder>> ListForEventAsync(string eventId, CancellationToken token = default)
        {
            RequireText(eventId, nameof(eventId));
            DataTable table = await QueryAsync("SELECT * FROM eventreminders WHERE " + Scope() + " AND eventid = " + D.Str(eventId) + " ORDER BY offsetminutes DESC;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<List<EventReminder>> ListDueAsync(DateTime nowUtc, int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT m.* FROM eventreminders m JOIN events e ON " + Scope("e") + " AND e.id = m.eventid WHERE " + Scope("m") + " AND m.sentutc IS NULL AND m.skippedutc IS NULL AND m.dueutc <= " +
                D.Ts(nowUtc) + " AND e.deletedutc IS NULL ORDER BY m.dueutc, m.id LIMIT " + D.Num(Math.Clamp(limit, 1, 1000)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<bool> MarkSentAsync(string id, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("UPDATE eventreminders SET sentutc = " + D.Ts(nowUtc) + ", claimedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) +
                " AND sentutc IS NULL AND skippedutc IS NULL RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> MarkSkippedAsync(string id, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("UPDATE eventreminders SET skippedutc = " + D.Ts(nowUtc) + ", claimedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) +
                " AND sentutc IS NULL AND skippedutc IS NULL RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<int> SkipPendingAsync(string eventId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(eventId, nameof(eventId));
            DataTable table = await QueryAsync("UPDATE eventreminders SET skippedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND eventid = " + D.Str(eventId) + " AND sentutc IS NULL AND skippedutc IS NULL RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        private static EventReminder Map(DataRow row)
        {
            return new EventReminder
            {
                Id = RowReader.GetString(row, "id"),
                EventId = RowReader.GetString(row, "eventid"),
                OffsetMinutes = RowReader.GetInt(row, "offsetminutes"),
                DueUtc = RowReader.GetDateTime(row, "dueutc"),
                SentUtc = RowReader.GetNullableDateTime(row, "sentutc"),
                SkippedUtc = RowReader.GetNullableDateTime(row, "skippedutc"),
                ClaimedUtc = RowReader.GetNullableDateTime(row, "claimedutc")
            };
        }
    }
}
