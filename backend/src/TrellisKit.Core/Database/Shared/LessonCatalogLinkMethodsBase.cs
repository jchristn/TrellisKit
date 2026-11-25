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

    internal abstract class LessonCatalogLinkMethodsBase : MethodsBase, ILessonCatalogLinkMethods
    {
        private const string _Columns = "id, lessonid, catalogentryid, position, startseconds, source, createdby, createdutc";
        private const int _MaxLessons = 100;

        protected LessonCatalogLinkMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<List<LessonCatalogEntry>> ReplaceAsync(string lessonId, List<LessonCatalogEntry> links, CancellationToken token = default)
        {
            RequireText(lessonId, nameof(lessonId));
            ArgumentNullException.ThrowIfNull(links);
            if (links.Count > LessonCatalogEntry.MaxPerLesson) throw new ArgumentOutOfRangeException(nameof(links), "At most " + LessonCatalogEntry.MaxPerLesson + " links.");
            string lesson = D.Str(lessonId);
            List<string> statements = new List<string>(Driver.Metadata.DeleteWhereStatements(TargetTypeEnum.LessonCatalogEntry,
                "SELECT x.id FROM lessoncatalogentries x WHERE " + Scope("x") + " AND x.lessonid = " + lesson));
            statements.Add("DELETE FROM lessoncatalogentries WHERE " + Scope() + " AND lessonid = " + lesson + ";");
            foreach (LessonCatalogEntry link in links)
            {
                RequireText(link.Id, nameof(link.Id));
                RequireText(link.CatalogEntryId, nameof(link.CatalogEntryId));
                statements.Add("INSERT INTO lessoncatalogentries (propertyid, " + _Columns + ") VALUES (" + Pid + ", " + D.Str(link.Id) + ", " + lesson + ", " + D.Str(link.CatalogEntryId) + ", " +
                    D.Num(link.Position) + ", " + D.Num(link.StartSeconds) + ", " + D.Enum(link.Source) + ", " + D.Str(link.CreatedBy) + ", " + D.Ts(link.CreatedUtc) + ");");
            }
            await TransactionAsync(statements, token).ConfigureAwait(false);
            foreach (LessonCatalogEntry link in links.Where(l => l.Labels.Count > 0 || l.Tags.Count > 0))
                await Driver.Metadata.WriteAsync(TargetTypeEnum.LessonCatalogEntry, link.Id, new ObjectMetadata { Labels = link.Labels, Tags = link.Tags }, token).ConfigureAwait(false);
            return await ListForLessonAsync(lessonId, token).ConfigureAwait(false);
        }

        public async Task<List<LessonCatalogEntry>> ListForLessonAsync(string lessonId, CancellationToken token = default)
        {
            RequireText(lessonId, nameof(lessonId));
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM lessoncatalogentries WHERE " + Scope() + " AND lessonid = " + D.Str(lessonId) +
                " ORDER BY position, id LIMIT " + D.Num(LessonCatalogEntry.MaxPerLesson) + ";", token).ConfigureAwait(false);
            List<LessonCatalogEntry> rows = table.Rows.Cast<DataRow>().Select(Map).ToList();
            await FillAsync(rows, token).ConfigureAwait(false);
            return rows;
        }

        public async Task<EnumerationResult<LessonCatalogEntry>> EnumerateForLessonAsync(EnumerationRequest request, string lessonId, bool publishedOnly, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            RequireText(lessonId, nameof(lessonId));
            List<string> conditions = new List<string> { "lce.lessonid = " + D.Str(lessonId), "e.deletedutc IS NULL" };
            if (publishedOnly) conditions.Add("e.status = 'Published' AND e.moderationstate = " + D.Enum(ModerationStateEnum.None));
            string select = String.Join(", ", _Columns.Split(", ").Select(c => "lce." + c));
            EnumerationResult<LessonCatalogEntry> page = await EnumerateAsync(request, select,
                "lessoncatalogentries lce JOIN catalogentries e ON e.propertyid = lce.propertyid AND e.id = lce.catalogentryid AND " + Scope("e"), conditions, Map, token).ConfigureAwait(false);
            await FillAsync(page.Objects, token).ConfigureAwait(false);
            return page;
        }

        public async Task<Dictionary<string, LessonCatalogEntry>> ForEntryAsync(string catalogEntryId, IReadOnlyList<string> lessonIds, CancellationToken token = default)
        {
            RequireText(catalogEntryId, nameof(catalogEntryId));
            ArgumentNullException.ThrowIfNull(lessonIds);
            List<string> ids = lessonIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).Take(_MaxLessons).ToList();
            if (ids.Count == 0) return new Dictionary<string, LessonCatalogEntry>(StringComparer.Ordinal);
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM lessoncatalogentries WHERE " + Scope() + " AND catalogentryid = " + D.Str(catalogEntryId) +
                " AND lessonid IN " + D.InList(ids) + " LIMIT " + D.Num(_MaxLessons) + ";", token).ConfigureAwait(false);
            List<LessonCatalogEntry> rows = table.Rows.Cast<DataRow>().Select(Map).ToList();
            await FillAsync(rows, token).ConfigureAwait(false);
            return rows.ToDictionary(r => r.LessonId, StringComparer.Ordinal);
        }

        public Task<int> DeleteForLessonAsync(string lessonId, CancellationToken token = default)
        {
            RequireText(lessonId, nameof(lessonId));
            return DeleteWhereAsync("x.lessonid = " + D.Str(lessonId), token);
        }

        public Task<int> DeleteForEntryAsync(string catalogEntryId, CancellationToken token = default)
        {
            RequireText(catalogEntryId, nameof(catalogEntryId));
            return DeleteWhereAsync("x.catalogentryid = " + D.Str(catalogEntryId), token);
        }

        private async Task<int> DeleteWhereAsync(string condition, CancellationToken token)
        {
            string match = Scope("x") + " AND " + condition;
            List<string> statements = new List<string>(Driver.Metadata.DeleteWhereStatements(TargetTypeEnum.LessonCatalogEntry, "SELECT x.id FROM lessoncatalogentries x WHERE " + match));
            statements.Add("DELETE FROM lessoncatalogentries x WHERE " + match + " RETURNING x.id;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        private async Task FillAsync(List<LessonCatalogEntry> rows, CancellationToken token)
        {
            if (rows.Count == 0) return;
            Dictionary<string, ObjectMetadata> metadata = await Driver.Metadata.ReadManyAsync(TargetTypeEnum.LessonCatalogEntry, rows.Select(r => r.Id), token).ConfigureAwait(false);
            foreach (LessonCatalogEntry row in rows)
            {
                if (!metadata.TryGetValue(row.Id, out ObjectMetadata? m)) continue;
                row.Labels = m.Labels;
                row.Tags = m.Tags;
            }
        }

        private static LessonCatalogEntry Map(DataRow row)
        {
            return new LessonCatalogEntry
            {
                Id = RowReader.GetString(row, "id"),
                LessonId = RowReader.GetString(row, "lessonid"),
                CatalogEntryId = RowReader.GetString(row, "catalogentryid"),
                Position = RowReader.GetInt(row, "position"),
                StartSeconds = RowReader.GetNullableInt(row, "startseconds"),
                Source = RowReader.GetEnum(row, "source", LessonCatalogLinkSourceEnum.Editor),
                CreatedBy = RowReader.GetString(row, "createdby"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
        }
    }
}
