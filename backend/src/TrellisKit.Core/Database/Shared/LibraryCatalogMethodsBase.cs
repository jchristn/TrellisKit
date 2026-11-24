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
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    internal abstract class LibraryCatalogMethodsBase : MethodsBase, ILibraryCatalogMethods
    {
        private const string _SeriesColumns = "se.id, se.slug, se.title, se.summary, se.description, se.covermediaid, se.status, se.sortorder, se.audience, se.lessoncount, se.totaldurationseconds, " +
            "se.publishedutc, se.archivedutc, se.deletedutc, se.createdbyuserid, se.createdutc, se.updatedutc";

        private const string _LessonColumns = "l.id, l.seriesid, l.position, l.slug, l.title, l.summary, l.description, l.kind, l.videomediaid, l.durationseconds, l.status, l.publishatutc, l.audience, " +
            "l.downloadable, l.commentsenabled, l.notifystaffoncomment, l.commentcount, l.reactiontotal, l.openreportcount, l.moderationstate, l.moderatedutc, l.moderatedbyuserid, l.moderationreason, " +
            "l.promotedfeedpostid, l.publishedutc, l.archivedutc, l.deletedutc, l.createdbyuserid, l.createdutc, l.updatedutc";

        protected LibraryCatalogMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        #region Config-and-Levels

        public async Task<LibraryConfig?> ReadConfigAsync(CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT * FROM libraryconfig WHERE " + Scope() + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            DataRow row = table.Rows[0];
            return new LibraryConfig
            {
                LevelSource = RowReader.GetEnum(row, "levelsource", LibraryLevelSourceEnum.None),
                LevelFieldKey = RowReader.GetNullableString(row, "levelfieldkey"),
                SourceHash = RowReader.GetNullableString(row, "sourcehash"),
                DownloadsEnabled = row.IsNull("downloadsenabled") ? null : RowReader.GetBool(row, "downloadsenabled"),
                CompletionThresholdPercent = RowReader.GetNullableInt(row, "completionthresholdpercent"),
                PublicSamplesEnabled = row.IsNull("publicsamplesenabled") ? null : RowReader.GetBool(row, "publicsamplesenabled"),
                AutoFollowOnProgress = row.IsNull("autofollowonprogress") ? null : RowReader.GetBool(row, "autofollowonprogress"),
                CommentsEnabledByDefault = row.IsNull("commentsenabledbydefault") ? null : RowReader.GetBool(row, "commentsenabledbydefault"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        public async Task WriteConfigAsync(LibraryConfig config, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(config);
            await QueryAsync("INSERT INTO libraryconfig (propertyid, levelsource, levelfieldkey, sourcehash, downloadsenabled, completionthresholdpercent, publicsamplesenabled, autofollowonprogress, " +
                "commentsenabledbydefault, updatedutc) VALUES (" + Pid + ", " + D.Enum(config.LevelSource) + ", " + D.Str(config.LevelFieldKey) + ", " + D.Str(config.SourceHash) + ", " +
                NullableBool(config.DownloadsEnabled) + ", " + D.Num(config.CompletionThresholdPercent) + ", " + NullableBool(config.PublicSamplesEnabled) + ", " + NullableBool(config.AutoFollowOnProgress) + ", " +
                NullableBool(config.CommentsEnabledByDefault) + ", " + D.Ts(config.UpdatedUtc) + ") ON CONFLICT (propertyid) DO UPDATE SET levelsource = excluded.levelsource, levelfieldkey = excluded.levelfieldkey, " +
                "sourcehash = excluded.sourcehash, downloadsenabled = excluded.downloadsenabled, completionthresholdpercent = excluded.completionthresholdpercent, publicsamplesenabled = excluded.publicsamplesenabled, " +
                "autofollowonprogress = excluded.autofollowonprogress, commentsenabledbydefault = excluded.commentsenabledbydefault, updatedutc = excluded.updatedutc;", token).ConfigureAwait(false);
        }

        private string NullableBool(bool? value)
        {
            return value == null ? "NULL" : D.Bool(value.Value);
        }

        public async Task<List<LibraryLevel>> ListLevelsAsync(bool includeRetired, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT * FROM librarylevels WHERE " + Scope() + (includeRetired ? String.Empty : " AND isretired = " + D.Bool(false)) + " ORDER BY sortorder, id LIMIT 200;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapLevel).ToList();
        }

        public async Task<LibraryLevel?> ReadLevelAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            DataTable table = await QueryAsync("SELECT * FROM librarylevels WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : MapLevel(table.Rows[0]);
        }

        public async Task<LibraryLevel> CreateLevelAsync(LibraryLevel level, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(level);
            await UniqueAsync(LevelInsert(level), "Library level", token).ConfigureAwait(false);
            return level;
        }

        public async Task<bool> UpdateLevelAsync(LibraryLevel level, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(level);
            DataTable table = await QueryAsync("UPDATE librarylevels SET label = " + D.Str(level.Label) + ", color = " + D.Str(level.Color) + ", sortorder = " + D.Num(level.SortOrder) + ", isretired = " +
                D.Bool(level.IsRetired) + ", updatedutc = " + D.Ts(level.UpdatedUtc) + " WHERE " + Scope() + " AND id = " + D.Str(level.Id) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> DeleteLevelAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("DELETE FROM librarylevels WHERE " + Scope() + " AND id = " + D.Str(id) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task ReplaceLevelsAsync(List<LibraryLevel> levels, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(levels);
            List<string> statements = new List<string> { "DELETE FROM librarylevels WHERE " + Scope() + ";" };
            statements.AddRange(levels.Select(LevelInsert));
            await TransactionAsync(statements, token).ConfigureAwait(false);
        }

        #endregion

        #region Series

        public async Task<LibrarySeries> CreateSeriesAsync(LibrarySeries series, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(series);
            RequireText(series.Id, nameof(series.Id));
            List<string> statements = new List<string>
            {
                "DELETE FROM seriesslugs WHERE " + Scope() + " AND lower(slug) = lower(" + D.Str(series.Slug) + ");",
                "INSERT INTO series (propertyid, id, slug, title, summary, description, covermediaid, status, sortorder, audience, lessoncount, totaldurationseconds, publishedutc, archivedutc, deletedutc, " +
                    "createdbyuserid, createdutc, updatedutc) VALUES (" + Pid + ", " + D.Str(series.Id) + ", " + D.Str(series.Slug) + ", " + D.Str(series.Title) + ", " + D.Str(series.Summary) + ", " +
                    D.Str(series.Description) + ", " + D.Str(series.CoverMediaId) + ", " + D.Enum(series.Status) + ", " + D.Num(series.SortOrder) + ", " + D.Enum(series.Audience) + ", 0, 0, " +
                    D.Ts(series.PublishedUtc) + ", NULL, NULL, " + D.Str(series.CreatedByUserId) + ", " + D.Ts(series.CreatedUtc) + ", " + D.Ts(series.UpdatedUtc) + ");"
            };
            statements.AddRange(SeriesLevelStatements(series));
            await UniqueTransactionAsync(statements, "Series slug", token).ConfigureAwait(false);
            return (await ReadSeriesAsync(series.Id, token).ConfigureAwait(false)) ?? series;
        }

        public async Task<LibrarySeries?> ReadSeriesAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            List<LibrarySeries> list = await LoadSeriesAsync("se.id = " + D.Str(id) + " AND se.deletedutc IS NULL", token).ConfigureAwait(false);
            return list.FirstOrDefault();
        }

        public async Task<LibrarySeries?> ReadSeriesBySlugAsync(string slug, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(slug)) return null;
            string s = D.Str(slug.Trim());
            List<LibrarySeries> list = await LoadSeriesAsync("lower(se.slug) = lower(" + s + ") AND se.deletedutc IS NULL", token).ConfigureAwait(false);
            if (list.Count > 0) return list[0];
            list = await LoadSeriesAsync("se.deletedutc IS NULL AND se.id = (SELECT r.seriesid FROM seriesslugs r WHERE " + Scope("r") + " AND lower(r.slug) = lower(" + s + ") LIMIT 1)", token).ConfigureAwait(false);
            return list.FirstOrDefault();
        }

        public async Task<List<LibrarySeries>> ReadSeriesManyAsync(IEnumerable<string> ids, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            List<string> list = ids.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (list.Count == 0) return new List<LibrarySeries>();
            return await LoadSeriesAsync("se.id IN " + D.InList(list), token).ConfigureAwait(false);
        }

        public async Task<LibrarySeries> UpdateSeriesAsync(LibrarySeries series, string previousSlug, ContentAudienceEnum? narrowFrom, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(series);
            string id = D.Str(series.Id);
            List<string> statements = new List<string>();
            if (!String.IsNullOrEmpty(previousSlug) && !String.Equals(previousSlug, series.Slug, StringComparison.OrdinalIgnoreCase))
            {
                statements.Add("DELETE FROM seriesslugs WHERE " + Scope() + " AND lower(slug) = lower(" + D.Str(series.Slug) + ");");
                statements.Add("INSERT INTO seriesslugs (propertyid, slug, seriesid, createdutc) VALUES (" + Pid + ", " + D.Str(previousSlug.ToLowerInvariant()) + ", " + id + ", " + D.Ts(series.UpdatedUtc) +
                    ") ON CONFLICT (propertyid, slug) DO UPDATE SET seriesid = excluded.seriesid, createdutc = excluded.createdutc;");
            }
            statements.Add("UPDATE series SET slug = " + D.Str(series.Slug) + ", title = " + D.Str(series.Title) + ", summary = " + D.Str(series.Summary) + ", description = " + D.Str(series.Description) +
                ", covermediaid = " + D.Str(series.CoverMediaId) + ", audience = " + D.Enum(series.Audience) + ", updatedutc = " + D.Ts(series.UpdatedUtc) + " WHERE " + Scope() + " AND id = " + id + ";");
            statements.AddRange(SeriesLevelStatements(series));
            if (narrowFrom != null && narrowFrom.Value != series.Audience)
                statements.Add("UPDATE lessons SET audience = " + D.Enum(series.Audience) + ", updatedutc = " + D.Ts(series.UpdatedUtc) + " WHERE " + Scope() + " AND seriesid = " + id + " AND audience = " + D.Enum(narrowFrom.Value) + ";");
            await UniqueTransactionAsync(statements, "Series slug", token).ConfigureAwait(false);
            return (await ReadSeriesAsync(series.Id, token).ConfigureAwait(false)) ?? series;
        }

        public Task<long> CountSeriesAsync(CancellationToken token = default)
        {
            return CountAsync("SELECT COUNT(*) AS cnt FROM series WHERE " + Scope() + " AND deletedutc IS NULL;", token);
        }

        public async Task<int> NextSeriesSortOrderAsync(CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT COALESCE(MAX(sortorder), 0) AS m FROM series WHERE " + Scope() + " AND deletedutc IS NULL;", token).ConfigureAwait(false);
            return (table.Rows.Count == 0 ? 0 : RowReader.GetInt(table.Rows[0], "m")) / 10 * 10 + 10;
        }

        public async Task<List<string>> ActiveSeriesIdsAsync(int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT id FROM series WHERE " + Scope() + " AND deletedutc IS NULL AND status <> " + D.Enum(LibraryStatusEnum.Archived) + " ORDER BY sortorder, id LIMIT " +
                D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "id")).ToList();
        }

        public async Task<int> ReorderSeriesAsync(List<string> seriesIds, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(seriesIds);
            if (seriesIds.Count == 0) return 0;
            string values = String.Join(", ", seriesIds.Select((id, i) => "(" + D.Str(id) + ", " + D.Num((i + 1) * 10) + ")"));
            DataTable table = await QueryAsync("UPDATE series SET sortorder = v.pos, updatedutc = " + D.Ts(nowUtc) + " FROM (VALUES " + values + ") AS v(id, pos) WHERE " + Scope("series") +
                " AND series.id = v.id AND series.deletedutc IS NULL RETURNING series.id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public async Task<bool> SetSeriesStatusAsync(string id, LibraryStatusEnum status, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            string now = D.Ts(nowUtc);
            string extra = status == LibraryStatusEnum.Published ? ", publishedutc = COALESCE(publishedutc, " + now + "), archivedutc = NULL"
                : status == LibraryStatusEnum.Archived ? ", archivedutc = " + now : ", archivedutc = NULL";
            DataTable table = await QueryAsync("UPDATE series SET status = " + D.Enum(status) + extra + ", updatedutc = " + now + " WHERE " + Scope() + " AND id = " + D.Str(id) +
                " AND deletedutc IS NULL RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> DeleteSeriesAsync(string id, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("UPDATE series SET deletedutc = " + D.Ts(nowUtc) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) +
                " AND deletedutc IS NULL AND NOT EXISTS (SELECT 1 FROM lessons l WHERE " + Scope("l") + " AND l.seriesid = series.id AND l.deletedutc IS NULL) RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<EnumerationResult<LibrarySeries>> EnumerateSeriesAsync(EnumerationRequest request, SeriesFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = new List<string> { "se.deletedutc IS NULL" };
            if (filter.Admin)
            {
                if (filter.Statuses.Count > 0) conditions.Add("se.status IN " + D.InList(filter.Statuses.Select(s => s.ToString())));
            }
            else
            {
                conditions.Add("se.status = " + D.Enum(LibraryStatusEnum.Published));
                List<string> reach = new List<string>();
                if (filter.Audiences != null && filter.Audiences.Count > 0) reach.Add("se.audience IN " + D.InList(filter.Audiences.Select(a => a.ToString())));
                if (filter.StubAudiences != null && filter.StubAudiences.Count > 0) reach.Add("se.audience IN " + D.InList(filter.StubAudiences.Select(a => a.ToString())));
                if (filter.LessonAudiences != null && filter.LessonAudiences.Count > 0)
                    reach.Add("EXISTS (SELECT 1 FROM lessons ol WHERE " + Scope("ol") + " AND ol.seriesid = se.id AND " + VisibleLesson("ol") + " AND ol.audience IN " + D.InList(filter.LessonAudiences.Select(a => a.ToString())) + ")");
                if (filter.Audiences != null) conditions.Add(reach.Count == 0 ? "1 = 0" : "(" + String.Join(" OR ", reach) + ")");
            }
            if (filter.Audience != null) conditions.Add("se.audience = " + D.Enum(filter.Audience.Value));
            if (filter.Levels.Count > 0) conditions.Add("EXISTS (SELECT 1 FROM serieslevels sl WHERE " + Scope("sl") + " AND sl.seriesid = se.id AND sl.levelkey IN " + D.InList(filter.Levels) + ")");
            if (filter.Ids != null) conditions.Add(filter.Ids.Count == 0 ? "1 = 0" : "se.id IN " + D.InList(filter.Ids));
            if (!String.IsNullOrEmpty(filter.FollowedByUserId))
                conditions.Add("EXISTS (SELECT 1 FROM seriesfollows sf WHERE " + Scope("sf") + " AND sf.seriesid = se.id AND sf.userid = " + D.Str(filter.FollowedByUserId) + " AND sf.isoptedout = " + D.Bool(false) + ")");
            if (filter.Progress != null && !String.IsNullOrEmpty(filter.ProgressUserId))
            {
                string user = D.Str(filter.ProgressUserId);
                string any = "EXISTS (SELECT 1 FROM lessonprogress sp JOIN lessons spl ON " + Scope("spl") + " AND spl.id = sp.lessonid WHERE " + Scope("sp") + " AND sp.userid = " + user + " AND spl.seriesid = se.id AND spl.deletedutc IS NULL)";
                string done = "(se.lessoncount > 0 AND (SELECT COUNT(*) FROM lessonprogress cp JOIN lessons cpl ON " + Scope("cpl") + " AND cpl.id = cp.lessonid WHERE " + Scope("cp") + " AND cp.userid = " + user +
                    " AND cp.completed = " + D.Bool(true) + " AND cpl.seriesid = se.id AND " + VisibleLesson("cpl") + ") >= se.lessoncount)";
                switch (filter.Progress.Value)
                {
                    case LessonProgressStateEnum.NotStarted:
                        conditions.Add("NOT " + any);
                        break;
                    case LessonProgressStateEnum.Completed:
                        conditions.Add(done);
                        break;
                    default:
                        conditions.Add(any + " AND NOT " + done);
                        break;
                }
            }
            EnumerationResult<string> ids = await EnumerateAsync(request, "se.id", "series se", conditions, r => RowReader.GetString(r, "id"), token).ConfigureAwait(false);
            List<LibrarySeries> rows = ids.Objects.Count == 0 ? new List<LibrarySeries>() : await LoadSeriesAsync("se.id IN " + D.InList(ids.Objects), token).ConfigureAwait(false);
            Dictionary<string, LibrarySeries> byId = rows.ToDictionary(s => s.Id, StringComparer.Ordinal);
            return ids.With(ids.Objects.Where(byId.ContainsKey).Select(i => byId[i]).ToList());
        }

        #endregion

        #region Lessons

        public async Task<Lesson> CreateLessonAsync(Lesson lesson, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(lesson);
            RequireText(lesson.Id, nameof(lesson.Id));
            RequireText(lesson.SeriesId, nameof(lesson.SeriesId));
            string series = D.Str(lesson.SeriesId);
            string last = "(SELECT COALESCE(MAX(p.position), 0) + 1 FROM lessons p WHERE " + Scope("p") + " AND p.seriesid = " + series + " AND p.deletedutc IS NULL)";
            List<string> statements = new List<string>
            {
                "DELETE FROM lessonslugs WHERE " + Scope() + " AND seriesid = " + series + " AND lower(slug) = lower(" + D.Str(lesson.Slug) + ");"
            };
            string position = last;
            if (lesson.Position >= 1)
            {
                statements.Add("UPDATE lessons SET position = position + 1 WHERE " + Scope() + " AND seriesid = " + series + " AND deletedutc IS NULL AND position >= " + D.Num(lesson.Position) + ";");
                position = "LEAST(" + D.Num(lesson.Position) + ", " + last + ")";
            }
            statements.Add("INSERT INTO lessons (propertyid, id, seriesid, position, slug, title, summary, description, kind, videomediaid, durationseconds, status, publishatutc, audience, downloadable, " +
                "commentsenabled, notifystaffoncomment, commentcount, reactiontotal, openreportcount, moderationstate, publishedutc, createdbyuserid, createdutc, updatedutc) SELECT " + Pid + ", " +
                D.Str(lesson.Id) + ", " + series + ", " + position + ", " + D.Str(lesson.Slug) + ", " + D.Str(lesson.Title) + ", " + D.Str(lesson.Summary) + ", " + D.Str(lesson.Description) + ", " +
                D.Enum(lesson.Kind) + ", " + D.Str(lesson.VideoMediaId) + ", " + D.Num(lesson.DurationSeconds) + ", " + D.Enum(lesson.Status) + ", " + D.Ts(lesson.PublishAtUtc) + ", " +
                D.Enum(lesson.Audience) + ", " + D.Bool(lesson.Downloadable) + ", " + D.Bool(lesson.CommentsEnabled) + ", " + D.Bool(lesson.NotifyStaffOnComment) + ", 0, 0, 0, " +
                D.Enum(ModerationStateEnum.None) + ", " + D.Ts(lesson.PublishedUtc) + ", " + D.Str(lesson.CreatedByUserId) + ", " + D.Ts(lesson.CreatedUtc) + ", " + D.Ts(lesson.UpdatedUtc) + ";");
            statements.AddRange(ChildStatements(lesson));
            statements.AddRange(RecomputeSeries(series));
            await UniqueTransactionAsync(statements, "Lesson slug", token).ConfigureAwait(false);
            return (await ReadLessonAsync(lesson.Id, token).ConfigureAwait(false)) ?? lesson;
        }

        public async Task<Lesson?> ReadLessonAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            List<Lesson> list = await LoadLessonsAsync("l.id = " + D.Str(id) + " AND l.deletedutc IS NULL", true, token).ConfigureAwait(false);
            return list.FirstOrDefault();
        }

        public async Task<Lesson?> ReadLessonBySlugAsync(string slug, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(slug)) return null;
            string s = D.Str(slug.Trim());
            DataTable table = await QueryAsync("SELECT l.id FROM lessons l WHERE " + Scope("l") + " AND l.deletedutc IS NULL AND lower(l.slug) = lower(" + s + ") ORDER BY l.publishedutc DESC NULLS LAST, l.id LIMIT 1;", token).ConfigureAwait(false);
            if (table.Rows.Count == 0)
                table = await QueryAsync("SELECT r.lessonid AS id FROM lessonslugs r WHERE " + Scope("r") + " AND lower(r.slug) = lower(" + s + ") ORDER BY r.createdutc DESC LIMIT 1;", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            return await ReadLessonAsync(RowReader.GetString(table.Rows[0], "id"), token).ConfigureAwait(false);
        }

        public async Task<List<Lesson>> ReadLessonsAsync(IEnumerable<string> ids, bool children, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            List<string> list = ids.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (list.Count == 0) return new List<Lesson>();
            return await LoadLessonsAsync("l.id IN " + D.InList(list), children, token).ConfigureAwait(false);
        }

        public async Task<Lesson> UpdateLessonAsync(Lesson lesson, string previousSlug, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(lesson);
            string id = D.Str(lesson.Id);
            string series = D.Str(lesson.SeriesId);
            List<string> statements = new List<string>();
            if (!String.IsNullOrEmpty(previousSlug) && !String.Equals(previousSlug, lesson.Slug, StringComparison.OrdinalIgnoreCase))
            {
                statements.Add("DELETE FROM lessonslugs WHERE " + Scope() + " AND seriesid = " + series + " AND lower(slug) = lower(" + D.Str(lesson.Slug) + ");");
                statements.Add("INSERT INTO lessonslugs (propertyid, seriesid, slug, lessonid, createdutc) VALUES (" + Pid + ", " + series + ", " + D.Str(previousSlug.ToLowerInvariant()) + ", " + id + ", " +
                    D.Ts(lesson.UpdatedUtc) + ") ON CONFLICT (propertyid, seriesid, slug) DO UPDATE SET lessonid = excluded.lessonid, createdutc = excluded.createdutc;");
            }
            statements.Add("UPDATE lessons SET slug = " + D.Str(lesson.Slug) + ", title = " + D.Str(lesson.Title) + ", summary = " + D.Str(lesson.Summary) + ", description = " + D.Str(lesson.Description) +
                ", kind = " + D.Enum(lesson.Kind) + ", videomediaid = " + D.Str(lesson.VideoMediaId) + ", durationseconds = " + D.Num(lesson.DurationSeconds) + ", status = " + D.Enum(lesson.Status) +
                ", publishatutc = " + D.Ts(lesson.PublishAtUtc) + ", audience = " + D.Enum(lesson.Audience) + ", downloadable = " + D.Bool(lesson.Downloadable) + ", commentsenabled = " +
                D.Bool(lesson.CommentsEnabled) + ", notifystaffoncomment = " + D.Bool(lesson.NotifyStaffOnComment) + ", updatedutc = " + D.Ts(lesson.UpdatedUtc) + " WHERE " + Scope() + " AND id = " + id + ";");
            statements.Add("UPDATE lessonprogress SET positionseconds = LEAST(positionseconds, " + D.Num(lesson.DurationSeconds) + "), maxpositionseconds = LEAST(maxpositionseconds, " +
                D.Num(lesson.DurationSeconds) + ") WHERE " + Scope() + " AND lessonid = " + id + " AND " + D.Num(lesson.DurationSeconds) + " > 0;");
            statements.AddRange(ChildStatements(lesson));
            statements.AddRange(RecomputeSeries(series));
            await UniqueTransactionAsync(statements, "Lesson slug", token).ConfigureAwait(false);
            return (await ReadLessonAsync(lesson.Id, token).ConfigureAwait(false)) ?? lesson;
        }

        public async Task<bool> SetLessonStatusAsync(string id, LibraryStatusEnum status, DateTime? publishAtUtc, IReadOnlyCollection<LibraryStatusEnum> expected, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            ArgumentNullException.ThrowIfNull(expected);
            string now = D.Ts(nowUtc);
            string extra = status == LibraryStatusEnum.Published ? ", publishedutc = COALESCE(publishedutc, " + now + "), archivedutc = NULL, publishatutc = NULL"
                : status == LibraryStatusEnum.Archived ? ", archivedutc = " + now : ", archivedutc = NULL, publishatutc = " + D.Ts(publishAtUtc);
            string guard = expected.Count == 0 ? String.Empty : " AND status IN " + D.InList(expected.Select(e => e.ToString()));
            List<string> statements = new List<string>
            {
                "CREATE TEMP TABLE IF NOT EXISTS lessonstatuschange (lessonid TEXT, seriesid TEXT) ON COMMIT DROP;",
                "WITH changed AS (UPDATE lessons SET status = " + D.Enum(status) + extra + ", updatedutc = " + now + " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND deletedutc IS NULL" + guard +
                    " RETURNING id, seriesid) INSERT INTO lessonstatuschange (lessonid, seriesid) SELECT id, seriesid FROM changed;",
                RecomputeSeriesSql("(SELECT c.seriesid FROM lessonstatuschange c)"),
                "SELECT lessonid FROM lessonstatuschange;"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> DeleteLessonAsync(string id, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            Lesson? lesson = (await LoadLessonsAsync("l.id = " + D.Str(id) + " AND l.deletedutc IS NULL", false, token).ConfigureAwait(false)).FirstOrDefault();
            if (lesson == null) return false;
            string series = D.Str(lesson.SeriesId);
            List<string> statements = new List<string>
            {
                "UPDATE lessons SET deletedutc = " + D.Ts(nowUtc) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND deletedutc IS NULL;",
                "UPDATE lessons SET position = position - 1 WHERE " + Scope() + " AND seriesid = " + series + " AND deletedutc IS NULL AND position > " + D.Num(lesson.Position) + ";"
            };
            statements.AddRange(RecomputeSeries(series));
            statements.Add("SELECT id FROM lessons WHERE " + Scope() + " AND id = " + D.Str(id) + " AND deletedutc IS NOT NULL;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> MoveLessonAsync(string id, string seriesId, int position, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            RequireText(seriesId, nameof(seriesId));
            Lesson? lesson = (await LoadLessonsAsync("l.id = " + D.Str(id) + " AND l.deletedutc IS NULL", false, token).ConfigureAwait(false)).FirstOrDefault();
            if (lesson == null) return false;
            string from = D.Str(lesson.SeriesId);
            string to = D.Str(seriesId);
            string lid = D.Str(id);
            List<string> statements = new List<string>
            {
                "UPDATE lessons SET position = 0 WHERE " + Scope() + " AND id = " + lid + ";",
                "UPDATE lessons SET position = position - 1 WHERE " + Scope() + " AND seriesid = " + from + " AND deletedutc IS NULL AND id <> " + lid + " AND position > " + D.Num(lesson.Position) + ";",
                "UPDATE lessons SET seriesid = " + to + ", position = -1, updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + lid + ";",
                "UPDATE lessons SET position = position + 1 WHERE " + Scope() + " AND seriesid = " + to + " AND deletedutc IS NULL AND id <> " + lid + " AND position >= " + D.Num(Math.Max(1, position)) + ";",
                "UPDATE lessons SET position = LEAST(" + D.Num(Math.Max(1, position)) + ", (SELECT COUNT(*) FROM lessons c WHERE " + Scope("c") + " AND c.seriesid = " + to + " AND c.deletedutc IS NULL)) WHERE " +
                    Scope() + " AND id = " + lid + ";"
            };
            statements.AddRange(RecomputeSeries(from));
            statements.AddRange(RecomputeSeries(to));
            await UniqueTransactionAsync(statements, "Lesson slug", token).ConfigureAwait(false);
            return true;
        }

        public async Task<int> ReorderLessonsAsync(string seriesId, List<string> lessonIds, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(seriesId, nameof(seriesId));
            ArgumentNullException.ThrowIfNull(lessonIds);
            if (lessonIds.Count == 0) return 0;
            string values = String.Join(", ", lessonIds.Select((id, i) => "(" + D.Str(id) + ", " + D.Num(i + 1) + ")"));
            DataTable table = await QueryAsync("UPDATE lessons SET position = v.pos, updatedutc = " + D.Ts(nowUtc) + " FROM (VALUES " + values + ") AS v(id, pos) WHERE " + Scope("lessons") +
                " AND lessons.id = v.id AND lessons.seriesid = " + D.Str(seriesId) + " AND lessons.deletedutc IS NULL RETURNING lessons.id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public async Task<List<string>> LessonIdsOfSeriesAsync(string seriesId, int limit, CancellationToken token = default)
        {
            RequireText(seriesId, nameof(seriesId));
            DataTable table = await QueryAsync("SELECT id FROM lessons WHERE " + Scope() + " AND seriesid = " + D.Str(seriesId) + " AND deletedutc IS NULL ORDER BY position, id LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "id")).ToList();
        }

        public async Task<List<Lesson?>> NeighborsAsync(string seriesId, int position, CancellationToken token = default)
        {
            RequireText(seriesId, nameof(seriesId));
            string baseCondition = "l.seriesid = " + D.Str(seriesId) + " AND " + VisibleLesson("l");
            List<Lesson> before = await LoadLessonsAsync(baseCondition + " AND l.position < " + D.Num(position) + " ORDER BY l.position DESC LIMIT 1", false, token).ConfigureAwait(false);
            List<Lesson> after = await LoadLessonsAsync(baseCondition + " AND l.position > " + D.Num(position) + " ORDER BY l.position ASC LIMIT 1", false, token).ConfigureAwait(false);
            return new List<Lesson?> { before.FirstOrDefault(), after.FirstOrDefault() };
        }

        public async Task<EnumerationResult<Lesson>> EnumerateLessonsAsync(EnumerationRequest request, LessonFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = new List<string> { "l.deletedutc IS NULL" };
            string seriesOk = "EXISTS (SELECT 1 FROM series ls WHERE " + Scope("ls") + " AND ls.id = l.seriesid AND ls.deletedutc IS NULL" +
                (filter.Admin ? String.Empty : " AND ls.status = " + D.Enum(LibraryStatusEnum.Published)) + ")";
            conditions.Add(seriesOk);
            if (filter.Admin)
            {
                if (filter.Statuses.Count > 0) conditions.Add("l.status IN " + D.InList(filter.Statuses.Select(s => s.ToString())));
                if (filter.HasVideo != null) conditions.Add(filter.HasVideo.Value ? "l.videomediaid IS NOT NULL" : "l.videomediaid IS NULL");
                if (filter.MediaStatus != null)
                    conditions.Add("EXISTS (SELECT 1 FROM mediaattachments lm WHERE " + Scope("lm") + " AND lm.id = l.videomediaid AND lm.status = " + D.Enum(filter.MediaStatus.Value) + ")");
            }
            else
            {
                conditions.Add("l.status = " + D.Enum(LibraryStatusEnum.Published) + " AND l.moderationstate = " + D.Enum(ModerationStateEnum.None));
                if (filter.PublicOnly) conditions.Add("l.audience = " + D.Enum(ContentAudienceEnum.Public));
                List<ContentAudienceEnum> reach = new List<ContentAudienceEnum>();
                if (filter.Audiences != null) reach.AddRange(filter.Audiences);
                if (filter.StubAudiences != null) reach.AddRange(filter.StubAudiences);
                if (filter.Audiences != null) conditions.Add(reach.Count == 0 ? "1 = 0" : "l.audience IN " + D.InList(reach.Select(a => a.ToString())));
            }
            if (filter.SeriesIds.Count > 0) conditions.Add("l.seriesid IN " + D.InList(filter.SeriesIds));
            if (filter.Audience != null) conditions.Add("l.audience = " + D.Enum(filter.Audience.Value));
            if (filter.Kind != null) conditions.Add("l.kind = " + D.Enum(filter.Kind.Value));
            if (filter.Levels.Count > 0) conditions.Add("EXISTS (SELECT 1 FROM lessonlevels ll WHERE " + Scope("ll") + " AND ll.lessonid = l.id AND ll.levelkey IN " + D.InList(filter.Levels) + ")");
            if (filter.HasResources != null) conditions.Add((filter.HasResources.Value ? String.Empty : "NOT ") + "EXISTS (SELECT 1 FROM lessonresources lr WHERE " + Scope("lr") + " AND lr.lessonid = l.id)");
            if (filter.MinDurationSeconds != null) conditions.Add("l.durationseconds >= " + D.Num(filter.MinDurationSeconds.Value));
            if (filter.MaxDurationSeconds != null) conditions.Add("l.durationseconds <= " + D.Num(filter.MaxDurationSeconds.Value));
            if (filter.Ids != null) conditions.Add(filter.Ids.Count == 0 ? "1 = 0" : "l.id IN " + D.InList(filter.Ids));
            if (filter.Progress != null && !String.IsNullOrEmpty(filter.ProgressUserId))
            {
                string mine = "SELECT 1 FROM lessonprogress lp WHERE " + Scope("lp") + " AND lp.lessonid = l.id AND lp.userid = " + D.Str(filter.ProgressUserId);
                switch (filter.Progress.Value)
                {
                    case LessonProgressStateEnum.NotStarted:
                        conditions.Add("NOT EXISTS (" + mine + ")");
                        break;
                    case LessonProgressStateEnum.Completed:
                        conditions.Add("EXISTS (" + mine + " AND lp.completed = " + D.Bool(true) + ")");
                        break;
                    default:
                        conditions.Add("EXISTS (" + mine + " AND lp.completed = " + D.Bool(false) + ")");
                        break;
                }
            }
            EnumerationResult<string> ids = await EnumerateAsync(request, "l.id", "lessons l", conditions, r => RowReader.GetString(r, "id"), token).ConfigureAwait(false);
            List<Lesson> rows = ids.Objects.Count == 0 ? new List<Lesson>() : await LoadLessonsAsync("l.id IN " + D.InList(ids.Objects), false, token).ConfigureAwait(false);
            Dictionary<string, Lesson> byId = rows.ToDictionary(s => s.Id, StringComparer.Ordinal);
            return ids.With(ids.Objects.Where(byId.ContainsKey).Select(i => byId[i]).ToList());
        }

        public async Task<List<Lesson>> DueLessonsAsync(DateTime nowUtc, int limit, CancellationToken token = default)
        {
            return await LoadLessonsAsync("l.deletedutc IS NULL AND l.status IN " + D.InList(new[] { LibraryStatusEnum.Scheduled.ToString(), LibraryStatusEnum.Processing.ToString() }) +
                " AND (l.publishatutc IS NULL OR l.publishatutc <= " + D.Ts(nowUtc) + ") ORDER BY l.publishatutc NULLS FIRST, l.id LIMIT " + D.Num(Math.Max(1, limit)), false, token).ConfigureAwait(false);
        }

        public async Task<List<string>> LessonIdsUsingMediaAsync(string mediaId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(mediaId)) return new List<string>();
            string m = D.Str(mediaId);
            DataTable table = await QueryAsync("SELECT l.id FROM lessons l WHERE " + Scope("l") + " AND l.deletedutc IS NULL AND (l.videomediaid = " + m +
                " OR EXISTS (SELECT 1 FROM lessonresources r WHERE " + Scope("r") + " AND r.lessonid = l.id AND r.mediaid = " + m + ")" +
                " OR EXISTS (SELECT 1 FROM lessoncaptions c WHERE " + Scope("c") + " AND c.lessonid = l.id AND c.mediaid = " + m + ")) LIMIT 100;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "id")).ToList();
        }

        public async Task<bool> SetLessonDurationAsync(string id, int seconds, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            string lid = D.Str(id);
            List<string> statements = new List<string>
            {
                "UPDATE lessons SET durationseconds = " + D.Num(seconds) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + lid + ";",
                "UPDATE lessonprogress SET positionseconds = LEAST(positionseconds, " + D.Num(seconds) + "), maxpositionseconds = LEAST(maxpositionseconds, " + D.Num(seconds) + ") WHERE " + Scope() +
                    " AND lessonid = " + lid + " AND " + D.Num(seconds) + " > 0;",
                RecomputeSeriesSql("(SELECT x.seriesid FROM lessons x WHERE " + Scope("x") + " AND x.id = " + lid + ")"),
                "SELECT id FROM lessons WHERE " + Scope() + " AND id = " + lid + ";"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task RecomputeEngagementAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            await QueryAsync("UPDATE lessons SET commentcount = (SELECT COUNT(*) FROM comments c WHERE " + Scope("c") + " AND c.targettype = " + D.Enum(TargetTypeEnum.Lesson) +
                " AND c.targetid = lessons.id AND c.deletedutc IS NULL AND c.moderationstate = " + D.Enum(ModerationStateEnum.None) + "), reactiontotal = (SELECT COUNT(*) FROM reactions r WHERE " + Scope("r") +
                " AND r.targettype = " + D.Enum(TargetTypeEnum.Lesson) + " AND r.targetid = lessons.id) WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
        }

        public async Task RecomputeReportsAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            await QueryAsync("UPDATE lessons SET openreportcount = (SELECT COUNT(*) FROM reports r WHERE " + Scope("r") + " AND r.targettype = " + D.Enum(TargetTypeEnum.Lesson) +
                " AND r.targetid = lessons.id AND r.status = " + D.Enum(ReportStatusEnum.Open) + ") WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
        }

        public async Task<bool> SetLessonModerationAsync(string id, ModerationStateEnum state, string? byUserId, string? reason, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            string lid = D.Str(id);
            List<string> statements = new List<string>
            {
                "UPDATE lessons SET moderationstate = " + D.Enum(state) + ", moderatedutc = " + D.Ts(nowUtc) + ", moderatedbyuserid = " + D.Str(byUserId) + ", moderationreason = " + D.Str(reason) +
                    ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + lid + ";",
                RecomputeSeriesSql("(SELECT x.seriesid FROM lessons x WHERE " + Scope("x") + " AND x.id = " + lid + ")"),
                "SELECT id FROM lessons WHERE " + Scope() + " AND id = " + lid + ";"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task SetLessonPromotionAsync(string id, string? feedPostId, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            await QueryAsync("UPDATE lessons SET promotedfeedpostid = " + D.Str(feedPostId) + " WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
        }

        public async Task<List<string>> ChangedIdsAsync(bool series, DateTime? sinceUtc, int limit, CancellationToken token = default)
        {
            string table = series ? "series" : "lessons";
            DataTable rows = await QueryAsync("SELECT id FROM " + table + " WHERE " + Scope() + (sinceUtc == null ? String.Empty : " AND updatedutc >= " + D.Ts(sinceUtc.Value)) +
                " ORDER BY updatedutc, id LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return rows.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "id")).ToList();
        }

        public async Task<List<string>> AggregateNewLessonNotificationsAsync(string seriesId, IReadOnlyCollection<string> recipientUserIds, string seriesTitle, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(seriesId, nameof(seriesId));
            ArgumentNullException.ThrowIfNull(recipientUserIds);
            if (recipientUserIds.Count == 0) return new List<string>();
            string now = D.Ts(nowUtc);
            DataTable table = await QueryAsync("UPDATE notifications SET actorcount = GREATEST(actorcount, 1) + 1, title = CAST(GREATEST(actorcount, 1) + 1 AS TEXT) || " + D.Str(" new lessons in " + seriesTitle) +
                ", path = " + D.Str("/series/" + seriesId) + ", updatedutc = " + now + ", pushstate = CASE WHEN lastpushedutc IS NULL OR lastpushedutc < " + D.Ts(nowUtc.AddHours(-1)) + " THEN " +
                D.Enum(PushStateEnum.Pending) + " ELSE pushstate END WHERE " + Scope() + " AND type = " + D.Enum(NotificationTypeEnum.NewLessonInSeries) + " AND targettype = " + D.Enum(TargetTypeEnum.Series) +
                " AND targetid = " + D.Str(seriesId) + " AND readutc IS NULL AND recipientuserid IN " + D.InList(recipientUserIds) + " RETURNING recipientuserid;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "recipientuserid")).Distinct(StringComparer.Ordinal).ToList();
        }

        public async Task<List<string>> StaffIdsAsync(CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT DISTINCT r.userid FROM roleassignments r JOIN users u ON " + Scope("u") + " AND u.id = r.userid WHERE " + Scope("r") + " AND r.role IN " +
                D.InList(new[] { RoleEnum.SystemAdmin.ToString(), RoleEnum.LibraryEditor.ToString() }) + " AND u.isdeleted = " + D.Bool(false) + " AND u.isbanned = " + D.Bool(false) +
                " ORDER BY r.userid LIMIT 500;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "userid")).ToList();
        }

        #endregion

        #region Private-Methods

        private string VisibleLesson(string alias)
        {
            return alias + ".deletedutc IS NULL AND " + alias + ".status = " + D.Enum(LibraryStatusEnum.Published) + " AND " + alias + ".moderationstate = " + D.Enum(ModerationStateEnum.None);
        }

        private List<string> RecomputeSeries(string seriesLiteral)
        {
            return new List<string> { RecomputeSeriesSql(seriesLiteral) };
        }

        private string RecomputeSeriesSql(string seriesExpression)
        {
            string counted = "FROM lessons c WHERE " + Scope("c") + " AND c.seriesid = series.id AND " + VisibleLesson("c");
            return "UPDATE series SET lessoncount = (SELECT COUNT(*) " + counted + "), totaldurationseconds = (SELECT COALESCE(SUM(c.durationseconds), 0) " + counted + ") WHERE " + Scope() +
                " AND id IN (" + (seriesExpression.StartsWith("(", StringComparison.Ordinal) ? seriesExpression.Substring(1, seriesExpression.Length - 2) : seriesExpression) + ");";
        }

        private string LevelInsert(LibraryLevel level)
        {
            return "INSERT INTO librarylevels (propertyid, id, key, label, color, sortorder, isretired, createdutc, updatedutc) VALUES (" + Pid + ", " + D.Str(level.Id) + ", " + D.Str(level.Key) + ", " +
                D.Str(level.Label) + ", " + D.Str(level.Color) + ", " + D.Num(level.SortOrder) + ", " + D.Bool(level.IsRetired) + ", " + D.Ts(level.CreatedUtc) + ", " + D.Ts(level.UpdatedUtc) + ");";
        }

        private List<string> SeriesLevelStatements(LibrarySeries series)
        {
            List<string> statements = new List<string> { "DELETE FROM serieslevels WHERE " + Scope() + " AND seriesid = " + D.Str(series.Id) + ";" };
            List<string> levels = series.Levels.Where(l => !String.IsNullOrEmpty(l)).Distinct(StringComparer.Ordinal).ToList();
            if (levels.Count > 0)
                statements.Add("INSERT INTO serieslevels (propertyid, seriesid, levelkey) VALUES " + String.Join(", ", levels.Select(l => "(" + Pid + ", " + D.Str(series.Id) + ", " + D.Str(l) + ")")) + ";");
            return statements;
        }

        private List<string> ChildStatements(Lesson lesson)
        {
            string id = D.Str(lesson.Id);
            List<string> statements = new List<string>
            {
                "DELETE FROM lessonlevels WHERE " + Scope() + " AND lessonid = " + id + ";",
                "DELETE FROM lessonmarkers WHERE " + Scope() + " AND lessonid = " + id + ";",
                "DELETE FROM lessonresources WHERE " + Scope() + " AND lessonid = " + id + ";",
                "DELETE FROM lessoncaptions WHERE " + Scope() + " AND lessonid = " + id + ";"
            };
            List<string> levels = lesson.Levels.Where(l => !String.IsNullOrEmpty(l)).Distinct(StringComparer.Ordinal).ToList();
            if (levels.Count > 0)
                statements.Add("INSERT INTO lessonlevels (propertyid, lessonid, levelkey) VALUES " + String.Join(", ", levels.Select(l => "(" + Pid + ", " + id + ", " + D.Str(l) + ")")) + ";");
            if (lesson.Markers.Count > 0)
                statements.Add("INSERT INTO lessonmarkers (propertyid, id, lessonid, seconds, label) VALUES " + String.Join(", ", lesson.Markers.Select(m => "(" + Pid + ", " + D.Str(m.Id) + ", " + id + ", " +
                    D.Num(m.Seconds) + ", " + D.Str(m.Label) + ")")) + ";");
            if (lesson.Resources.Count > 0)
                statements.Add("INSERT INTO lessonresources (propertyid, id, lessonid, kind, title, mediaid, url, text, position) VALUES " + String.Join(", ", lesson.Resources.Select(r => "(" + Pid + ", " +
                    D.Str(r.Id) + ", " + id + ", " + D.Enum(r.Kind) + ", " + D.Str(r.Title) + ", " + D.Str(r.MediaId) + ", " + D.Str(r.Url) + ", " + D.Str(r.Text) + ", " + D.Num(r.Position) + ")")) + ";");
            if (lesson.Captions.Count > 0)
                statements.Add("INSERT INTO lessoncaptions (propertyid, id, lessonid, language, label, mediaid, position) VALUES " + String.Join(", ", lesson.Captions.Select(c => "(" + Pid + ", " +
                    D.Str(c.Id) + ", " + id + ", " + D.Str(c.Language) + ", " + D.Str(c.Label) + ", " + D.Str(c.MediaId) + ", " + D.Num(c.Position) + ")")) + ";");
            return statements;
        }

        private async Task<List<LibrarySeries>> LoadSeriesAsync(string condition, CancellationToken token)
        {
            DataTable table = await QueryAsync("SELECT " + _SeriesColumns + " FROM series se WHERE " + Scope("se") + " AND " + condition + ";", token).ConfigureAwait(false);
            List<LibrarySeries> list = table.Rows.Cast<DataRow>().Select(MapSeries).ToList();
            if (list.Count == 0) return list;
            DataTable levels = await QueryAsync("SELECT seriesid, levelkey FROM serieslevels WHERE " + Scope() + " AND seriesid IN " + D.InList(list.Select(s => s.Id)) + " ORDER BY levelkey;", token).ConfigureAwait(false);
            Dictionary<string, LibrarySeries> byId = list.ToDictionary(s => s.Id, StringComparer.Ordinal);
            foreach (DataRow row in levels.Rows)
                if (byId.TryGetValue(RowReader.GetString(row, "seriesid"), out LibrarySeries? s)) s.Levels.Add(RowReader.GetString(row, "levelkey"));
            return list;
        }

        private async Task<List<Lesson>> LoadLessonsAsync(string condition, bool children, CancellationToken token)
        {
            DataTable table = await QueryAsync("SELECT " + _LessonColumns + " FROM lessons l WHERE " + Scope("l") + " AND " + condition + ";", token).ConfigureAwait(false);
            List<Lesson> list = table.Rows.Cast<DataRow>().Select(MapLesson).ToList();
            if (list.Count == 0) return list;
            string ids = D.InList(list.Select(l => l.Id));
            Dictionary<string, Lesson> byId = list.GroupBy(l => l.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            DataTable levels = await QueryAsync("SELECT lessonid, levelkey FROM lessonlevels WHERE " + Scope() + " AND lessonid IN " + ids + " ORDER BY levelkey;", token).ConfigureAwait(false);
            foreach (DataRow row in levels.Rows)
                if (byId.TryGetValue(RowReader.GetString(row, "lessonid"), out Lesson? l)) l.Levels.Add(RowReader.GetString(row, "levelkey"));
            DataTable resources = await QueryAsync("SELECT * FROM lessonresources WHERE " + Scope() + " AND lessonid IN " + ids + " ORDER BY lessonid, position, id;", token).ConfigureAwait(false);
            foreach (DataRow row in resources.Rows)
            {
                if (!byId.TryGetValue(RowReader.GetString(row, "lessonid"), out Lesson? l)) continue;
                l.Resources.Add(new LessonResource
                {
                    Id = RowReader.GetString(row, "id"),
                    LessonId = l.Id,
                    Kind = RowReader.GetEnum(row, "kind", LessonResourceKindEnum.Link),
                    Title = RowReader.GetString(row, "title"),
                    MediaId = RowReader.GetNullableString(row, "mediaid"),
                    Url = RowReader.GetNullableString(row, "url"),
                    Text = RowReader.GetNullableString(row, "text"),
                    Position = RowReader.GetInt(row, "position")
                });
            }
            if (!children) return list;
            DataTable markers = await QueryAsync("SELECT * FROM lessonmarkers WHERE " + Scope() + " AND lessonid IN " + ids + " ORDER BY lessonid, seconds, id;", token).ConfigureAwait(false);
            foreach (DataRow row in markers.Rows)
            {
                if (!byId.TryGetValue(RowReader.GetString(row, "lessonid"), out Lesson? l)) continue;
                l.Markers.Add(new LessonMarker { Id = RowReader.GetString(row, "id"), LessonId = l.Id, Seconds = RowReader.GetInt(row, "seconds"), Label = RowReader.GetString(row, "label") });
            }
            DataTable captions = await QueryAsync("SELECT * FROM lessoncaptions WHERE " + Scope() + " AND lessonid IN " + ids + " ORDER BY lessonid, position, id;", token).ConfigureAwait(false);
            foreach (DataRow row in captions.Rows)
            {
                if (!byId.TryGetValue(RowReader.GetString(row, "lessonid"), out Lesson? l)) continue;
                l.Captions.Add(new LessonCaption
                {
                    Id = RowReader.GetString(row, "id"),
                    LessonId = l.Id,
                    Language = RowReader.GetString(row, "language"),
                    Label = RowReader.GetString(row, "label"),
                    MediaId = RowReader.GetString(row, "mediaid"),
                    Position = RowReader.GetInt(row, "position")
                });
            }
            return list;
        }

        private static LibraryLevel MapLevel(DataRow row)
        {
            return new LibraryLevel
            {
                Id = RowReader.GetString(row, "id"),
                Key = RowReader.GetString(row, "key"),
                Label = RowReader.GetString(row, "label"),
                Color = RowReader.GetNullableString(row, "color"),
                SortOrder = RowReader.GetInt(row, "sortorder"),
                IsRetired = RowReader.GetBool(row, "isretired"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        private static LibrarySeries MapSeries(DataRow row)
        {
            return new LibrarySeries
            {
                Id = RowReader.GetString(row, "id"),
                Slug = RowReader.GetString(row, "slug"),
                Title = RowReader.GetString(row, "title"),
                Summary = RowReader.GetNullableString(row, "summary"),
                Description = RowReader.GetNullableString(row, "description"),
                CoverMediaId = RowReader.GetNullableString(row, "covermediaid"),
                Status = RowReader.GetEnum(row, "status", LibraryStatusEnum.Draft),
                SortOrder = RowReader.GetInt(row, "sortorder"),
                Audience = RowReader.GetEnum(row, "audience", ContentAudienceEnum.Members),
                LessonCount = RowReader.GetInt(row, "lessoncount"),
                TotalDurationSeconds = RowReader.GetInt(row, "totaldurationseconds"),
                PublishedUtc = RowReader.GetNullableDateTime(row, "publishedutc"),
                ArchivedUtc = RowReader.GetNullableDateTime(row, "archivedutc"),
                DeletedUtc = RowReader.GetNullableDateTime(row, "deletedutc"),
                CreatedByUserId = RowReader.GetNullableString(row, "createdbyuserid"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        private static Lesson MapLesson(DataRow row)
        {
            return new Lesson
            {
                Id = RowReader.GetString(row, "id"),
                SeriesId = RowReader.GetString(row, "seriesid"),
                Position = RowReader.GetInt(row, "position"),
                Slug = RowReader.GetString(row, "slug"),
                Title = RowReader.GetString(row, "title"),
                Summary = RowReader.GetNullableString(row, "summary"),
                Description = RowReader.GetNullableString(row, "description"),
                Kind = RowReader.GetEnum(row, "kind", LessonKindEnum.Video),
                VideoMediaId = RowReader.GetNullableString(row, "videomediaid"),
                DurationSeconds = RowReader.GetInt(row, "durationseconds"),
                Status = RowReader.GetEnum(row, "status", LibraryStatusEnum.Draft),
                PublishAtUtc = RowReader.GetNullableDateTime(row, "publishatutc"),
                Audience = RowReader.GetEnum(row, "audience", ContentAudienceEnum.Members),
                Downloadable = RowReader.GetBool(row, "downloadable"),
                CommentsEnabled = RowReader.GetBool(row, "commentsenabled"),
                NotifyStaffOnComment = RowReader.GetBool(row, "notifystaffoncomment"),
                CommentCount = RowReader.GetInt(row, "commentcount"),
                ReactionTotal = RowReader.GetInt(row, "reactiontotal"),
                OpenReportCount = RowReader.GetInt(row, "openreportcount"),
                ModerationState = RowReader.GetEnum(row, "moderationstate", ModerationStateEnum.None),
                ModeratedUtc = RowReader.GetNullableDateTime(row, "moderatedutc"),
                ModeratedByUserId = RowReader.GetNullableString(row, "moderatedbyuserid"),
                ModerationReason = RowReader.GetNullableString(row, "moderationreason"),
                PromotedFeedPostId = RowReader.GetNullableString(row, "promotedfeedpostid"),
                PublishedUtc = RowReader.GetNullableDateTime(row, "publishedutc"),
                ArchivedUtc = RowReader.GetNullableDateTime(row, "archivedutc"),
                DeletedUtc = RowReader.GetNullableDateTime(row, "deletedutc"),
                CreatedByUserId = RowReader.GetNullableString(row, "createdbyuserid"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        #endregion
    }
}
