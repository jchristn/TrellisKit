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
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    internal abstract class ForumMethodsBase : MethodsBase, IForumMethods
    {
        protected ForumMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<Forum> CreateAsync(Forum forum, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(forum);
            RequireText(forum.Id, nameof(forum.Id));
            RequireText(forum.Slug, nameof(forum.Slug));
            List<string> statements = new List<string>
            {
                "INSERT INTO forums (propertyid, id, slug, name, description, guidelines, iconemoji, iconmediaid, color, section, sortorder, audience, threadcreation, defaultordering, allowmedia, allowvideo, " +
                "maxopenfeedbackpermember, isarchived, threadcount, commentcount, createdbyuserid, createdutc, updatedutc) VALUES (" + Pid + ", " + D.Str(forum.Id) + ", " + D.Str(forum.Slug) + ", " +
                D.Str(forum.Name) + ", " + D.Str(forum.Description) + ", " + D.Str(forum.Guidelines) + ", " + D.Str(forum.IconEmoji) + ", " + D.Str(forum.IconMediaId) + ", " + D.Str(forum.Color) + ", " +
                D.Str(forum.Section) + ", " + D.Num(forum.SortOrder) + ", " + D.Enum(forum.Audience) + ", " + D.Enum(forum.ThreadCreation) + ", " + D.Enum(forum.DefaultOrdering) + ", " +
                D.Bool(forum.AllowMedia) + ", " + D.Bool(forum.AllowVideo) + ", " + D.Num(forum.MaxOpenFeedbackPerMember) + ", " + D.Bool(false) + ", 0, 0, " + D.Str(forum.CreatedByUserId) + ", " +
                D.Ts(forum.CreatedUtc) + ", " + D.Ts(forum.UpdatedUtc) + ");"
            };
            statements.AddRange(TypeStatements(forum));
            await UniqueTransactionAsync(statements, "A forum with this slug", token).ConfigureAwait(false);
            return (await ReadAsync(forum.Id, token).ConfigureAwait(false))!;
        }

        public async Task<Forum?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            List<Forum> forums = await LoadAsync("fo.id = " + D.Str(id) + " AND fo.deletedutc IS NULL", token).ConfigureAwait(false);
            return forums.FirstOrDefault();
        }

        public async Task<Forum?> ReadBySlugAsync(string slug, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(slug)) return null;
            List<Forum> forums = await LoadAsync("lower(fo.slug) = " + D.Str(slug.Trim().ToLowerInvariant()) + " AND fo.deletedutc IS NULL", token).ConfigureAwait(false);
            return forums.FirstOrDefault();
        }

        public async Task<List<Forum>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            List<string> list = ids.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (list.Count == 0) return new List<Forum>();
            return await LoadAsync("fo.id IN " + D.InList(list), token).ConfigureAwait(false);
        }

        public async Task<Forum> UpdateAsync(Forum forum, bool narrowThreads, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(forum);
            RequireText(forum.Id, nameof(forum.Id));
            string id = D.Str(forum.Id);
            List<string> statements = new List<string>
            {
                "UPDATE forums SET slug = " + D.Str(forum.Slug) + ", name = " + D.Str(forum.Name) + ", description = " + D.Str(forum.Description) + ", guidelines = " + D.Str(forum.Guidelines) +
                    ", iconemoji = " + D.Str(forum.IconEmoji) + ", iconmediaid = " + D.Str(forum.IconMediaId) + ", color = " + D.Str(forum.Color) + ", section = " + D.Str(forum.Section) +
                    ", audience = " + D.Enum(forum.Audience) + ", threadcreation = " + D.Enum(forum.ThreadCreation) + ", defaultordering = " + D.Enum(forum.DefaultOrdering) +
                    ", allowmedia = " + D.Bool(forum.AllowMedia) + ", allowvideo = " + D.Bool(forum.AllowVideo) + ", maxopenfeedbackpermember = " + D.Num(forum.MaxOpenFeedbackPerMember) +
                    ", updatedutc = " + D.Ts(forum.UpdatedUtc) + " WHERE " + Scope() + " AND id = " + id + " AND deletedutc IS NULL;",
                "DELETE FROM forumthreadtypes WHERE " + Scope() + " AND forumid = " + id + ";"
            };
            statements.AddRange(TypeStatements(forum));
            if (narrowThreads)
            {
                statements.Add("UPDATE forumthreads SET audience = " + D.Enum(forum.Audience) + ", updatedutc = " + D.Ts(forum.UpdatedUtc) + " WHERE " + Scope() + " AND forumid = " + id + " AND " +
                    ForumCounterSql.AudienceRank("audience") + " < " + ForumCounterSql.AudienceRank(D.Enum(forum.Audience)) + ";");
            }
            await UniqueTransactionAsync(statements, "A forum with this slug", token).ConfigureAwait(false);
            return (await ReadAsync(forum.Id, token).ConfigureAwait(false)) ?? forum;
        }

        public Task<long> CountAsync(CancellationToken token = default)
        {
            return CountAsync("SELECT COUNT(*) AS cnt FROM forums WHERE " + Scope() + " AND deletedutc IS NULL;", token);
        }

        public async Task<int> NextSortOrderAsync(CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT COALESCE(MAX(sortorder), 0) AS m FROM forums WHERE " + Scope() + " AND deletedutc IS NULL;", token).ConfigureAwait(false);
            return (table.Rows.Count == 0 ? 0 : RowReader.GetInt(table.Rows[0], "m")) + 10;
        }

        public async Task<List<string>> ActiveIdsAsync(int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT id FROM forums WHERE " + Scope() + " AND deletedutc IS NULL AND isarchived = " + D.Bool(false) + " ORDER BY sortorder, id LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "id")).ToList();
        }

        public async Task<int> ReorderAsync(List<string> forumIds, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(forumIds);
            if (forumIds.Count == 0) return 0;
            List<string> statements = new List<string>();
            for (int i = 0; i < forumIds.Count; i++)
            {
                statements.Add("UPDATE forums SET sortorder = " + D.Num((i + 1) * 10) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(forumIds[i]) + " AND deletedutc IS NULL;");
            }
            statements.Add("SELECT COUNT(*) AS cnt FROM forums WHERE " + Scope() + " AND id IN " + D.InList(forumIds) + " AND deletedutc IS NULL;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? 0 : (int)RowReader.GetLong(table.Rows[0], "cnt");
        }

        public async Task<bool> SetArchivedAsync(string id, bool archived, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("UPDATE forums SET isarchived = " + D.Bool(archived) + ", archivedutc = " + (archived ? "COALESCE(archivedutc, " + D.Ts(nowUtc) + ")" : "NULL") +
                ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND deletedutc IS NULL RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> DeleteAsync(string id, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            string fid = D.Str(id);
            string empty = "NOT EXISTS (SELECT 1 FROM forumthreads t WHERE " + Scope("t") + " AND t.forumid = " + fid + " AND t.deletedutc IS NULL)";
            List<string> statements = new List<string>
            {
                "UPDATE forums SET deletedutc = " + D.Ts(nowUtc) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + fid + " AND deletedutc IS NULL AND " + empty + ";"
            };
            string deleted = "EXISTS (SELECT 1 FROM forums d WHERE " + Scope("d") + " AND d.id = " + fid + " AND d.deletedutc = " + D.Ts(nowUtc) + ")";
            statements.Add("DELETE FROM forumthreadtypes WHERE " + Scope() + " AND forumid = " + fid + " AND " + deleted + ";");
            statements.Add("DELETE FROM forumfollows WHERE " + Scope() + " AND forumid = " + fid + " AND " + deleted + ";");
            statements.Add("DELETE FROM forumreads WHERE " + Scope() + " AND forumid = " + fid + " AND " + deleted + ";");
            statements.Add("SELECT id FROM forums WHERE " + Scope() + " AND id = " + fid + " AND deletedutc = " + D.Ts(nowUtc) + ";");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return false;
            await TransactionAsync(Driver.Metadata.DeleteStatements(TargetTypeEnum.Forum, id), token).ConfigureAwait(false);
            return true;
        }

        public async Task<bool> HasThreadsAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            long count = await CountAsync("SELECT COUNT(*) AS cnt FROM forumthreads t WHERE " + Scope("t") + " AND t.forumid = " + D.Str(id) + " AND t.deletedutc IS NULL;", token).ConfigureAwait(false);
            return count > 0;
        }

        public async Task<EnumerationResult<Forum>> EnumerateAsync(EnumerationRequest request, ForumFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = new List<string> { "fo.deletedutc IS NULL" };
            if (filter.IsArchived != null) conditions.Add("fo.isarchived = " + D.Bool(filter.IsArchived.Value));
            else if (!filter.IncludeArchived) conditions.Add("fo.isarchived = " + D.Bool(false));
            if (!String.IsNullOrEmpty(filter.Section)) conditions.Add("fo.section = " + D.Str(filter.Section));
            if (!String.IsNullOrEmpty(filter.FollowedByUserId))
                conditions.Add("EXISTS (SELECT 1 FROM forumfollows ff WHERE " + Scope("ff") + " AND ff.forumid = fo.id AND ff.userid = " + D.Str(filter.FollowedByUserId) + ")");
            if (filter.Audiences != null) conditions.Add(filter.Audiences.Count == 0 ? "1 = 0" : "fo.audience IN " + D.InList(filter.Audiences.Select(a => a.ToString())));
            if (filter.Audience != null) conditions.Add("fo.audience = " + D.Enum(filter.Audience.Value));
            EnumerationResult<Forum> page = await EnumerateAsync(request, "fo.*", "forums fo", conditions, Map, token).ConfigureAwait(false);
            await FillTypesAsync(page.Objects, token).ConfigureAwait(false);
            return page;
        }

        public async Task<bool> FollowAsync(ForumFollow follow, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(follow);
            RequireText(follow.ForumId, nameof(follow.ForumId));
            RequireText(follow.UserId, nameof(follow.UserId));
            DataTable table = await QueryAsync("INSERT INTO forumfollows (propertyid, id, forumid, userid, level, createdutc) VALUES (" + Pid + ", " + D.Str(follow.Id) + ", " + D.Str(follow.ForumId) + ", " +
                D.Str(follow.UserId) + ", " + D.Str(follow.Level) + ", " + D.Ts(follow.CreatedUtc) + ") ON CONFLICT (propertyid, forumid, userid) DO UPDATE SET level = excluded.level RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> UnfollowAsync(string forumId, string userId, CancellationToken token = default)
        {
            RequireText(forumId, nameof(forumId));
            RequireText(userId, nameof(userId));
            string where = Scope() + " AND forumid = " + D.Str(forumId) + " AND userid = " + D.Str(userId);
            List<string> statements = new List<string>();
            statements.AddRange(Driver.Metadata.DeleteWhereStatements(TargetTypeEnum.ForumFollow, "SELECT id FROM forumfollows WHERE " + where));
            statements.Add("DELETE FROM forumfollows WHERE " + where + " RETURNING id;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<HashSet<string>> FollowedAmongAsync(string userId, IEnumerable<string> forumIds, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            ArgumentNullException.ThrowIfNull(forumIds);
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            List<string> ids = forumIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT forumid FROM forumfollows WHERE " + Scope() + " AND userid = " + D.Str(userId) + " AND forumid IN " + D.InList(ids) + ";", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows) result.Add(RowReader.GetString(row, "forumid"));
            return result;
        }

        public async Task<List<string>> FollowerIdsAsync(string forumId, string? afterUserId, int limit, CancellationToken token = default)
        {
            RequireText(forumId, nameof(forumId));
            DataTable table = await QueryAsync("SELECT userid FROM forumfollows WHERE " + Scope() + " AND forumid = " + D.Str(forumId) + (afterUserId == null ? String.Empty : " AND userid > " + D.Str(afterUserId)) +
                " ORDER BY userid LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "userid")).ToList();
        }

        public async Task MarkReadAsync(string userId, string forumId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            RequireText(forumId, nameof(forumId));
            await QueryAsync("INSERT INTO forumreads (propertyid, userid, forumid, lastreadutc) VALUES (" + Pid + ", " + D.Str(userId) + ", " + D.Str(forumId) + ", " + D.Ts(nowUtc) +
                ") ON CONFLICT (propertyid, userid, forumid) DO UPDATE SET lastreadutc = GREATEST(forumreads.lastreadutc, excluded.lastreadutc);", token).ConfigureAwait(false);
        }

        public async Task<Dictionary<string, DateTime>> ReadTimesAsync(string userId, IEnumerable<string> forumIds, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            ArgumentNullException.ThrowIfNull(forumIds);
            Dictionary<string, DateTime> result = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            List<string> ids = forumIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT forumid, lastreadutc FROM forumreads WHERE " + Scope() + " AND userid = " + D.Str(userId) + " AND forumid IN " + D.InList(ids) + ";", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows) result[RowReader.GetString(row, "forumid")] = RowReader.GetDateTime(row, "lastreadutc");
            return result;
        }

        public async Task<Dictionary<string, int>> UnreadCountsAsync(string userId, IEnumerable<string> forumIds, List<ContentAudienceEnum> audiences, List<string> hiddenAuthorIds, DateTime windowStartUtc, int cap, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            ArgumentNullException.ThrowIfNull(forumIds);
            ArgumentNullException.ThrowIfNull(audiences);
            ArgumentNullException.ThrowIfNull(hiddenAuthorIds);
            Dictionary<string, int> result = new Dictionary<string, int>(StringComparer.Ordinal);
            List<string> ids = forumIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0 || audiences.Count == 0) return result;
            string user = D.Str(userId);
            List<string> conditions = new List<string>
            {
                Scope("t"),
                ForumCounterSql.Live("t"),
                "t.forumid IN " + D.InList(ids),
                "t.audience IN " + D.InList(audiences.Select(a => a.ToString())),
                "t.lastactivityutc > COALESCE(GREATEST((SELECT tr.lastreadutc FROM threadreads tr WHERE " + Scope("tr") + " AND tr.userid = " + user + " AND tr.threadid = t.id), " +
                    "(SELECT fr.lastreadutc FROM forumreads fr WHERE " + Scope("fr") + " AND fr.userid = " + user + " AND fr.forumid = t.forumid)), " + D.Ts(windowStartUtc) + ")"
            };
            if (hiddenAuthorIds.Count > 0) conditions.Add("t.authoruserid NOT IN " + D.InList(hiddenAuthorIds));
            DataTable table = await QueryAsync("SELECT t.forumid, LEAST(COUNT(*), " + D.Num(Math.Max(1, cap)) + ") AS cnt FROM forumthreads t" + Where(conditions) + " GROUP BY t.forumid;", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows) result[RowReader.GetString(row, "forumid")] = (int)RowReader.GetLong(row, "cnt");
            return result;
        }

        public async Task<ForumMute> CreateMuteAsync(ForumMute mute, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(mute);
            RequireText(mute.Id, nameof(mute.Id));
            RequireText(mute.ForumId, nameof(mute.ForumId));
            RequireText(mute.UserId, nameof(mute.UserId));
            List<string> statements = new List<string>
            {
                "UPDATE forums SET threadcount = threadcount WHERE " + Scope() + " AND id = " + D.Str(mute.ForumId) + ";",
                "UPDATE forummutes SET endedutc = " + D.Ts(mute.CreatedUtc) + ", endedbyuserid = " + D.Str(mute.MutedByUserId) + " WHERE " + Scope() + " AND forumid = " + D.Str(mute.ForumId) +
                    " AND userid = " + D.Str(mute.UserId) + " AND endedutc IS NULL;",
                "INSERT INTO forummutes (propertyid, id, forumid, userid, untilutc, reason, mutedbyuserid, createdutc) VALUES (" + Pid + ", " + D.Str(mute.Id) + ", " + D.Str(mute.ForumId) + ", " +
                    D.Str(mute.UserId) + ", " + D.Ts(mute.UntilUtc) + ", " + D.Str(mute.Reason) + ", " + D.Str(mute.MutedByUserId) + ", " + D.Ts(mute.CreatedUtc) + ");"
            };
            await TransactionAsync(statements, token).ConfigureAwait(false);
            return (await ReadMuteAsync(mute.Id, token).ConfigureAwait(false))!;
        }

        public async Task<ForumMute?> ReadMuteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            DataTable table = await QueryAsync("SELECT m.* FROM forummutes m WHERE " + Scope("m") + " AND m.id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : MapMute(table.Rows[0]);
        }

        public async Task<ForumMute?> ActiveMuteAsync(string userId, string forumId, DateTime nowUtc, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId) || String.IsNullOrEmpty(forumId)) return null;
            DataTable table = await QueryAsync("SELECT m.* FROM forummutes m WHERE " + Scope("m") + " AND m.userid = " + D.Str(userId) + " AND m.forumid = " + D.Str(forumId) + " AND " + ActiveMute("m", nowUtc) +
                " ORDER BY m.createdutc DESC LIMIT 1;", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : MapMute(table.Rows[0]);
        }

        public async Task<bool> EndMuteAsync(string id, string? byUserId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("UPDATE forummutes SET endedutc = " + D.Ts(nowUtc) + ", endedbyuserid = " + D.Str(byUserId) + " WHERE " + Scope() + " AND id = " + D.Str(id) +
                " AND endedutc IS NULL RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public Task<EnumerationResult<ForumMute>> EnumerateMutesAsync(EnumerationRequest request, ForumMuteFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = new List<string>();
            if (filter.ScopeForumIds != null) conditions.Add(filter.ScopeForumIds.Count == 0 ? "1 = 0" : "m.forumid IN " + D.InList(filter.ScopeForumIds));
            if (!String.IsNullOrEmpty(filter.ForumId)) conditions.Add("m.forumid = " + D.Str(filter.ForumId));
            if (!String.IsNullOrEmpty(filter.UserId)) conditions.Add("m.userid = " + D.Str(filter.UserId));
            if (filter.ActiveOnly) conditions.Add(ActiveMute("m", filter.NowUtc));
            return EnumerateAsync(request, "m.*", "forummutes m", conditions, MapMute, token);
        }

        public async Task<List<ForumMute>> ListMutesForUserAsync(string userId, int limit, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("SELECT m.* FROM forummutes m WHERE " + Scope("m") + " AND m.userid = " + D.Str(userId) + " ORDER BY m.createdutc DESC LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapMute).ToList();
        }

        public async Task<int> DeleteMutesForUserAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            string where = Scope() + " AND userid = " + D.Str(userId);
            List<string> statements = new List<string>();
            statements.AddRange(Driver.Metadata.DeleteWhereStatements(TargetTypeEnum.ForumMute, "SELECT id FROM forummutes WHERE " + where));
            statements.Add("DELETE FROM forummutes WHERE " + where + " RETURNING id;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public async Task<bool?> ReadAutoFollowOnCommentAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("SELECT autofollowoncomment FROM forummemberprefs WHERE " + Scope() + " AND userid = " + D.Str(userId) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : RowReader.GetBool(table.Rows[0], "autofollowoncomment");
        }

        public async Task WriteAutoFollowOnCommentAsync(string userId, bool value, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            await QueryAsync("INSERT INTO forummemberprefs (propertyid, userid, autofollowoncomment, updatedutc) VALUES (" + Pid + ", " + D.Str(userId) + ", " + D.Bool(value) + ", " + D.Ts(nowUtc) +
                ") ON CONFLICT (propertyid, userid) DO UPDATE SET autofollowoncomment = excluded.autofollowoncomment, updatedutc = excluded.updatedutc;", token).ConfigureAwait(false);
        }

        public Task<EnumerationResult<string>> EnumerateModeratorsAsync(string forumId, EnumerationRequest request, CancellationToken token = default)
        {
            RequireText(forumId, nameof(forumId));
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = new List<string> { "u.isdeleted = " + D.Bool(false), "EXISTS (SELECT 1 FROM roleassignments r WHERE " + Scope("r") + " AND r.userid = u.id AND " + CoversForum("r", forumId) + ")" };
            return EnumerateAsync(request, "u.id", "users u", conditions, row => RowReader.GetString(row, "id"), token);
        }

        public Task<long> CountModeratorsAsync(string forumId, CancellationToken token = default)
        {
            RequireText(forumId, nameof(forumId));
            return CountAsync("SELECT COUNT(DISTINCT r.userid) AS cnt FROM roleassignments r JOIN users u ON " + Scope("u") + " AND u.id = r.userid AND u.isdeleted = " + D.Bool(false) +
                " WHERE " + Scope("r") + " AND " + CoversForum("r", forumId) + ";", token);
        }

        public async Task<List<string>> ModeratorIdsForForumAsync(string forumId, CancellationToken token = default)
        {
            RequireText(forumId, nameof(forumId));
            DataTable table = await QueryAsync("SELECT DISTINCT r.userid FROM roleassignments r JOIN users u ON " + Scope("u") + " AND u.id = r.userid AND u.isdeleted = " + D.Bool(false) + " AND u.isbanned = " + D.Bool(false) +
                " WHERE " + Scope("r") + " AND (" + CoversForum("r", forumId) + " OR r.role = " + D.Enum(RoleEnum.SystemAdmin) + ") ORDER BY r.userid;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "userid")).ToList();
        }

        public async Task<Dictionary<string, long>> OpenReportCountsAsync(IEnumerable<string> forumIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(forumIds);
            Dictionary<string, long> result = new Dictionary<string, long>(StringComparer.Ordinal);
            List<string> ids = forumIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT r.scopeid, COUNT(*) AS cnt FROM reports r WHERE " + Scope("r") + " AND r.scopetype = 'Forum' AND r.status = " + D.Enum(ReportStatusEnum.Open) +
                " AND r.scopeid IN " + D.InList(ids) + " GROUP BY r.scopeid;", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows) result[RowReader.GetString(row, "scopeid")] = RowReader.GetLong(row, "cnt");
            return result;
        }

        private string CoversForum(string alias, string forumId)
        {
            return "((" + alias + ".role = " + D.Enum(RoleEnum.Moderator) + " AND " + alias + ".resourcetype = " + D.Enum(ResourceTypeEnum.Forum) + " AND " + alias + ".resourceid IN " +
                D.InList(new[] { forumId, "*" }) + ") OR (" + alias + ".role = " + D.Enum(RoleEnum.Moderator) + " AND " + alias + ".resourcetype = " + D.Enum(ResourceTypeEnum.Global) + "))";
        }

        private string ActiveMute(string alias, DateTime nowUtc)
        {
            return "(" + alias + ".endedutc IS NULL AND (" + alias + ".untilutc IS NULL OR " + alias + ".untilutc > " + D.Ts(nowUtc) + "))";
        }

        private List<string> TypeStatements(Forum forum)
        {
            List<string> statements = new List<string>();
            List<ThreadTypeEnum> types = forum.AllowedThreadTypes.Distinct().ToList();
            if (types.Count == 0) return statements;
            statements.Add("INSERT INTO forumthreadtypes (propertyid, forumid, threadtype) VALUES " + String.Join(", ", types.Select(t => "(" + Pid + ", " + D.Str(forum.Id) + ", " + D.Enum(t) + ")")) +
                " ON CONFLICT DO NOTHING;");
            return statements;
        }

        private async Task<List<Forum>> LoadAsync(string condition, CancellationToken token)
        {
            DataTable table = await QueryAsync("SELECT fo.* FROM forums fo WHERE " + Scope("fo") + " AND " + condition + ";", token).ConfigureAwait(false);
            List<Forum> forums = table.Rows.Cast<DataRow>().Select(Map).ToList();
            await FillTypesAsync(forums, token).ConfigureAwait(false);
            return forums;
        }

        private async Task FillTypesAsync(List<Forum> forums, CancellationToken token)
        {
            if (forums.Count == 0) return;
            DataTable table = await QueryAsync("SELECT forumid, threadtype FROM forumthreadtypes WHERE " + Scope() + " AND forumid IN " + D.InList(forums.Select(f => f.Id)) + ";", token).ConfigureAwait(false);
            Dictionary<string, Forum> byId = forums.GroupBy(f => f.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            Dictionary<string, HashSet<ThreadTypeEnum>> types = new Dictionary<string, HashSet<ThreadTypeEnum>>(StringComparer.Ordinal);
            foreach (DataRow row in table.Rows)
            {
                string id = RowReader.GetString(row, "forumid");
                if (!types.TryGetValue(id, out HashSet<ThreadTypeEnum>? set))
                {
                    set = new HashSet<ThreadTypeEnum>();
                    types[id] = set;
                }
                set.Add(RowReader.GetEnum(row, "threadtype", ThreadTypeEnum.Discussion));
            }
            foreach (Forum forum in forums)
            {
                forum.AllowedThreadTypes = types.TryGetValue(forum.Id, out HashSet<ThreadTypeEnum>? set)
                    ? Enum.GetValues<ThreadTypeEnum>().Where(set.Contains).ToList()
                    : new List<ThreadTypeEnum>();
            }
        }

        private static Forum Map(DataRow row)
        {
            return new Forum
            {
                Id = RowReader.GetString(row, "id"),
                Slug = RowReader.GetString(row, "slug"),
                Name = RowReader.GetString(row, "name"),
                Description = RowReader.GetNullableString(row, "description"),
                Guidelines = RowReader.GetNullableString(row, "guidelines"),
                IconEmoji = RowReader.GetNullableString(row, "iconemoji"),
                IconMediaId = RowReader.GetNullableString(row, "iconmediaid"),
                Color = RowReader.GetNullableString(row, "color"),
                Section = RowReader.GetNullableString(row, "section"),
                SortOrder = RowReader.GetInt(row, "sortorder"),
                Audience = RowReader.GetEnum(row, "audience", ContentAudienceEnum.Members),
                ThreadCreation = RowReader.GetEnum(row, "threadcreation", ThreadCreationEnum.Members),
                DefaultOrdering = RowReader.GetEnum(row, "defaultordering", ThreadOrderingEnum.Active),
                AllowMedia = RowReader.GetBool(row, "allowmedia"),
                AllowVideo = RowReader.GetBool(row, "allowvideo"),
                MaxOpenFeedbackPerMember = RowReader.GetNullableInt(row, "maxopenfeedbackpermember"),
                IsArchived = RowReader.GetBool(row, "isarchived"),
                ArchivedUtc = RowReader.GetNullableDateTime(row, "archivedutc"),
                ThreadCount = RowReader.GetInt(row, "threadcount"),
                CommentCount = RowReader.GetInt(row, "commentcount"),
                LastActivityUtc = RowReader.GetNullableDateTime(row, "lastactivityutc"),
                LastThreadId = RowReader.GetNullableString(row, "lastthreadid"),
                CreatedByUserId = RowReader.GetNullableString(row, "createdbyuserid"),
                DeletedUtc = RowReader.GetNullableDateTime(row, "deletedutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        private static ForumMute MapMute(DataRow row)
        {
            return new ForumMute
            {
                Id = RowReader.GetString(row, "id"),
                ForumId = RowReader.GetString(row, "forumid"),
                UserId = RowReader.GetString(row, "userid"),
                UntilUtc = RowReader.GetNullableDateTime(row, "untilutc"),
                Reason = RowReader.GetNullableString(row, "reason"),
                MutedByUserId = RowReader.GetNullableString(row, "mutedbyuserid"),
                EndedUtc = RowReader.GetNullableDateTime(row, "endedutc"),
                EndedByUserId = RowReader.GetNullableString(row, "endedbyuserid"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
        }
    }
}
