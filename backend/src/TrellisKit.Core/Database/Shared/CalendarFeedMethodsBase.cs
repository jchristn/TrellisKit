namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Data;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Models;

    internal abstract class CalendarFeedMethodsBase : MethodsBase, ICalendarFeedMethods
    {
        protected CalendarFeedMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<CalendarFeed?> ReadForUserAsync(string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) return null;
            DataTable table = await QueryAsync("SELECT * FROM calendarfeeds WHERE " + Scope() + " AND userid = " + D.Str(userId) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<CalendarFeed?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            DataTable table = await QueryAsync("SELECT * FROM calendarfeeds WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<CalendarFeed> UpsertAsync(CalendarFeed feed, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(feed);
            RequireText(feed.Id, nameof(feed.Id));
            RequireText(feed.UserId, nameof(feed.UserId));
            DataTable table = await QueryAsync("INSERT INTO calendarfeeds (propertyid, id, userid, secrethash, isenabled, lastfetchedutc, createdutc, rotatedutc) VALUES (" + Pid + ", " + D.Str(feed.Id) + ", " +
                D.Str(feed.UserId) + ", " + D.Str(feed.SecretHash) + ", TRUE, NULL, " + D.Ts(feed.CreatedUtc) + ", " + D.Ts(feed.RotatedUtc) + ") ON CONFLICT (propertyid, userid) DO UPDATE SET id = excluded.id, " +
                "secrethash = excluded.secrethash, isenabled = TRUE, rotatedutc = excluded.rotatedutc RETURNING *;", token).ConfigureAwait(false);
            return Map(table.Rows[0]);
        }

        public async Task<bool> DeleteForUserAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("DELETE FROM calendarfeeds WHERE " + Scope() + " AND userid = " + D.Str(userId) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> DisableForUserAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("UPDATE calendarfeeds SET isenabled = FALSE WHERE " + Scope() + " AND userid = " + D.Str(userId) + " AND isenabled = TRUE RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task TouchAsync(string id, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            await QueryAsync("UPDATE calendarfeeds SET lastfetchedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
        }

        private static CalendarFeed Map(DataRow row)
        {
            return new CalendarFeed
            {
                Id = RowReader.GetString(row, "id"),
                UserId = RowReader.GetString(row, "userid"),
                SecretHash = RowReader.GetString(row, "secrethash"),
                IsEnabled = RowReader.GetBool(row, "isenabled"),
                LastFetchedUtc = RowReader.GetNullableDateTime(row, "lastfetchedutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                RotatedUtc = RowReader.GetNullableDateTime(row, "rotatedutc")
            };
        }
    }
}
