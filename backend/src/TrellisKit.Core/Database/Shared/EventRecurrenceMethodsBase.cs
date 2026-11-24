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

    internal abstract class EventRecurrenceMethodsBase : MethodsBase, IEventRecurrenceMethods
    {
        protected EventRecurrenceMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task CreateAsync(EventRecurrence r, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(r);
            RequireText(r.Id, nameof(r.Id));
            List<string> statements = new List<string>
            {
                "INSERT INTO eventrecurrences (propertyid, id, frequency, intervalcount, occurrencecount, untillocaldate, monthday, setposition, timezone, startlocal, durationminutes, isallday, " +
                    "materializedthroughutc, materializedcount, icssequence, templateeventid, createdbyuserid, deletedutc, createdutc, updatedutc) VALUES (" + Pid + ", " + D.Str(r.Id) + ", " +
                    D.Enum(r.Frequency) + ", " + D.Num(r.Interval) + ", " + D.Num(r.Count) + ", " + D.Str(r.UntilLocalDate) + ", " + D.Num(r.MonthDay) + ", " + D.Num(r.SetPosition) + ", " +
                    D.Str(r.TimeZone) + ", " + D.Str(r.StartLocal) + ", " + D.Num(r.DurationMinutes) + ", " + D.Bool(r.IsAllDay) + ", " + D.Ts(r.MaterializedThroughUtc) + ", " +
                    D.Num(r.MaterializedCount) + ", " + D.Num(r.IcsSequence) + ", " + D.Str(r.TemplateEventId) + ", " + D.Str(r.CreatedByUserId) + ", NULL, " + D.Ts(r.CreatedUtc) + ", " + D.Ts(r.UpdatedUtc) + ");"
            };
            statements.AddRange(DayStatements(r));
            await TransactionAsync(statements, token).ConfigureAwait(false);
        }

        public async Task<EventRecurrence?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) return null;
            Dictionary<string, EventRecurrence> rows = await ReadManyAsync(new[] { id.Trim() }, token).ConfigureAwait(false);
            return rows.TryGetValue(id.Trim(), out EventRecurrence? r) ? r : null;
        }

        public async Task<Dictionary<string, EventRecurrence>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            List<string> list = ids.Where(i => !String.IsNullOrWhiteSpace(i)).Distinct(StringComparer.Ordinal).ToList();
            Dictionary<string, EventRecurrence> result = new Dictionary<string, EventRecurrence>(StringComparer.Ordinal);
            if (list.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT * FROM eventrecurrences WHERE " + Scope() + " AND id IN " + D.InList(list) + " AND deletedutc IS NULL;", token).ConfigureAwait(false);
            foreach (EventRecurrence r in table.Rows.Cast<DataRow>().Select(Map)) result[r.Id] = r;
            await FillDaysAsync(result, token).ConfigureAwait(false);
            return result;
        }

        public async Task UpdateAsync(EventRecurrence r, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(r);
            RequireText(r.Id, nameof(r.Id));
            List<string> statements = new List<string>
            {
                "UPDATE eventrecurrences SET frequency = " + D.Enum(r.Frequency) + ", intervalcount = " + D.Num(r.Interval) + ", occurrencecount = " + D.Num(r.Count) + ", untillocaldate = " +
                    D.Str(r.UntilLocalDate) + ", monthday = " + D.Num(r.MonthDay) + ", setposition = " + D.Num(r.SetPosition) + ", timezone = " + D.Str(r.TimeZone) + ", startlocal = " + D.Str(r.StartLocal) +
                    ", durationminutes = " + D.Num(r.DurationMinutes) + ", isallday = " + D.Bool(r.IsAllDay) + ", materializedthroughutc = " + D.Ts(r.MaterializedThroughUtc) + ", materializedcount = " +
                    D.Num(r.MaterializedCount) + ", icssequence = " + D.Num(r.IcsSequence) + ", templateeventid = " + D.Str(r.TemplateEventId) + ", updatedutc = " + D.Ts(r.UpdatedUtc) +
                    " WHERE " + Scope() + " AND id = " + D.Str(r.Id) + ";",
                "DELETE FROM eventrecurrencedays WHERE " + Scope() + " AND recurrenceid = " + D.Str(r.Id) + ";"
            };
            statements.AddRange(DayStatements(r));
            await TransactionAsync(statements, token).ConfigureAwait(false);
        }

        public async Task DeleteAsync(string id, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            await QueryAsync("UPDATE eventrecurrences SET deletedutc = " + D.Ts(nowUtc) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
        }

        public async Task<List<EventRecurrence>> ListBehindHorizonAsync(DateTime beforeUtc, int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT * FROM eventrecurrences WHERE " + Scope() + " AND deletedutc IS NULL AND (materializedthroughutc IS NULL OR materializedthroughutc < " + D.Ts(beforeUtc) +
                ") ORDER BY materializedthroughutc NULLS FIRST, id LIMIT " + D.Num(Math.Clamp(limit, 1, 1000)) + ";", token).ConfigureAwait(false);
            Dictionary<string, EventRecurrence> result = new Dictionary<string, EventRecurrence>(StringComparer.Ordinal);
            List<EventRecurrence> ordered = table.Rows.Cast<DataRow>().Select(Map).ToList();
            foreach (EventRecurrence r in ordered) result[r.Id] = r;
            await FillDaysAsync(result, token).ConfigureAwait(false);
            return ordered;
        }

        public async Task<List<EventRecurrenceException>> ListExceptionsAsync(string recurrenceId, CancellationToken token = default)
        {
            RequireText(recurrenceId, nameof(recurrenceId));
            DataTable table = await QueryAsync("SELECT * FROM eventrecurrenceexceptions WHERE " + Scope() + " AND recurrenceid = " + D.Str(recurrenceId) + " ORDER BY originalstartutc;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(row => new EventRecurrenceException
            {
                Id = RowReader.GetString(row, "id"),
                RecurrenceId = RowReader.GetString(row, "recurrenceid"),
                OriginalStartUtc = RowReader.GetDateTime(row, "originalstartutc"),
                Kind = RowReader.GetEnum(row, "kind", EventRecurrenceExceptionKindEnum.Edited),
                EventId = RowReader.GetNullableString(row, "eventid"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            }).ToList();
        }

        public async Task UpsertExceptionAsync(EventRecurrenceException x, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(x);
            RequireText(x.RecurrenceId, nameof(x.RecurrenceId));
            await QueryAsync("INSERT INTO eventrecurrenceexceptions (propertyid, id, recurrenceid, originalstartutc, kind, eventid, createdutc) VALUES (" + Pid + ", " + D.Str(x.Id) + ", " + D.Str(x.RecurrenceId) + ", " +
                D.Ts(x.OriginalStartUtc) + ", " + D.Enum(x.Kind) + ", " + D.Str(x.EventId) + ", " + D.Ts(x.CreatedUtc) + ") ON CONFLICT (propertyid, recurrenceid, originalstartutc) DO UPDATE SET kind = excluded.kind, " +
                "eventid = COALESCE(excluded.eventid, eventrecurrenceexceptions.eventid);", token).ConfigureAwait(false);
        }

        public async Task MoveAsync(string fromRecurrenceId, string toRecurrenceId, IEnumerable<string> eventIds, DateTime sinceOriginalUtc, CancellationToken token = default)
        {
            RequireText(fromRecurrenceId, nameof(fromRecurrenceId));
            RequireText(toRecurrenceId, nameof(toRecurrenceId));
            ArgumentNullException.ThrowIfNull(eventIds);
            List<string> ids = eventIds.Where(i => !String.IsNullOrWhiteSpace(i)).Distinct(StringComparer.Ordinal).ToList();
            List<string> statements = new List<string>
            {
                "UPDATE eventrecurrenceexceptions SET recurrenceid = " + D.Str(toRecurrenceId) + " WHERE " + Scope() + " AND recurrenceid = " + D.Str(fromRecurrenceId) + " AND originalstartutc >= " + D.Ts(sinceOriginalUtc) + ";"
            };
            if (ids.Count > 0)
                statements.Add("UPDATE events SET recurrenceid = " + D.Str(toRecurrenceId) + " WHERE " + Scope() + " AND recurrenceid = " + D.Str(fromRecurrenceId) + " AND id IN " + D.InList(ids) + ";");
            await TransactionAsync(statements, token).ConfigureAwait(false);
        }

        private List<string> DayStatements(EventRecurrence r)
        {
            List<DayOfWeek> days = r.Weekdays.Distinct().ToList();
            if (days.Count == 0) return new List<string>();
            return new List<string>
            {
                "INSERT INTO eventrecurrencedays (propertyid, recurrenceid, weekday) VALUES " + String.Join(", ", days.Select(d => "(" + Pid + ", " + D.Str(r.Id) + ", " + D.Str(d.ToString()) + ")")) + " ON CONFLICT DO NOTHING;"
            };
        }

        private async Task FillDaysAsync(Dictionary<string, EventRecurrence> rows, CancellationToken token)
        {
            if (rows.Count == 0) return;
            DataTable table = await QueryAsync("SELECT recurrenceid, weekday FROM eventrecurrencedays WHERE " + Scope() + " AND recurrenceid IN " + D.InList(rows.Keys) + ";", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows)
            {
                if (!rows.TryGetValue(RowReader.GetString(row, "recurrenceid"), out EventRecurrence? r)) continue;
                if (Enum.TryParse(RowReader.GetString(row, "weekday"), out DayOfWeek day)) r.Weekdays.Add(day);
            }
            foreach (EventRecurrence r in rows.Values) r.Weekdays = r.Weekdays.Distinct().OrderBy(d => ((int)d + 6) % 7).ToList();
        }

        private static EventRecurrence Map(DataRow row)
        {
            return new EventRecurrence
            {
                Id = RowReader.GetString(row, "id"),
                Frequency = RowReader.GetEnum(row, "frequency", EventRecurrenceFrequencyEnum.Weekly),
                Interval = RowReader.GetInt(row, "intervalcount"),
                Count = RowReader.GetNullableInt(row, "occurrencecount"),
                UntilLocalDate = RowReader.GetNullableString(row, "untillocaldate"),
                MonthDay = RowReader.GetNullableInt(row, "monthday"),
                SetPosition = RowReader.GetNullableInt(row, "setposition"),
                TimeZone = RowReader.GetString(row, "timezone"),
                StartLocal = RowReader.GetString(row, "startlocal"),
                DurationMinutes = RowReader.GetInt(row, "durationminutes"),
                IsAllDay = RowReader.GetBool(row, "isallday"),
                MaterializedThroughUtc = RowReader.GetNullableDateTime(row, "materializedthroughutc"),
                MaterializedCount = RowReader.GetInt(row, "materializedcount"),
                IcsSequence = RowReader.GetInt(row, "icssequence"),
                TemplateEventId = RowReader.GetNullableString(row, "templateeventid"),
                CreatedByUserId = RowReader.GetNullableString(row, "createdbyuserid"),
                DeletedUtc = RowReader.GetNullableDateTime(row, "deletedutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }
    }
}
