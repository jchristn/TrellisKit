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
    /// Shared notification preference and notification hours methods.
    /// </summary>
    internal abstract class NotificationPreferenceMethodsBase : MethodsBase, INotificationPreferenceMethods
    {
        protected NotificationPreferenceMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<List<NotificationPreference>> EnumerateAsync(IEnumerable<string> userIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(userIds);
            List<string> ids = userIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return new List<NotificationPreference>();
            DataTable table = await QueryAsync("SELECT userid, category, inapp, push, email, updatedutc FROM notificationpreferences WHERE " + Scope() + " AND userid IN " + D.InList(ids) + ";", token).ConfigureAwait(false);
            List<NotificationPreference> result = new List<NotificationPreference>();
            foreach (DataRow row in table.Rows)
            {
                NotificationCategoryEnum? category = RowReader.GetNullableEnum<NotificationCategoryEnum>(row, "category");
                if (category == null) continue;
                result.Add(new NotificationPreference
                {
                    UserId = RowReader.GetString(row, "userid"),
                    Category = category.Value,
                    InApp = RowReader.GetBool(row, "inapp"),
                    Push = RowReader.GetBool(row, "push"),
                    Email = RowReader.GetBool(row, "email"),
                    UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
                });
            }
            return result;
        }

        public async Task UpsertAsync(List<NotificationPreference> preferences, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(preferences);
            if (preferences.Count == 0) return;
            List<string> statements = preferences.Select(p =>
                "INSERT INTO notificationpreferences (propertyid, userid, category, inapp, push, email, updatedutc) VALUES (" + Pid + ", " + D.Str(p.UserId) + ", " + D.Enum(p.Category) + ", " +
                D.Bool(p.InApp) + ", " + D.Bool(p.Push) + ", " + D.Bool(p.Email) + ", " + D.Ts(p.UpdatedUtc) + ") ON CONFLICT (userid, category) DO UPDATE SET " +
                "inapp = excluded.inapp, push = excluded.push, email = excluded.email, updatedutc = excluded.updatedutc;").ToList();
            await TransactionAsync(statements, token).ConfigureAwait(false);
        }

        public async Task<List<NotificationHours>> EnumerateHoursAsync(IEnumerable<string> userIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(userIds);
            List<string> ids = userIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return new List<NotificationHours>();
            DataTable table = await QueryAsync("SELECT userid, enabled, startminute, endminute, timezone, pushpaused, updatedutc FROM notificationhours WHERE " + Scope() + " AND userid IN " + D.InList(ids) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(row => new NotificationHours
            {
                UserId = RowReader.GetString(row, "userid"),
                Enabled = RowReader.GetBool(row, "enabled"),
                StartMinute = RowReader.GetInt(row, "startminute"),
                EndMinute = RowReader.GetInt(row, "endminute"),
                TimeZone = RowReader.GetNullableString(row, "timezone"),
                PushPaused = RowReader.GetBool(row, "pushpaused"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            }).ToList();
        }

        public async Task UpsertHoursAsync(NotificationHours hours, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(hours);
            RequireText(hours.UserId, nameof(hours.UserId));
            await QueryAsync("INSERT INTO notificationhours (propertyid, userid, enabled, startminute, endminute, timezone, pushpaused, updatedutc) VALUES (" + Pid + ", " + D.Str(hours.UserId) + ", " +
                D.Bool(hours.Enabled) + ", " + D.Num(hours.StartMinute) + ", " + D.Num(hours.EndMinute) + ", " + D.Str(hours.TimeZone) + ", " + D.Bool(hours.PushPaused) + ", " +
                D.Ts(hours.UpdatedUtc) + ") ON CONFLICT (userid) DO UPDATE SET enabled = excluded.enabled, startminute = excluded.startminute, endminute = excluded.endminute, " +
                "timezone = excluded.timezone, pushpaused = excluded.pushpaused, updatedutc = excluded.updatedutc;", token).ConfigureAwait(false);
        }

        public async Task DeleteByUserAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            await TransactionAsync(new List<string>
            {
                "DELETE FROM notificationpreferences WHERE " + Scope() + " AND userid = " + D.Str(userId) + ";",
                "DELETE FROM notificationhours WHERE " + Scope() + " AND userid = " + D.Str(userId) + ";",
                "DELETE FROM forumemaildigests WHERE " + Scope() + " AND userid = " + D.Str(userId) + ";"
            }, token).ConfigureAwait(false);
        }

        public async Task<List<EmailDigestCandidate>> ListDigestsDueAsync(IReadOnlyList<NotificationCategoryEnum> categories, DateTime checkedBeforeUtc, int limit, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(categories);
            if (categories.Count == 0) return new List<EmailDigestCandidate>();
            DataTable table = await QueryAsync("SELECT p.userid, p.category, u.email, u.displayname, d.lastcheckedutc, d.lastsentutc FROM notificationpreferences p " +
                "JOIN users u ON " + Scope("u") + " AND u.id = p.userid " +
                "LEFT JOIN forumemaildigests d ON " + Scope("d") + " AND d.userid = p.userid AND d.category = p.category " +
                "WHERE " + Scope("p") + " AND p.email = " + D.Bool(true) + " AND p.category IN " + D.InList(categories.Select(c => c.ToString())) +
                " AND u.isdeleted = " + D.Bool(false) + " AND u.isbanned = " + D.Bool(false) + " AND u.emailverified = " + D.Bool(true) + " AND u.email IS NOT NULL" +
                " AND (d.lastcheckedutc IS NULL OR d.lastcheckedutc < " + D.Ts(checkedBeforeUtc) + ")" +
                " ORDER BY d.lastcheckedutc NULLS FIRST, p.userid, p.category LIMIT " + D.Num(Math.Clamp(limit, 1, 1000)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(row => new EmailDigestCandidate
            {
                UserId = RowReader.GetString(row, "userid"),
                Category = RowReader.GetEnum(row, "category", NotificationCategoryEnum.Following),
                Email = RowReader.GetString(row, "email"),
                DisplayName = RowReader.GetString(row, "displayname"),
                LastCheckedUtc = RowReader.GetNullableDateTime(row, "lastcheckedutc"),
                LastSentUtc = RowReader.GetNullableDateTime(row, "lastsentutc")
            }).ToList();
        }

        public Task MarkDigestAsync(string userId, NotificationCategoryEnum category, DateTime checkedUtc, DateTime? sentUtc, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            return QueryAsync("INSERT INTO forumemaildigests (propertyid, userid, category, lastsentutc, lastcheckedutc) VALUES (" + Pid + ", " + D.Str(userId) + ", " + D.Enum(category) + ", " +
                D.Ts(sentUtc) + ", " + D.Ts(checkedUtc) + ") ON CONFLICT (propertyid, userid, category) DO UPDATE SET lastcheckedutc = excluded.lastcheckedutc, " +
                "lastsentutc = COALESCE(excluded.lastsentutc, forumemaildigests.lastsentutc);", token);
        }
    }
}
