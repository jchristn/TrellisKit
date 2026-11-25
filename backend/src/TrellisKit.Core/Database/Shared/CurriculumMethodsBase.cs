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

    internal abstract class CurriculumMethodsBase : MethodsBase, ICurriculumMethods
    {
        private const string _Select = "c.*, (SELECT COUNT(*) FROM curriculumitems ci WHERE ci.propertyid = c.propertyid AND ci.curriculumid = c.id) AS itemcount";

        protected CurriculumMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task CreateAsync(Curriculum curriculum, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(curriculum);
            await QueryAsync("INSERT INTO curricula (propertyid, id, name, description, fieldkey, visibility, status, position, createdbyuserid, publishedutc, createdutc, updatedutc) VALUES (" +
                Pid + ", " + D.Str(curriculum.Id) + ", " + D.Str(curriculum.Name) + ", " + D.Str(curriculum.Description) + ", " + D.Str(curriculum.FieldKey) + ", " + D.Enum(curriculum.Visibility) + ", " +
                D.Enum(curriculum.Status) + ", " + D.Num(curriculum.Position) + ", " + D.Str(curriculum.CreatedByUserId) + ", " + D.Ts(curriculum.PublishedUtc) + ", " +
                D.Ts(curriculum.CreatedUtc) + ", " + D.Ts(curriculum.UpdatedUtc) + ");", token).ConfigureAwait(false);
        }

        public async Task<bool> UpdateAsync(Curriculum curriculum, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(curriculum);
            DataTable table = await QueryAsync("UPDATE curricula SET name = " + D.Str(curriculum.Name) + ", description = " + D.Str(curriculum.Description) + ", fieldkey = " + D.Str(curriculum.FieldKey) +
                ", visibility = " + D.Enum(curriculum.Visibility) + ", status = " + D.Enum(curriculum.Status) + ", position = " + D.Num(curriculum.Position) + ", publishedutc = " + D.Ts(curriculum.PublishedUtc) +
                ", updatedutc = " + D.Ts(curriculum.UpdatedUtc) + " WHERE " + Scope() + " AND id = " + D.Str(curriculum.Id) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<Curriculum?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            DataTable table = await QueryAsync("SELECT " + _Select + " FROM curricula c WHERE " + Scope("c") + " AND c.id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Map(table.Rows[0]);
        }

        public async Task<Dictionary<string, Curriculum>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            List<string> list = DirectorySql.Distinct(ids).Take(1000).ToList();
            Dictionary<string, Curriculum> result = new Dictionary<string, Curriculum>(StringComparer.Ordinal);
            if (list.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT " + _Select + " FROM curricula c WHERE " + Scope("c") + " AND c.id IN " + D.InList(list) + ";", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows)
            {
                Curriculum c = Map(row);
                result[c.Id] = c;
            }
            return result;
        }

        public async Task<EnumerationResult<Curriculum>> EnumerateAsync(EnumerationRequest request, CurriculumFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = new List<string>();
            if (filter.Statuses.Count > 0) conditions.Add("c.status IN " + D.InList(filter.Statuses.Distinct().Select(s => s.ToString())));
            if (filter.Visibilities.Count > 0) conditions.Add("c.visibility IN " + D.InList(filter.Visibilities.Distinct().Select(v => v.ToString())));
            if (!String.IsNullOrEmpty(filter.FieldKey)) conditions.Add("lower(c.fieldkey) = " + D.Str(filter.FieldKey.ToLowerInvariant()));
            return await EnumerateAsync(request, _Select, "curricula c", conditions, Map, token).ConfigureAwait(false);
        }

        public async Task<List<CurriculumItem>> ReadItemsAsync(string curriculumId, CancellationToken token = default)
        {
            RequireText(curriculumId, nameof(curriculumId));
            DataTable table = await QueryAsync("SELECT * FROM curriculumitems WHERE " + Scope() + " AND curriculumid = " + D.Str(curriculumId) + " ORDER BY position, id LIMIT 500;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapItem).ToList();
        }

        public async Task ReplaceItemsAsync(string curriculumId, List<CurriculumItem> items, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(curriculumId, nameof(curriculumId));
            ArgumentNullException.ThrowIfNull(items);
            string where = Scope() + " AND curriculumid = " + D.Str(curriculumId);
            List<string> keep = items.Where(i => !String.IsNullOrEmpty(i.Id)).Select(i => i.Id).ToList();
            string notKept = keep.Count == 0 ? String.Empty : " AND id NOT IN " + D.InList(keep);
            string removed = "SELECT id FROM curriculumitems WHERE " + where + notKept;
            List<string> statements = new List<string>();
            statements.AddRange(Driver.Metadata.DeleteWhereStatements(TargetTypeEnum.CurriculumItem, removed));
            statements.AddRange(Driver.Metadata.DeleteWhereStatements(TargetTypeEnum.CurriculumProgress, "SELECT id FROM curriculumprogress WHERE " + Scope() + " AND itemid IN (" + removed + ")"));
            statements.Add("DELETE FROM curriculumprogress WHERE " + Scope() + " AND itemid IN (" + removed + ");");
            statements.Add("DELETE FROM curriculumitems WHERE " + where + notKept + ";");
            for (int i = 0; i < items.Count; i++)
            {
                CurriculumItem item = items[i];
                statements.Add("INSERT INTO curriculumitems (propertyid, id, curriculumid, optionkey, itemtype, targetid, customtext, position, required, createdutc, updatedutc) VALUES (" +
                    Pid + ", " + D.Str(item.Id) + ", " + D.Str(curriculumId) + ", " + D.Str(item.OptionKey) + ", " + D.Enum(item.ItemType) + ", " + D.Str(item.TargetId) + ", " + D.Str(item.CustomText) + ", " +
                    D.Num(i) + ", " + D.Bool(item.Required) + ", " + D.Ts(item.CreatedUtc) + ", " + D.Ts(nowUtc) + ") ON CONFLICT (id) DO UPDATE SET optionkey = EXCLUDED.optionkey, itemtype = EXCLUDED.itemtype, " +
                    "targetid = EXCLUDED.targetid, customtext = EXCLUDED.customtext, position = EXCLUDED.position, required = EXCLUDED.required, updatedutc = EXCLUDED.updatedutc " +
                    "WHERE curriculumitems.propertyid = " + Pid + " AND curriculumitems.curriculumid = " + D.Str(curriculumId) + ";");
            }
            statements.Add("UPDATE curricula SET updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(curriculumId) + ";");
            await TransactionAsync(statements, token).ConfigureAwait(false);
        }

        public async Task<Dictionary<string, CurriculumProgress>> ReadProgressAsync(string curriculumId, string userId, CancellationToken token = default)
        {
            RequireText(curriculumId, nameof(curriculumId));
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("SELECT * FROM curriculumprogress WHERE " + Scope() + " AND curriculumid = " + D.Str(curriculumId) + " AND userid = " + D.Str(userId) + " LIMIT 500;", token).ConfigureAwait(false);
            Dictionary<string, CurriculumProgress> result = new Dictionary<string, CurriculumProgress>(StringComparer.Ordinal);
            foreach (DataRow row in table.Rows)
            {
                CurriculumProgress p = MapProgress(row);
                result[p.ItemId] = p;
            }
            return result;
        }

        public async Task UpsertProgressAsync(CurriculumProgress progress, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(progress);
            await QueryAsync("INSERT INTO curriculumprogress (propertyid, id, curriculumid, itemid, userid, status, verifiedby, verifiedutc, createdutc, updatedutc) VALUES (" +
                Pid + ", " + D.Str(progress.Id) + ", " + D.Str(progress.CurriculumId) + ", " + D.Str(progress.ItemId) + ", " + D.Str(progress.UserId) + ", " + D.Enum(progress.Status) + ", " +
                D.Str(progress.VerifiedBy) + ", " + D.Ts(progress.VerifiedUtc) + ", " + D.Ts(progress.CreatedUtc) + ", " + D.Ts(progress.UpdatedUtc) +
                ") ON CONFLICT (propertyid, itemid, userid) DO UPDATE SET status = EXCLUDED.status, verifiedby = EXCLUDED.verifiedby, verifiedutc = EXCLUDED.verifiedutc, updatedutc = EXCLUDED.updatedutc;", token).ConfigureAwait(false);
        }

        public async Task<bool> DeleteProgressAsync(string itemId, string userId, CancellationToken token = default)
        {
            RequireText(itemId, nameof(itemId));
            RequireText(userId, nameof(userId));
            string match = Scope() + " AND itemid = " + D.Str(itemId) + " AND userid = " + D.Str(userId);
            List<string> statements = Driver.Metadata.DeleteWhereStatements(TargetTypeEnum.CurriculumProgress, "SELECT id FROM curriculumprogress WHERE " + match);
            statements.Add("DELETE FROM curriculumprogress WHERE " + match + " RETURNING id;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<HashSet<string>> CompletedLessonIdsAsync(string userId, IEnumerable<string> lessonIds, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            ArgumentNullException.ThrowIfNull(lessonIds);
            List<string> ids = DirectorySql.Distinct(lessonIds).Take(1000).ToList();
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            if (ids.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT lessonid FROM lessonprogress WHERE " + Scope() + " AND userid = " + D.Str(userId) + " AND completed = " + D.Bool(true) +
                " AND lessonid IN " + D.InList(ids) + ";", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows) result.Add(RowReader.GetString(row, "lessonid"));
            return result;
        }

        public async Task<HashSet<string>> CompletedSeriesIdsAsync(string userId, IEnumerable<string> seriesIds, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            ArgumentNullException.ThrowIfNull(seriesIds);
            List<string> ids = DirectorySql.Distinct(seriesIds).Take(1000).ToList();
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            if (ids.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT l.seriesid FROM lessons l LEFT JOIN lessonprogress lp ON lp.propertyid = l.propertyid AND lp.lessonid = l.id AND lp.userid = " + D.Str(userId) +
                " AND lp.completed = " + D.Bool(true) + " WHERE " + Scope("l") + " AND l.seriesid IN " + D.InList(ids) + " AND l.status = 'Published' AND l.deletedutc IS NULL" +
                " GROUP BY l.seriesid HAVING COUNT(*) > 0 AND COUNT(lp.id) = COUNT(*);", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows) result.Add(RowReader.GetString(row, "seriesid"));
            return result;
        }

        public async Task<Dictionary<string, string>> TargetTitlesAsync(CurriculumItemTypeEnum type, IEnumerable<string> ids, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
            string? table = TableOf(type);
            List<string> list = DirectorySql.Distinct(ids).Take(1000).ToList();
            if (table == null || list.Count == 0) return result;
            DataTable rows = await QueryAsync("SELECT id, title FROM " + table + " WHERE " + Scope() + " AND id IN " + D.InList(list) + " AND status = 'Published' AND deletedutc IS NULL;", token).ConfigureAwait(false);
            foreach (DataRow row in rows.Rows) result[RowReader.GetString(row, "id")] = RowReader.GetString(row, "title");
            return result;
        }

        public async Task<HashSet<string>> ExistingTargetsAsync(CurriculumItemTypeEnum type, IEnumerable<string> ids, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            string? table = TableOf(type);
            List<string> list = DirectorySql.Distinct(ids).Take(1000).ToList();
            if (table == null || list.Count == 0) return result;
            DataTable rows = await QueryAsync("SELECT id FROM " + table + " WHERE " + Scope() + " AND id IN " + D.InList(list) + " AND deletedutc IS NULL;", token).ConfigureAwait(false);
            foreach (DataRow row in rows.Rows) result.Add(RowReader.GetString(row, "id"));
            return result;
        }

        public async Task<List<CurriculumProgress>> ListProgressForUserAsync(string userId, string? afterId, int limit, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            string after = String.IsNullOrEmpty(afterId) ? String.Empty : " AND id > " + D.Str(afterId);
            DataTable table = await QueryAsync("SELECT * FROM curriculumprogress WHERE " + Scope() + " AND userid = " + D.Str(userId) + after + " ORDER BY id LIMIT " + D.Num(Math.Clamp(limit, 1, 1000)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapProgress).ToList();
        }

        public async Task EraseForUserAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            string match = Scope() + " AND userid = " + D.Str(userId);
            List<string> statements = Driver.Metadata.DeleteWhereStatements(TargetTypeEnum.CurriculumProgress, "SELECT id FROM curriculumprogress WHERE " + match);
            statements.Add("DELETE FROM curriculumprogress WHERE " + match + ";");
            await TransactionAsync(statements, token).ConfigureAwait(false);
        }

        private static string? TableOf(CurriculumItemTypeEnum type)
        {
            if (type == CurriculumItemTypeEnum.Lesson) return "lessons";
            if (type == CurriculumItemTypeEnum.Series) return "series";
            return null;
        }

        private static Curriculum Map(DataRow row)
        {
            return new Curriculum
            {
                Id = RowReader.GetString(row, "id"),
                Name = RowReader.GetString(row, "name"),
                Description = RowReader.GetNullableString(row, "description"),
                FieldKey = RowReader.GetNullableString(row, "fieldkey"),
                Visibility = RowReader.GetEnum(row, "visibility", CurriculumVisibilityEnum.Members),
                Status = RowReader.GetEnum(row, "status", CurriculumStatusEnum.Draft),
                Position = RowReader.GetInt(row, "position"),
                ItemCount = (int)RowReader.GetLong(row, "itemcount"),
                CreatedByUserId = RowReader.GetNullableString(row, "createdbyuserid"),
                PublishedUtc = RowReader.GetNullableDateTime(row, "publishedutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        private static CurriculumItem MapItem(DataRow row)
        {
            return new CurriculumItem
            {
                Id = RowReader.GetString(row, "id"),
                CurriculumId = RowReader.GetString(row, "curriculumid"),
                OptionKey = RowReader.GetNullableString(row, "optionkey"),
                ItemType = RowReader.GetEnum(row, "itemtype", CurriculumItemTypeEnum.Custom),
                TargetId = RowReader.GetNullableString(row, "targetid"),
                CustomText = RowReader.GetNullableString(row, "customtext"),
                Position = RowReader.GetInt(row, "position"),
                Required = RowReader.GetBool(row, "required"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        private static CurriculumProgress MapProgress(DataRow row)
        {
            return new CurriculumProgress
            {
                Id = RowReader.GetString(row, "id"),
                CurriculumId = RowReader.GetString(row, "curriculumid"),
                ItemId = RowReader.GetString(row, "itemid"),
                UserId = RowReader.GetString(row, "userid"),
                Status = RowReader.GetEnum(row, "status", CurriculumProgressStatusEnum.SelfComplete),
                VerifiedBy = RowReader.GetNullableString(row, "verifiedby"),
                VerifiedUtc = RowReader.GetNullableDateTime(row, "verifiedutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }
    }
}
