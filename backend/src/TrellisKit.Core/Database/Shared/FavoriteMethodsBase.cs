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
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    internal abstract class FavoriteMethodsBase : MethodsBase, IFavoriteMethods
    {
        private const int _MaxTargets = 100;

        protected FavoriteMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<FavoriteUpsertResult> UpsertAsync(Favorite favorite, bool setNote, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(favorite);
            RequireText(favorite.Id, nameof(favorite.Id));
            RequireText(favorite.UserId, nameof(favorite.UserId));
            RequireText(favorite.TargetId, nameof(favorite.TargetId));
            string sql = "INSERT INTO favorites (id, propertyid, userid, targettype, targetid, note, createdutc, lastupdateutc) VALUES (" +
                D.Str(favorite.Id) + ", " + Pid + ", " + D.Str(favorite.UserId) + ", " + D.Enum(favorite.TargetType) + ", " + D.Str(favorite.TargetId) + ", " +
                D.Str(favorite.Note) + ", " + D.Ts(favorite.CreatedUtc) + ", " + D.Ts(favorite.LastUpdateUtc) + ") " +
                "ON CONFLICT (propertyid, userid, targettype, targetid) DO UPDATE SET lastupdateutc = EXCLUDED.lastupdateutc" +
                (setNote ? ", note = EXCLUDED.note" : String.Empty) + " RETURNING *, (xmax = 0) AS inserted;";
            DataTable table = await QueryAsync(sql, token).ConfigureAwait(false);
            DataRow row = table.Rows[0];
            Favorite stored = Map(row);
            Dictionary<string, List<string>> collections = await CollectionsOfAsync(new[] { stored.Id }, token).ConfigureAwait(false);
            if (collections.TryGetValue(stored.Id, out List<string>? ids)) stored.CollectionIds = ids;
            return new FavoriteUpsertResult { Favorite = stored, Created = RowReader.GetBool(row, "inserted") };
        }

        public async Task<Favorite?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            List<Favorite> rows = await LoadAsync("f.id = " + D.Str(id), 1, token).ConfigureAwait(false);
            await FillCollectionsAsync(rows, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        public async Task<Favorite?> ReadByTargetAsync(string userId, FavoriteTargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId) || String.IsNullOrEmpty(targetId)) return null;
            List<Favorite> rows = await LoadAsync("f.userid = " + D.Str(userId) + " AND f.targettype = " + D.Enum(targetType) + " AND f.targetid = " + D.Str(targetId), 1, token).ConfigureAwait(false);
            await FillCollectionsAsync(rows, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        public async Task<bool> DeleteAsync(string userId, FavoriteTargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            RequireText(targetId, nameof(targetId));
            int deleted = await DeleteWhereAsync("userid = " + D.Str(userId) + " AND targettype = " + D.Enum(targetType) + " AND targetid = " + D.Str(targetId), token).ConfigureAwait(false);
            return deleted > 0;
        }

        public async Task<EnumerationResult<Favorite>> EnumerateAsync(EnumerationRequest request, FavoriteFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            RequireText(filter.UserId, nameof(filter.UserId));
            List<string> conditions = new List<string> { "f.userid = " + D.Str(filter.UserId) };
            if (filter.TargetTypes.Count > 0) conditions.Add("f.targettype IN " + D.InList(filter.TargetTypes.Distinct().Select(t => t.ToString())));
            if (!String.IsNullOrEmpty(filter.CollectionId))
                conditions.Add("EXISTS (SELECT 1 FROM favoritecollectionitems ci WHERE " + Scope("ci") + " AND ci.collectionid = " + D.Str(filter.CollectionId) + " AND ci.favoriteid = f.id)");
            EnumerationResult<Favorite> page = await EnumerateAsync(request, "f.*", "favorites f", conditions, Map, token).ConfigureAwait(false);
            await FillCollectionsAsync(page.Objects, token).ConfigureAwait(false);
            return page;
        }

        public async Task<List<Favorite>> StateAsync(string userId, IReadOnlyList<FavoriteTargetKey> targets, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            ArgumentNullException.ThrowIfNull(targets);
            if (targets.Count > _MaxTargets) throw new ArgumentException("At most " + _MaxTargets + " targets.", nameof(targets));
            List<string> pairs = targets.Where(t => !String.IsNullOrEmpty(t.TargetId))
                .Select(t => "(" + D.Enum(t.TargetType) + ", " + D.Str(t.TargetId) + ")").Distinct(StringComparer.Ordinal).ToList();
            if (pairs.Count == 0) return new List<Favorite>();
            return await LoadAsync("f.userid = " + D.Str(userId) + " AND (f.targettype, f.targetid) IN (" + String.Join(", ", pairs) + ")", _MaxTargets, token).ConfigureAwait(false);
        }

        public Task<long> CountForUserAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            return CountAsync("SELECT COUNT(*) AS cnt FROM favorites WHERE " + Scope() + " AND userid = " + D.Str(userId) + ";", token);
        }

        public async Task<bool> SetCollectionsAsync(string userId, string favoriteId, IReadOnlyList<string> collectionIds, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            RequireText(favoriteId, nameof(favoriteId));
            ArgumentNullException.ThrowIfNull(collectionIds);
            List<string> ids = collectionIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            string user = D.Str(userId);
            string fav = D.Str(favoriteId);
            string owned = "EXISTS (SELECT 1 FROM favorites fx WHERE " + Scope("fx") + " AND fx.id = " + fav + " AND fx.userid = " + user + ")";
            string body = "DELETE FROM favoritecollectionitems WHERE " + Scope() + " AND favoriteid = " + fav + " AND " + owned +
                (ids.Count == 0 ? String.Empty : " AND collectionid NOT IN " + D.InList(ids)) + "; ";
            if (ids.Count > 0)
            {
                body += "INSERT INTO favoritecollectionitems (collectionid, favoriteid, propertyid, position) SELECT c.id, f.id, " + Pid +
                    ", COALESCE((SELECT MAX(i.position) FROM favoritecollectionitems i WHERE " + Scope("i") + " AND i.collectionid = c.id), -1) + 1" +
                    " FROM favoritecollections c JOIN favorites f ON f.propertyid = c.propertyid AND f.userid = c.userid WHERE " + Scope("c") + " AND " + Scope("f") +
                    " AND c.userid = " + user + " AND f.userid = " + user + " AND f.id = " + fav + " AND c.id IN " + D.InList(ids) +
                    " ON CONFLICT (collectionid, favoriteid) DO NOTHING; ";
            }
            body += "SELECT COUNT(*) INTO n FROM favoritecollectionitems WHERE " + Scope() + " AND favoriteid = " + fav + "; " +
                "IF n <> " + D.Num(ids.Count) + " OR NOT " + owned + " THEN RAISE EXCEPTION 'Collection not found' USING ERRCODE = '23505'; END IF; ";
            try
            {
                await UniqueAsync("DO $favorite_items$ DECLARE n integer; BEGIN " + body + "END $favorite_items$;", "A collection membership", token).ConfigureAwait(false);
                return true;
            }
            catch (DuplicateRecordException)
            {
                return false;
            }
        }

        public async Task<FavoriteCollection> CreateCollectionAsync(FavoriteCollection collection, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(collection);
            RequireText(collection.Id, nameof(collection.Id));
            RequireText(collection.UserId, nameof(collection.UserId));
            RequireText(collection.Name, nameof(collection.Name));
            string user = D.Str(collection.UserId);
            string sql = "INSERT INTO favoritecollections (id, propertyid, userid, name, position, createdutc, lastupdateutc) VALUES (" + D.Str(collection.Id) + ", " + Pid + ", " + user + ", " +
                D.Str(collection.Name) + ", COALESCE((SELECT MAX(x.position) FROM favoritecollections x WHERE " + Scope("x") + " AND x.userid = " + user + "), -1) + 1, " +
                D.Ts(collection.CreatedUtc) + ", " + D.Ts(collection.LastUpdateUtc) + ") RETURNING *, 0 AS itemcount;";
            DataTable table = await UniqueAsync(sql, "A collection with this name", token).ConfigureAwait(false);
            return MapCollection(table.Rows[0]);
        }

        public async Task<FavoriteCollection?> ReadCollectionAsync(string userId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId) || String.IsNullOrEmpty(id)) return null;
            List<FavoriteCollection> rows = await LoadCollectionsAsync("c.userid = " + D.Str(userId) + " AND c.id = " + D.Str(id), 1, token).ConfigureAwait(false);
            return rows.FirstOrDefault();
        }

        public async Task<List<FavoriteCollection>> ReadCollectionsAsync(string userId, IEnumerable<string> ids, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            ArgumentNullException.ThrowIfNull(ids);
            List<string> list = ids.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).Take(500).ToList();
            if (list.Count == 0) return new List<FavoriteCollection>();
            return await LoadCollectionsAsync("c.userid = " + D.Str(userId) + " AND c.id IN " + D.InList(list), 500, token).ConfigureAwait(false);
        }

        public async Task<List<string>> CollectionIdsAsync(string userId, int limit, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("SELECT id FROM favoritecollections WHERE " + Scope() + " AND userid = " + D.Str(userId) +
                " ORDER BY position, id LIMIT " + D.Num(Math.Clamp(limit, 1, 1000)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "id")).ToList();
        }

        public async Task<bool> RenameCollectionAsync(string userId, string id, string name, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            RequireText(id, nameof(id));
            RequireText(name, nameof(name));
            DataTable table = await UniqueAsync("UPDATE favoritecollections SET name = " + D.Str(name) + ", lastupdateutc = " + D.Ts(nowUtc) + " WHERE " + Scope() +
                " AND userid = " + D.Str(userId) + " AND id = " + D.Str(id) + " RETURNING id;", "A collection with this name", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> DeleteCollectionAsync(string userId, string id, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            RequireText(id, nameof(id));
            string condition = "userid = " + D.Str(userId) + " AND id = " + D.Str(id);
            List<string> statements = MetadataDeletes(TargetTypeEnum.FavoriteCollection, "favoritecollections", condition);
            statements.Add("DELETE FROM favoritecollectionitems WHERE " + Scope() + " AND collectionid IN (SELECT id FROM favoritecollections WHERE " + Scope() + " AND " + condition + ");");
            statements.Add("DELETE FROM favoritecollections WHERE " + Scope() + " AND " + condition + " RETURNING id;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task ReorderCollectionsAsync(string userId, IReadOnlyList<string> ids, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            ArgumentNullException.ThrowIfNull(ids);
            if (ids.Count == 0) return;
            string cases = String.Join(" ", ids.Select((id, index) => "WHEN " + D.Str(id) + " THEN " + D.Num(index)));
            await QueryAsync("UPDATE favoritecollections SET position = CASE id " + cases + " ELSE position END, lastupdateutc = " + D.Ts(nowUtc) + " WHERE " + Scope() +
                " AND userid = " + D.Str(userId) + " AND id IN " + D.InList(ids) + ";", token).ConfigureAwait(false);
        }

        public Task<long> CountCollectionsAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            return CountAsync("SELECT COUNT(*) AS cnt FROM favoritecollections WHERE " + Scope() + " AND userid = " + D.Str(userId) + ";", token);
        }

        public Task<EnumerationResult<FavoriteCollection>> EnumerateCollectionsAsync(EnumerationRequest request, string userId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            RequireText(userId, nameof(userId));
            List<string> conditions = new List<string> { "c.userid = " + D.Str(userId) };
            return EnumerateAsync(request, "c.*, " + ItemCount(), "favoritecollections c", conditions, MapCollection, token);
        }

        public Task<int> DeleteForTargetAsync(FavoriteTargetTypeEnum targetType, IEnumerable<string> targetIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(targetIds);
            List<string> ids = targetIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).Take(1000).ToList();
            if (ids.Count == 0) return Task.FromResult(0);
            return DeleteWhereAsync("targettype = " + D.Enum(targetType) + " AND targetid IN " + D.InList(ids), token);
        }

        public async Task<int> DeleteForUserAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            string condition = "userid = " + D.Str(userId);
            List<string> statements = MetadataDeletes(TargetTypeEnum.FavoriteCollection, "favoritecollections", condition);
            statements.AddRange(MetadataDeletes(TargetTypeEnum.Favorite, "favorites", condition));
            statements.Add("DELETE FROM favoritecollectionitems WHERE " + Scope() + " AND collectionid IN (SELECT id FROM favoritecollections WHERE " + Scope() + " AND " + condition + ");");
            statements.Add("DELETE FROM favoritecollections WHERE " + Scope() + " AND " + condition + ";");
            statements.Add("DELETE FROM favorites WHERE " + Scope() + " AND " + condition + " RETURNING id;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public async Task<List<Favorite>> ReadForUserAsync(string userId, string? afterId, int limit, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            List<Favorite> rows = await LoadAsync("f.userid = " + D.Str(userId) + (String.IsNullOrEmpty(afterId) ? String.Empty : " AND f.id > " + D.Str(afterId)), Math.Clamp(limit, 1, 1000), token).ConfigureAwait(false);
            await FillCollectionsAsync(rows, token).ConfigureAwait(false);
            return rows;
        }

        public Task<List<FavoriteCollection>> ReadCollectionsForUserAsync(string userId, string? afterId, int limit, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            return LoadCollectionsAsync("c.userid = " + D.Str(userId) + (String.IsNullOrEmpty(afterId) ? String.Empty : " AND c.id > " + D.Str(afterId)), Math.Clamp(limit, 1, 1000), token);
        }

        private async Task<int> DeleteWhereAsync(string condition, CancellationToken token)
        {
            List<string> statements = MetadataDeletes(TargetTypeEnum.Favorite, "favorites", condition);
            statements.Add("DELETE FROM favoritecollectionitems WHERE " + Scope() + " AND favoriteid IN (SELECT id FROM favorites WHERE " + Scope() + " AND " + condition + ");");
            statements.Add("DELETE FROM favorites WHERE " + Scope() + " AND " + condition + " RETURNING id;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        private List<string> MetadataDeletes(TargetTypeEnum type, string table, string condition)
        {
            string ids = "(SELECT id FROM " + table + " WHERE " + Scope() + " AND " + condition + ")";
            return new List<string>
            {
                "DELETE FROM labels WHERE " + Scope() + " AND targettype = " + D.Enum(type) + " AND targetid IN " + ids + ";",
                "DELETE FROM tags WHERE " + Scope() + " AND targettype = " + D.Enum(type) + " AND targetid IN " + ids + ";"
            };
        }

        private string ItemCount()
        {
            return "(SELECT COUNT(*) FROM favoritecollectionitems ci WHERE " + Scope("ci") + " AND ci.collectionid = c.id) AS itemcount";
        }

        private async Task<List<Favorite>> LoadAsync(string condition, int limit, CancellationToken token)
        {
            DataTable table = await QueryAsync("SELECT f.* FROM favorites f WHERE " + Scope("f") + " AND " + condition + " ORDER BY f.id LIMIT " + D.Num(limit) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        private async Task<List<FavoriteCollection>> LoadCollectionsAsync(string condition, int limit, CancellationToken token)
        {
            DataTable table = await QueryAsync("SELECT c.*, " + ItemCount() + " FROM favoritecollections c WHERE " + Scope("c") + " AND " + condition +
                " ORDER BY c.id LIMIT " + D.Num(limit) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapCollection).ToList();
        }

        private async Task FillCollectionsAsync(List<Favorite> favorites, CancellationToken token)
        {
            if (favorites.Count == 0) return;
            Dictionary<string, List<string>> map = await CollectionsOfAsync(favorites.Select(f => f.Id), token).ConfigureAwait(false);
            foreach (Favorite favorite in favorites)
            {
                if (map.TryGetValue(favorite.Id, out List<string>? ids)) favorite.CollectionIds = ids;
            }
        }

        private async Task<Dictionary<string, List<string>>> CollectionsOfAsync(IEnumerable<string> favoriteIds, CancellationToken token)
        {
            Dictionary<string, List<string>> result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            List<string> ids = favoriteIds.Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT ci.favoriteid, ci.collectionid FROM favoritecollectionitems ci JOIN favoritecollections c ON c.id = ci.collectionid AND " + Scope("c") +
                " WHERE " + Scope("ci") + " AND ci.favoriteid IN " + D.InList(ids) + " ORDER BY c.position, c.id;", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows)
            {
                string favoriteId = RowReader.GetString(row, "favoriteid");
                if (!result.TryGetValue(favoriteId, out List<string>? list))
                {
                    list = new List<string>();
                    result[favoriteId] = list;
                }
                list.Add(RowReader.GetString(row, "collectionid"));
            }
            return result;
        }

        private static Favorite Map(DataRow row)
        {
            return new Favorite
            {
                Id = RowReader.GetString(row, "id"),
                UserId = RowReader.GetString(row, "userid"),
                TargetType = RowReader.GetEnum(row, "targettype", FavoriteTargetTypeEnum.Lesson),
                TargetId = RowReader.GetString(row, "targetid"),
                Note = RowReader.GetNullableString(row, "note"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                LastUpdateUtc = RowReader.GetDateTime(row, "lastupdateutc")
            };
        }

        private static FavoriteCollection MapCollection(DataRow row)
        {
            return new FavoriteCollection
            {
                Id = RowReader.GetString(row, "id"),
                UserId = RowReader.GetString(row, "userid"),
                Name = RowReader.GetString(row, "name"),
                Position = RowReader.GetInt(row, "position"),
                ItemCount = (int)RowReader.GetLong(row, "itemcount"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                LastUpdateUtc = RowReader.GetDateTime(row, "lastupdateutc")
            };
        }
    }
}
