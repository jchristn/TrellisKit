namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    internal abstract class LessonProgressMethodsBase : MethodsBase, ILessonProgressMethods
    {
        private const string _Columns = "id, lessonid, userid, positionseconds, maxpositionseconds, watchedseconds, completed, completedutc, completionsource, lastwatchedutc, clientupdatedutc, createdutc, updatedutc";

        protected LessonProgressMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<LessonProgress?> ReadAsync(string userId, string lessonId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId) || String.IsNullOrEmpty(lessonId)) return null;
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM lessonprogress WHERE " + Scope() + " AND userid = " + D.Str(userId) + " AND lessonid = " + D.Str(lessonId) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<Dictionary<string, LessonProgress>> ReadManyAsync(string userId, IEnumerable<string> lessonIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(lessonIds);
            Dictionary<string, LessonProgress> result = new Dictionary<string, LessonProgress>(StringComparer.Ordinal);
            List<string> ids = lessonIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (String.IsNullOrEmpty(userId) || ids.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM lessonprogress WHERE " + Scope() + " AND userid = " + D.Str(userId) + " AND lessonid IN " + D.InList(ids) + ";", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows)
            {
                LessonProgress p = Map(row);
                result[p.LessonId] = p;
            }
            return result;
        }

        public async Task<LessonProgress> MergeAsync(LessonProgressMerge merge, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(merge);
            RequireText(merge.UserId, nameof(merge.UserId));
            RequireText(merge.LessonId, nameof(merge.LessonId));
            double duration = Math.Max(0, merge.DurationSeconds);
            double cap = duration > 0 ? 3 * duration : Math.Max(0, merge.PlayedSeconds);
            double watched = Math.Min(Math.Max(0, merge.PlayedSeconds), cap);
            bool reached = duration > 0 && Math.Round(100.0 * Math.Min(merge.ReachedSeconds, watched) / duration) >= merge.ThresholdPercent;
            string now = D.Ts(merge.NowUtc);
            string stale = "(excluded.clientupdatedutc IS NOT NULL AND lessonprogress.clientupdatedutc IS NOT NULL AND excluded.clientupdatedutc <= lessonprogress.clientupdatedutc)";
            string replay = "(excluded.clientupdatedutc IS NOT NULL AND excluded.clientupdatedutc = lessonprogress.clientupdatedutc)";
            string newWatched = "(CASE WHEN " + replay + " THEN lessonprogress.watchedseconds ELSE LEAST(lessonprogress.watchedseconds + excluded.watchedseconds, " + D.Num(cap) + ") END)";
            string reach = "(" + D.Num(duration) + " > 0 AND ROUND(100.0 * LEAST(GREATEST(lessonprogress.maxpositionseconds, excluded.maxpositionseconds), " + newWatched + ") / " +
                D.Num(Math.Max(duration, 1)) + ") >= " + D.Num(merge.ThresholdPercent) + ")";
            string sql = "INSERT INTO lessonprogress (propertyid, " + _Columns + ") VALUES (" + Pid + ", " + D.Str(IdGenerator.Generate(LibraryIdPrefixes.Progress)) + ", " + D.Str(merge.LessonId) + ", " +
                D.Str(merge.UserId) + ", " + D.Num(merge.PositionSeconds) + ", " + D.Num(merge.ReachedSeconds) + ", " + D.Num(watched) + ", " + D.Bool(reached) + ", " + (reached ? now : "NULL") + ", " +
                (reached ? D.Enum(LessonCompletionSourceEnum.Threshold) : "NULL") + ", " + D.Ts(merge.ClientUpdatedUtc ?? merge.NowUtc) + ", " + D.Ts(merge.ClientUpdatedUtc) + ", " + now + ", " + now + ")" +
                " ON CONFLICT (propertyid, userid, lessonid) DO UPDATE SET" +
                " positionseconds = CASE WHEN " + stale + " THEN lessonprogress.positionseconds ELSE excluded.positionseconds END," +
                " maxpositionseconds = GREATEST(lessonprogress.maxpositionseconds, excluded.maxpositionseconds)," +
                " watchedseconds = " + newWatched + "," +
                " completed = (lessonprogress.completed OR " + reach + ")," +
                " completedutc = CASE WHEN lessonprogress.completed THEN lessonprogress.completedutc WHEN " + reach + " THEN " + now + " ELSE NULL END," +
                " completionsource = CASE WHEN lessonprogress.completed THEN lessonprogress.completionsource WHEN " + reach + " THEN " + D.Enum(LessonCompletionSourceEnum.Threshold) + " ELSE NULL END," +
                " lastwatchedutc = CASE WHEN " + stale + " THEN lessonprogress.lastwatchedutc ELSE excluded.lastwatchedutc END," +
                " clientupdatedutc = CASE WHEN " + stale + " THEN lessonprogress.clientupdatedutc ELSE COALESCE(excluded.clientupdatedutc, lessonprogress.clientupdatedutc) END," +
                " updatedutc = " + now + " RETURNING " + _Columns + ";";
            DataTable table = await QueryAsync(sql, token).ConfigureAwait(false);
            return Map(table.Rows[0]);
        }

        public async Task<LessonProgress> SetCompletedAsync(string userId, string lessonId, bool completed, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            RequireText(lessonId, nameof(lessonId));
            string now = D.Ts(nowUtc);
            string sql = "INSERT INTO lessonprogress (propertyid, " + _Columns + ") VALUES (" + Pid + ", " + D.Str(IdGenerator.Generate(LibraryIdPrefixes.Progress)) + ", " + D.Str(lessonId) + ", " + D.Str(userId) +
                ", 0, 0, 0, " + D.Bool(completed) + ", " + (completed ? now : "NULL") + ", " + (completed ? D.Enum(LessonCompletionSourceEnum.Manual) : "NULL") + ", " + now + ", NULL, " + now + ", " + now + ")" +
                " ON CONFLICT (propertyid, userid, lessonid) DO UPDATE SET " +
                (completed
                    ? "completed = " + D.Bool(true) + ", completedutc = COALESCE(lessonprogress.completedutc, " + now + "), completionsource = COALESCE(lessonprogress.completionsource, " + D.Enum(LessonCompletionSourceEnum.Manual) + ")"
                    : "completed = " + D.Bool(false) + ", completedutc = NULL, completionsource = NULL") +
                ", updatedutc = " + now + " RETURNING " + _Columns + ";";
            DataTable table = await QueryAsync(sql, token).ConfigureAwait(false);
            return Map(table.Rows[0]);
        }

        public async Task<bool> DeleteAsync(string userId, string lessonId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            RequireText(lessonId, nameof(lessonId));
            DataTable table = await QueryAsync("DELETE FROM lessonprogress WHERE " + Scope() + " AND userid = " + D.Str(userId) + " AND lessonid = " + D.Str(lessonId) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<Dictionary<string, SeriesProgressSummary>> SeriesProgressAsync(string userId, IEnumerable<string> seriesIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(seriesIds);
            Dictionary<string, SeriesProgressSummary> result = new Dictionary<string, SeriesProgressSummary>(StringComparer.Ordinal);
            List<string> ids = seriesIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (String.IsNullOrEmpty(userId) || ids.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT l.seriesid, COUNT(*) FILTER (WHERE p.completed) AS done, COUNT(*) AS started, (array_agg(p.lessonid ORDER BY p.lastwatchedutc DESC))[1] AS lastlesson, " +
                "MAX(p.lastwatchedutc) AS lastwatched FROM lessonprogress p JOIN lessons l ON " + Scope("l") + " AND l.id = p.lessonid WHERE " + Scope("p") + " AND p.userid = " + D.Str(userId) +
                " AND l.seriesid IN " + D.InList(ids) + " AND l.deletedutc IS NULL AND l.status = " + D.Enum(LibraryStatusEnum.Published) + " AND l.moderationstate = " + D.Enum(ModerationStateEnum.None) +
                " GROUP BY l.seriesid;", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows)
            {
                SeriesProgressSummary s = new SeriesProgressSummary
                {
                    SeriesId = RowReader.GetString(row, "seriesid"),
                    CompletedLessons = RowReader.GetInt(row, "done"),
                    StartedLessons = RowReader.GetInt(row, "started"),
                    LastLessonId = RowReader.GetNullableString(row, "lastlesson"),
                    LastWatchedUtc = RowReader.GetNullableDateTime(row, "lastwatched")
                };
                result[s.SeriesId] = s;
            }
            return result;
        }

        public async Task<EnumerationResult<LessonProgress>> EnumerateAsync(EnumerationRequest request, LessonProgressFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = new List<string>();
            if (!String.IsNullOrEmpty(filter.UserId)) conditions.Add("p.userid = " + D.Str(filter.UserId));
            if (!String.IsNullOrEmpty(filter.LessonId)) conditions.Add("p.lessonid = " + D.Str(filter.LessonId));
            if (filter.Completed != null) conditions.Add("p.completed = " + D.Bool(filter.Completed.Value));
            if (!String.IsNullOrEmpty(filter.SeriesId))
                conditions.Add("EXISTS (SELECT 1 FROM lessons sl WHERE " + Scope("sl") + " AND sl.id = p.lessonid AND sl.seriesid = " + D.Str(filter.SeriesId) + ")");
            if (filter.InProgressOnly)
            {
                string cutoff = D.Ts(filter.ArchivedCutoffUtc ?? DateTime.UtcNow.AddYears(-1));
                conditions.Add("p.completed = " + D.Bool(false));
                conditions.Add("EXISTS (SELECT 1 FROM lessons ol JOIN series os ON " + Scope("os") + " AND os.id = ol.seriesid WHERE " + Scope("ol") + " AND ol.id = p.lessonid AND ol.deletedutc IS NULL AND ol.moderationstate = " +
                    D.Enum(ModerationStateEnum.None) + " AND os.deletedutc IS NULL AND (ol.status = " + D.Enum(LibraryStatusEnum.Published) + " OR (ol.status = " + D.Enum(LibraryStatusEnum.Archived) +
                    " AND ol.archivedutc >= " + cutoff + ")) AND (os.status = " + D.Enum(LibraryStatusEnum.Published) + " OR (os.status = " + D.Enum(LibraryStatusEnum.Archived) + " AND os.archivedutc >= " + cutoff + ")))");
            }
            return await EnumerateAsync(request, "p." + _Columns.Replace(", ", ", p."), "lessonprogress p", conditions, Map, token).ConfigureAwait(false);
        }

        public async Task<bool> RecordPlayAsync(LessonPlay play, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(play);
            RequireText(play.LessonId, nameof(play.LessonId));
            RequireText(play.SessionId, nameof(play.SessionId));
            DataTable table = await QueryAsync("INSERT INTO lessonplays (propertyid, id, lessonid, userid, sessionid, platform, playedseconds, startedutc, lastutc) VALUES (" + Pid + ", " + D.Str(play.Id) + ", " +
                D.Str(play.LessonId) + ", " + D.Str(play.UserId) + ", " + D.Str(play.SessionId) + ", " + D.Str(play.Platform) + ", " + D.Num(Math.Max(0, play.PlayedSeconds)) + ", " + D.Ts(play.StartedUtc) + ", " +
                D.Ts(play.LastUtc) + ") ON CONFLICT (propertyid, lessonid, sessionid) DO UPDATE SET playedseconds = lessonplays.playedseconds + excluded.playedseconds, lastutc = excluded.lastutc " +
                "RETURNING (xmax = 0) AS inserted;", token).ConfigureAwait(false);
            return table.Rows.Count > 0 && RowReader.GetBool(table.Rows[0], "inserted");
        }

        public async Task<Dictionary<string, LessonPlayStats>> PlayStatsAsync(string? userId, IEnumerable<string> lessonIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(lessonIds);
            Dictionary<string, LessonPlayStats> result = new Dictionary<string, LessonPlayStats>(StringComparer.Ordinal);
            List<string> ids = lessonIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT lessonid, COUNT(*) AS plays, COALESCE(SUM(playedseconds), 0) AS played FROM lessonplays WHERE " + Scope() + " AND lessonid IN " + D.InList(ids) +
                (userId == null ? String.Empty : " AND userid = " + D.Str(userId)) + " GROUP BY lessonid;", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows)
            {
                LessonPlayStats s = new LessonPlayStats { LessonId = RowReader.GetString(row, "lessonid"), UserId = userId, Plays = RowReader.GetLong(row, "plays"), PlayedSeconds = RowReader.GetDouble(row, "played") };
                result[s.LessonId] = s;
            }
            return result;
        }

        public async Task<Dictionary<string, LessonPlayStats>> PlayStatsByUserAsync(string lessonId, IEnumerable<string> userIds, CancellationToken token = default)
        {
            RequireText(lessonId, nameof(lessonId));
            ArgumentNullException.ThrowIfNull(userIds);
            Dictionary<string, LessonPlayStats> result = new Dictionary<string, LessonPlayStats>(StringComparer.Ordinal);
            List<string> ids = userIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT userid, COUNT(*) AS plays, COALESCE(SUM(playedseconds), 0) AS played FROM lessonplays WHERE " + Scope() + " AND lessonid = " + D.Str(lessonId) +
                " AND userid IN " + D.InList(ids) + " GROUP BY userid;", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows)
            {
                LessonPlayStats s = new LessonPlayStats { LessonId = lessonId, UserId = RowReader.GetString(row, "userid"), Plays = RowReader.GetLong(row, "plays"), PlayedSeconds = RowReader.GetDouble(row, "played") };
                result[s.UserId!] = s;
            }
            return result;
        }

        public async Task FollowAsync(SeriesFollow follow, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(follow);
            await QueryAsync(FollowInsert(follow) + " ON CONFLICT (propertyid, seriesid, userid) DO UPDATE SET isoptedout = " + D.Bool(false) + ", source = " + D.Enum(SeriesFollowSourceEnum.Explicit) + ";", token).ConfigureAwait(false);
        }

        public async Task<bool> AutoFollowAsync(SeriesFollow follow, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(follow);
            DataTable table = await QueryAsync(FollowInsert(follow) + " ON CONFLICT (propertyid, seriesid, userid) DO NOTHING RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task UnfollowAsync(string seriesId, string userId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(seriesId, nameof(seriesId));
            RequireText(userId, nameof(userId));
            await QueryAsync(FollowInsert(new SeriesFollow { Id = IdGenerator.Generate(LibraryIdPrefixes.Follow), SeriesId = seriesId, UserId = userId, IsOptedOut = true, CreatedUtc = nowUtc }) +
                " ON CONFLICT (propertyid, seriesid, userid) DO UPDATE SET isoptedout = " + D.Bool(true) + ";", token).ConfigureAwait(false);
        }

        public async Task<HashSet<string>> FollowedAmongAsync(string userId, IEnumerable<string> seriesIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(seriesIds);
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            List<string> ids = seriesIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (String.IsNullOrEmpty(userId) || ids.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT seriesid FROM seriesfollows WHERE " + Scope() + " AND userid = " + D.Str(userId) + " AND isoptedout = " + D.Bool(false) + " AND seriesid IN " + D.InList(ids) + ";", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows) result.Add(RowReader.GetString(row, "seriesid"));
            return result;
        }

        public async Task<List<string>> FollowerIdsAsync(string seriesId, string? afterUserId, int limit, CancellationToken token = default)
        {
            RequireText(seriesId, nameof(seriesId));
            DataTable table = await QueryAsync("SELECT userid FROM seriesfollows WHERE " + Scope() + " AND seriesid = " + D.Str(seriesId) + " AND isoptedout = " + D.Bool(false) +
                (afterUserId == null ? String.Empty : " AND userid > " + D.Str(afterUserId)) + " ORDER BY userid LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "userid")).ToList();
        }

        public Task<long> CountFollowsAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            return CountAsync("SELECT COUNT(*) AS cnt FROM seriesfollows WHERE " + Scope() + " AND userid = " + D.Str(userId) + " AND isoptedout = " + D.Bool(false) + ";", token);
        }

        public async Task<List<SeriesFollow>> ListFollowsForUserAsync(string userId, string? afterId, int limit, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("SELECT * FROM seriesfollows WHERE " + Scope() + " AND userid = " + D.Str(userId) + (afterId == null ? String.Empty : " AND id > " + D.Str(afterId)) +
                " ORDER BY id LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => new SeriesFollow
            {
                Id = RowReader.GetString(r, "id"),
                SeriesId = RowReader.GetString(r, "seriesid"),
                UserId = RowReader.GetString(r, "userid"),
                Source = RowReader.GetEnum(r, "source", SeriesFollowSourceEnum.Explicit),
                IsOptedOut = RowReader.GetBool(r, "isoptedout"),
                CreatedUtc = RowReader.GetDateTime(r, "createdutc")
            }).ToList();
        }

        public async Task<List<LessonProgress>> ListForUserAsync(string userId, string? afterId, int limit, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM lessonprogress WHERE " + Scope() + " AND userid = " + D.Str(userId) + (afterId == null ? String.Empty : " AND id > " + D.Str(afterId)) +
                " ORDER BY id LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<int> DeleteForUserAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            string user = D.Str(userId);
            List<string> statements = new List<string>
            {
                "CREATE TEMP TABLE IF NOT EXISTS lessonerase (n INTEGER) ON COMMIT DROP;",
                "WITH d AS (DELETE FROM lessonprogress WHERE " + Scope() + " AND userid = " + user + " RETURNING 1) INSERT INTO lessonerase (n) SELECT COUNT(*) FROM d;",
                "WITH d AS (DELETE FROM seriesfollows WHERE " + Scope() + " AND userid = " + user + " RETURNING 1) INSERT INTO lessonerase (n) SELECT COUNT(*) FROM d;",
                "UPDATE lessonplays SET userid = NULL WHERE " + Scope() + " AND userid = " + user + ";",
                "SELECT COALESCE(SUM(n), 0) AS cnt FROM lessonerase;"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? 0 : (int)RowReader.GetLong(table.Rows[0], "cnt");
        }

        public async Task<List<DailyCount>> DailyAsync(bool completions, DateTime fromUtc, int days, CancellationToken token = default)
        {
            string from = D.Ts(fromUtc);
            string sql = completions
                ? "SELECT " + D.DayOf("completedutc") + " AS day, COUNT(*) AS cnt FROM lessonprogress WHERE " + Scope() + " AND completed = " + D.Bool(true) + " AND completedutc >= " + from + " GROUP BY 1;"
                : "SELECT " + D.DayOf("startedutc") + " AS day, COUNT(*) AS cnt FROM lessonplays WHERE " + Scope() + " AND startedutc >= " + from + " GROUP BY 1;";
            DataTable rows = await QueryAsync(sql, token).ConfigureAwait(false);
            Dictionary<string, long> counts = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (DataRow row in rows.Rows) counts[RowReader.GetString(row, "day")] = RowReader.GetLong(row, "cnt");
            List<DailyCount> result = new List<DailyCount>();
            for (int i = 0; i < Math.Max(1, days); i++)
            {
                string key = fromUtc.Date.AddDays(i).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                result.Add(new DailyCount { Date = key, Count = counts.TryGetValue(key, out long c) ? c : 0 });
            }
            return result;
        }

        public Task<long> ActiveLearnersAsync(DateTime fromUtc, CancellationToken token = default)
        {
            return CountAsync("SELECT COUNT(DISTINCT userid) AS cnt FROM lessonplays WHERE " + Scope() + " AND userid IS NOT NULL AND startedutc >= " + D.Ts(fromUtc) + ";", token);
        }

        public async Task<List<LessonMetricRow>> TopLessonsAsync(DateTime fromUtc, int limit, CancellationToken token = default)
        {
            string from = D.Ts(fromUtc);
            DataTable table = await QueryAsync("SELECT lp.lessonid, COUNT(*) AS plays, (SELECT COUNT(*) FROM lessonprogress c WHERE " + Scope("c") + " AND c.lessonid = lp.lessonid AND c.completed = " + D.Bool(true) +
                " AND c.completedutc >= " + from + ") AS completions FROM lessonplays lp WHERE " + Scope("lp") + " AND lp.startedutc >= " + from + " GROUP BY lp.lessonid ORDER BY plays DESC, lp.lessonid LIMIT " +
                D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => new LessonMetricRow { LessonId = RowReader.GetString(r, "lessonid"), Plays = RowReader.GetLong(r, "plays"), Completions = RowReader.GetLong(r, "completions") }).ToList();
        }

        public Task<long> CountPlayedLessonsAsync(DateTime fromUtc, CancellationToken token = default)
        {
            return CountAsync("SELECT COUNT(DISTINCT lessonid) AS cnt FROM lessonplays WHERE " + Scope() + " AND startedutc >= " + D.Ts(fromUtc) + ";", token);
        }

        public async Task<List<SeriesCompletionRow>> SeriesCompletionAsync(int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT l.seriesid, COUNT(DISTINCT p.userid) AS learners, COUNT(*) FILTER (WHERE p.completed) AS done, COUNT(*) AS total FROM lessonprogress p JOIN lessons l ON " +
                Scope("l") + " AND l.id = p.lessonid WHERE " + Scope("p") + " AND l.deletedutc IS NULL GROUP BY l.seriesid ORDER BY learners DESC, l.seriesid LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => new SeriesCompletionRow
            {
                SeriesId = RowReader.GetString(r, "seriesid"),
                Learners = RowReader.GetLong(r, "learners"),
                Completions = RowReader.GetLong(r, "done"),
                ProgressRows = RowReader.GetLong(r, "total")
            }).ToList();
        }

        private string FollowInsert(SeriesFollow follow)
        {
            return "INSERT INTO seriesfollows (propertyid, id, seriesid, userid, source, isoptedout, createdutc) VALUES (" + Pid + ", " + D.Str(String.IsNullOrEmpty(follow.Id) ? IdGenerator.Generate(LibraryIdPrefixes.Follow) : follow.Id) +
                ", " + D.Str(follow.SeriesId) + ", " + D.Str(follow.UserId) + ", " + D.Enum(follow.Source) + ", " + D.Bool(follow.IsOptedOut) + ", " + D.Ts(follow.CreatedUtc) + ")";
        }

        private static LessonProgress Map(DataRow row)
        {
            string? source = RowReader.GetNullableString(row, "completionsource");
            return new LessonProgress
            {
                Id = RowReader.GetString(row, "id"),
                LessonId = RowReader.GetString(row, "lessonid"),
                UserId = RowReader.GetString(row, "userid"),
                PositionSeconds = RowReader.GetDouble(row, "positionseconds"),
                MaxPositionSeconds = RowReader.GetDouble(row, "maxpositionseconds"),
                WatchedSeconds = RowReader.GetDouble(row, "watchedseconds"),
                Completed = RowReader.GetBool(row, "completed"),
                CompletedUtc = RowReader.GetNullableDateTime(row, "completedutc"),
                CompletionSource = source == null ? null : RowReader.GetEnum(row, "completionsource", LessonCompletionSourceEnum.Threshold),
                LastWatchedUtc = RowReader.GetDateTime(row, "lastwatchedutc"),
                ClientUpdatedUtc = RowReader.GetNullableDateTime(row, "clientupdatedutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }
    }
}
