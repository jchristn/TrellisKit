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

    /// <summary>
    /// Shared notification center methods.
    /// </summary>
    internal abstract class NotificationMethodsBase : MethodsBase, INotificationMethods
    {
        private const string _Columns = "id, recipientuserid, type, category, actoruserid, actorcount, targettype, targetid, feedpostid, commentid, parentcommentid, title, body, path, groupkey, readutc, pushstate, lastpushedutc, createdutc, updatedutc, threadid, forumid, pollid";
        private const string _InsertColumns = "propertyid, " + _Columns;
        private const string _Unique = "WHERE readutc IS NULL AND groupkey IS NOT NULL";
        private const int _InsertChunk = 200;

        protected NotificationMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<Notification> CreateAsync(Notification notification, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(notification);
            RequireText(notification.Id, nameof(notification.Id));
            List<string> statements = new List<string> { "INSERT INTO notifications (" + _InsertColumns + ") VALUES " + Values(notification) + ";" };
            if (!String.IsNullOrEmpty(notification.ActorUserId))
            {
                statements.Add("INSERT INTO notificationactors (propertyid, notificationid, userid, createdutc) VALUES (" + Pid + ", " + D.Str(notification.Id) + ", " + D.Str(notification.ActorUserId) + ", " + D.Ts(notification.CreatedUtc) + ");");
            }
            await TransactionAsync(statements, token).ConfigureAwait(false);
            return notification;
        }

        public async Task<int> CreateManyAsync(List<Notification> notifications, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(notifications);
            if (notifications.Count == 0) return 0;
            List<string> statements = new List<string>();
            for (int i = 0; i < notifications.Count; i += _InsertChunk)
            {
                List<Notification> chunk = notifications.Skip(i).Take(_InsertChunk).ToList();
                statements.Add("INSERT INTO notifications (" + _InsertColumns + ") VALUES " + String.Join(", ", chunk.Select(Values)) + ";");
                List<Notification> withActor = chunk.Where(n => !String.IsNullOrEmpty(n.ActorUserId)).ToList();
                if (withActor.Count > 0)
                {
                    statements.Add("INSERT INTO notificationactors (propertyid, notificationid, userid, createdutc) VALUES " +
                        String.Join(", ", withActor.Select(n => "(" + Pid + ", " + D.Str(n.Id) + ", " + D.Str(n.ActorUserId) + ", " + D.Ts(n.CreatedUtc) + ")")) + ";");
                }
            }
            await TransactionAsync(statements, token).ConfigureAwait(false);
            return notifications.Count;
        }

        public async Task<Notification?> AggregateAsync(Notification draft, string titleOne, string titleTwo, string titleManyPrefix, string titleManySuffix, bool queuePush, TimeSpan pushWindow, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(draft);
            RequireText(draft.Id, nameof(draft.Id));
            RequireText(draft.GroupKey, nameof(draft.GroupKey));
            RequireText(draft.ActorUserId, nameof(draft.ActorUserId));
            DateTime now = draft.UpdatedUtc;
            draft.ActorCount = 1;
            draft.Title = titleOne;
            draft.PushState = queuePush ? PushStateEnum.Pending : PushStateEnum.None;
            string group = Scope() + " AND recipientuserid = " + D.Str(draft.RecipientUserId) + " AND groupkey = " + D.Str(draft.GroupKey) + " AND readutc IS NULL";
            string pushUpdate = queuePush
                ? "CASE WHEN notifications.lastpushedutc IS NULL OR notifications.lastpushedutc < " + D.Ts(now - pushWindow) + " THEN " + D.Enum(PushStateEnum.Pending) + " ELSE notifications.pushstate END"
                : "notifications.pushstate";
            string count = "(SELECT COUNT(*) FROM notificationactors a WHERE " + Scope("a") + " AND a.notificationid = notifications.id)";
            List<string> statements = new List<string>
            {
                "INSERT INTO notifications (" + _InsertColumns + ") VALUES " + Values(draft) +
                    " ON CONFLICT (propertyid, recipientuserid, groupkey) " + _Unique +
                    " DO UPDATE SET actoruserid = excluded.actoruserid, body = excluded.body, path = excluded.path, updatedutc = excluded.updatedutc, pushstate = " + pushUpdate + ";",
                "INSERT INTO notificationactors (propertyid, notificationid, userid, createdutc) SELECT " + Pid + ", id, " + D.Str(draft.ActorUserId) + ", " + D.Ts(now) +
                    " FROM notifications WHERE " + group + " ON CONFLICT (notificationid, userid) DO UPDATE SET createdutc = excluded.createdutc;",
                "UPDATE notifications SET actorcount = " + count + ", title = CASE WHEN " + count + " <= 1 THEN " + D.Str(titleOne) +
                    " WHEN " + count + " = 2 THEN " + D.Str(titleTwo) +
                    " ELSE " + D.Str(titleManyPrefix) + " || CAST(" + count + " - 1 AS TEXT) || " + D.Str(titleManySuffix) + " END WHERE " + group + " RETURNING " + _Columns + ";"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            Notification result = Map(table.Rows[0]);
            await FillActorsAsync(new List<Notification> { result }, token).ConfigureAwait(false);
            return result;
        }

        public async Task<Notification?> UpsertGroupedAsync(Notification draft, bool queuePush, TimeSpan pushWindow, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(draft);
            RequireText(draft.Id, nameof(draft.Id));
            RequireText(draft.GroupKey, nameof(draft.GroupKey));
            DateTime now = draft.UpdatedUtc;
            draft.ActorCount = String.IsNullOrEmpty(draft.ActorUserId) ? 0 : 1;
            draft.PushState = queuePush ? PushStateEnum.Pending : PushStateEnum.None;
            string pushUpdate = queuePush
                ? "CASE WHEN notifications.lastpushedutc IS NULL OR notifications.lastpushedutc < " + D.Ts(now - pushWindow) + " THEN " + D.Enum(PushStateEnum.Pending) + " ELSE notifications.pushstate END"
                : "notifications.pushstate";
            List<string> statements = new List<string>
            {
                "INSERT INTO notifications (" + _InsertColumns + ") VALUES " + Values(draft) +
                    " ON CONFLICT (propertyid, recipientuserid, groupkey) " + _Unique +
                    " DO UPDATE SET actoruserid = excluded.actoruserid, actorcount = excluded.actorcount, title = excluded.title, body = excluded.body, path = excluded.path, " +
                    "updatedutc = excluded.updatedutc, pushstate = " + pushUpdate + ";"
            };
            string group = Scope() + " AND recipientuserid = " + D.Str(draft.RecipientUserId) + " AND groupkey = " + D.Str(draft.GroupKey) + " AND readutc IS NULL";
            if (!String.IsNullOrEmpty(draft.ActorUserId))
                statements.Add("INSERT INTO notificationactors (propertyid, notificationid, userid, createdutc) SELECT " + Pid + ", id, " + D.Str(draft.ActorUserId) + ", " + D.Ts(now) +
                    " FROM notifications WHERE " + group + " ON CONFLICT (notificationid, userid) DO UPDATE SET createdutc = excluded.createdutc;");
            statements.Add("SELECT " + _Columns + " FROM notifications WHERE " + group + ";");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            Notification result = Map(table.Rows[0]);
            await FillActorsAsync(new List<Notification> { result }, token).ConfigureAwait(false);
            return result;
        }

        public async Task<Notification?> ReadAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM notifications WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            Notification notification = Map(table.Rows[0]);
            await FillActorsAsync(new List<Notification> { notification }, token).ConfigureAwait(false);
            return notification;
        }

        public async Task<EnumerationResult<Notification>> EnumerateForRecipientAsync(string recipientUserId, EnumerationRequest request, bool unreadOnly, NotificationTypeEnum? type, NotificationCategoryEnum? category, CancellationToken token = default)
        {
            RequireText(recipientUserId, nameof(recipientUserId));
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = new List<string> { "n.recipientuserid = " + D.Str(recipientUserId) };
            if (unreadOnly) conditions.Add("n.readutc IS NULL");
            if (type != null) conditions.Add("n.type = " + D.Enum(type.Value));
            if (category != null) conditions.Add("n.category = " + D.Enum(category.Value));
            string select = String.Join(", ", _Columns.Split(", ").Select(c => "n." + c));
            EnumerationResult<Notification> page = await EnumerateAsync(request, select, "notifications n", conditions, Map, token).ConfigureAwait(false);
            await FillActorsAsync(page.Objects, token).ConfigureAwait(false);
            return page;
        }

        public Task<long> CountUnreadAsync(string recipientUserId, CancellationToken token = default)
        {
            RequireText(recipientUserId, nameof(recipientUserId));
            return CountAsync("SELECT COUNT(*) AS cnt FROM notifications WHERE " + Scope() + " AND recipientuserid = " + D.Str(recipientUserId) + " AND readutc IS NULL;", token);
        }

        public async Task<DateTime?> LatestUpdatedAsync(string recipientUserId, CancellationToken token = default)
        {
            RequireText(recipientUserId, nameof(recipientUserId));
            DataTable table = await QueryAsync("SELECT updatedutc FROM notifications WHERE " + Scope() + " AND recipientuserid = " + D.Str(recipientUserId) + " ORDER BY updatedutc DESC, id DESC LIMIT 1;", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : RowReader.GetNullableDateTime(table.Rows[0], "updatedutc");
        }

        public async Task<int> MarkReadAsync(string recipientUserId, List<string> ids, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(recipientUserId, nameof(recipientUserId));
            ArgumentNullException.ThrowIfNull(ids);
            if (ids.Count == 0) return 0;
            DataTable table = await QueryAsync("UPDATE notifications SET readutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND recipientuserid = " + D.Str(recipientUserId) +
                " AND readutc IS NULL AND id IN " + D.InList(ids) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public async Task<int> MarkUnreadAsync(string recipientUserId, List<string> ids, CancellationToken token = default)
        {
            RequireText(recipientUserId, nameof(recipientUserId));
            ArgumentNullException.ThrowIfNull(ids);
            if (ids.Count == 0) return 0;
            string owner = Scope() + " AND recipientuserid = " + D.Str(recipientUserId);
            DataTable plain = await QueryAsync("UPDATE notifications SET readutc = NULL WHERE " + owner + " AND readutc IS NOT NULL AND groupkey IS NULL AND id IN " + D.InList(ids) + " RETURNING id;", token).ConfigureAwait(false);
            int changed = plain.Rows.Count;
            DataTable grouped = await QueryAsync("SELECT id FROM notifications WHERE " + owner + " AND readutc IS NOT NULL AND groupkey IS NOT NULL AND id IN " + D.InList(ids) + " ORDER BY updatedutc DESC, id DESC;", token).ConfigureAwait(false);
            foreach (DataRow row in grouped.Rows)
            {
                string id = RowReader.GetString(row, "id");
                try
                {
                    DataTable one = await QueryAsync("UPDATE notifications SET readutc = NULL WHERE id = " + D.Str(id) + " AND " + owner + " AND readutc IS NOT NULL AND NOT EXISTS (SELECT 1 FROM notifications o WHERE " + Scope("o") + " AND o.recipientuserid = " +
                        D.Str(recipientUserId) + " AND o.groupkey = notifications.groupkey AND o.readutc IS NULL) RETURNING id;", token).ConfigureAwait(false);
                    changed += one.Rows.Count;
                }
                catch (Exception e) when (D.IsUniqueViolation(e))
                {
                    // Another unread notification took the group concurrently; this one stays read.
                }
            }
            return changed;
        }

        public async Task<int> MarkAllReadAsync(string recipientUserId, DateTime beforeUtc, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(recipientUserId, nameof(recipientUserId));
            DataTable table = await QueryAsync("UPDATE notifications SET readutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND recipientuserid = " + D.Str(recipientUserId) +
                " AND readutc IS NULL AND updatedutc <= " + D.Ts(beforeUtc) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public async Task<HashSet<string>> ExistingRecipientsAsync(NotificationTypeEnum type, string targetId, IEnumerable<string> recipientUserIds, CancellationToken token = default)
        {
            RequireText(targetId, nameof(targetId));
            ArgumentNullException.ThrowIfNull(recipientUserIds);
            List<string> ids = recipientUserIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            if (ids.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT DISTINCT recipientuserid FROM notifications WHERE " + Scope() + " AND type = " + D.Enum(type) + " AND targetid = " + D.Str(targetId) +
                " AND recipientuserid IN " + D.InList(ids) + ";", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows) result.Add(RowReader.GetString(row, "recipientuserid"));
            return result;
        }

        public async Task<HashSet<string>> BlockedEitherWayAsync(string userId, IEnumerable<string> otherUserIds, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            ArgumentNullException.ThrowIfNull(otherUserIds);
            List<string> ids = otherUserIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            if (ids.Count == 0) return result;
            string list = D.InList(ids);
            DataTable table = await QueryAsync("SELECT blockeduserid AS other FROM blocks WHERE " + Scope() + " AND blockeruserid = " + D.Str(userId) + " AND blockeduserid IN " + list +
                " UNION SELECT blockeruserid AS other FROM blocks WHERE " + Scope() + " AND blockeduserid = " + D.Str(userId) + " AND blockeruserid IN " + list + ";", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows) result.Add(RowReader.GetString(row, "other"));
            return result;
        }

        public async Task<List<Notification>> EnumeratePendingPushAsync(int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM notifications WHERE " + Scope() + " AND pushstate = " + D.Enum(PushStateEnum.Pending) +
                " ORDER BY createdutc, id LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public async Task<List<string>> TransitionPushStateAsync(List<string> ids, PushStateEnum state, DateTime? lastPushedUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            if (ids.Count == 0) return new List<string>();
            DataTable table = await QueryAsync("UPDATE notifications SET pushstate = " + D.Enum(state) + (lastPushedUtc == null ? String.Empty : ", lastpushedutc = " + D.Ts(lastPushedUtc.Value)) +
                " WHERE " + Scope() + " AND pushstate = " + D.Enum(PushStateEnum.Pending) + " AND id IN " + D.InList(ids) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "id")).ToList();
        }

        public async Task SetPushStateAsync(string id, PushStateEnum state, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            await QueryAsync("UPDATE notifications SET pushstate = " + D.Enum(state) + " WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
        }

        public Task<long> CountPushedSinceAsync(string recipientUserId, DateTime sinceUtc, CancellationToken token = default)
        {
            RequireText(recipientUserId, nameof(recipientUserId));
            return CountAsync("SELECT COUNT(*) AS cnt FROM notifications WHERE " + Scope() + " AND recipientuserid = " + D.Str(recipientUserId) + " AND lastpushedutc >= " + D.Ts(sinceUtc) + ";", token);
        }

        public async Task<bool> GroupPushedSinceAsync(string recipientUserId, string groupKey, DateTime sinceUtc, CancellationToken token = default)
        {
            RequireText(recipientUserId, nameof(recipientUserId));
            RequireText(groupKey, nameof(groupKey));
            long count = await CountAsync("SELECT COUNT(*) AS cnt FROM notifications WHERE " + Scope() + " AND recipientuserid = " + D.Str(recipientUserId) + " AND groupkey = " + D.Str(groupKey) +
                " AND lastpushedutc >= " + D.Ts(sinceUtc) + ";", token).ConfigureAwait(false);
            return count > 0;
        }

        public async Task<NotificationSourcePost?> ReadSourcePostAsync(string feedPostId, CancellationToken token = default)
        {
            RequireText(feedPostId, nameof(feedPostId));
            DataTable table = await QueryAsync("SELECT id, authoruserid, title, excerpt, publishedutc, deletedutc, moderationstate FROM feedposts WHERE " + Scope() + " AND id = " + D.Str(feedPostId) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            DataRow row = table.Rows[0];
            return new NotificationSourcePost
            {
                Id = RowReader.GetString(row, "id"),
                AuthorUserId = RowReader.GetString(row, "authoruserid"),
                Title = RowReader.GetNullableString(row, "title"),
                Excerpt = RowReader.GetString(row, "excerpt"),
                PublishedUtc = RowReader.GetNullableDateTime(row, "publishedutc"),
                DeletedUtc = RowReader.GetNullableDateTime(row, "deletedutc"),
                ModerationState = RowReader.GetEnum(row, "moderationstate", ModerationStateEnum.None)
            };
        }

        public Task<List<FanoutRecipient>> EnumerateFanoutRecipientsAsync(string authorUserId, NotificationCategoryEnum category, bool defaultEmail, string? afterUserId, int limit, DateTime nowUtc, CancellationToken token = default)
        {
            return EnumerateFanoutRecipientsAsync(authorUserId, category, defaultEmail, afterUserId, limit, true, nowUtc, token);
        }

        public async Task<List<FanoutRecipient>> EnumerateFanoutRecipientsAsync(string authorUserId, NotificationCategoryEnum category, bool defaultEmail, string? afterUserId, int limit, bool requireMembership, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(authorUserId, nameof(authorUserId));
            string author = D.Str(authorUserId);
            List<string> conditions = new List<string>
            {
                Scope("u"),
                "u.isdeleted = " + D.Bool(false),
                "u.isbanned = " + D.Bool(false),
                "u.ageattestedutc IS NOT NULL",
                "u.id <> " + author,
                "NOT EXISTS (SELECT 1 FROM blocks b WHERE " + Scope("b") + " AND ((b.blockeruserid = u.id AND b.blockeduserid = " + author + ") OR (b.blockeruserid = " + author + " AND b.blockeduserid = u.id)))"
            };
            if (requireMembership)
                conditions.Add("(" + ActiveEntitlementExists("u.id", nowUtc, null) + " OR EXISTS (SELECT 1 FROM roleassignments r WHERE " + Scope("r") + " AND r.userid = u.id AND r.role = " + D.Enum(RoleEnum.SystemAdmin) + "))");
            if (!String.IsNullOrEmpty(afterUserId)) conditions.Add("u.id > " + D.Str(afterUserId));
            DataTable table = await QueryAsync("SELECT u.id AS userid, u.email, u.emailverified, u.displayname, COALESCE(p.inapp, " + D.Bool(true) + ") AS prefinapp, COALESCE(p.email, " + D.Bool(defaultEmail) +
                ") AS prefemail FROM users u LEFT JOIN notificationpreferences p ON " + Scope("p") + " AND p.userid = u.id AND p.category = " + D.Enum(category) + Where(conditions) +
                " ORDER BY u.id LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(row => new FanoutRecipient
            {
                UserId = RowReader.GetString(row, "userid"),
                Email = RowReader.GetNullableString(row, "email"),
                EmailVerified = RowReader.GetBool(row, "emailverified"),
                DisplayName = RowReader.GetString(row, "displayname"),
                InApp = RowReader.GetBool(row, "prefinapp"),
                EmailOn = RowReader.GetBool(row, "prefemail")
            }).ToList();
        }

        public async Task<List<string>> EnumerateModeratorIdsAsync(CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT DISTINCT r.userid FROM roleassignments r JOIN users u ON " + Scope("u") + " AND u.id = r.userid WHERE " + Scope("r") + " AND r.role IN " +
                D.InList(new[] { RoleEnum.SystemAdmin.ToString(), RoleEnum.Moderator.ToString(), RoleEnum.FeedModerator.ToString(), RoleEnum.DirectoryModerator.ToString(),
                RoleEnum.EventManager.ToString(), RoleEnum.LibraryEditor.ToString() }) +
                " AND u.isdeleted = " + D.Bool(false) + " AND u.isbanned = " + D.Bool(false) + " ORDER BY r.userid;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "userid")).ToList();
        }

        public async Task<int> DeleteForUserAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            string z = D.Str(userId);
            string mine = "SELECT id FROM notifications WHERE " + Scope() + " AND recipientuserid = " + z;
            string others = "EXISTS (SELECT 1 FROM notificationactors o WHERE " + Scope("o") + " AND o.notificationid = n.id AND o.userid <> " + z + ")";
            string sole = "SELECT n.id FROM notifications n WHERE " + Scope("n") + " AND n.actoruserid = " + z + " AND n.recipientuserid <> " + z +
                " AND n.category <> " + D.Enum(NotificationCategoryEnum.Moderation) + " AND NOT " + others;
            string actor = "FROM notificationactors a WHERE " + Scope("a") + " AND a.notificationid = n.id AND a.userid <> " + z;
            string recount =
                "UPDATE notifications SET actorcount = r.cnt, actoruserid = r.top, title = CASE WHEN r.cnt <= 1 THEN r.name || r.suffix" +
                " WHEN r.cnt = 2 THEN r.name || ' and 1 other' || r.suffix ELSE r.name || ' and ' || CAST(r.cnt - 1 AS TEXT) || ' others' || r.suffix END" +
                " FROM (SELECT n.id, (SELECT COUNT(*) " + actor + ") AS cnt," +
                " (SELECT a.userid " + actor + " ORDER BY a.createdutc DESC, a.userid LIMIT 1) AS top," +
                " COALESCE((SELECT u.displayname FROM notificationactors a JOIN users u ON " + Scope("u") + " AND u.id = a.userid WHERE " + Scope("a") +
                " AND a.notificationid = n.id AND a.userid <> " + z + " ORDER BY a.createdutc DESC, a.userid LIMIT 1), 'Someone') AS name," +
                " CASE n.targettype WHEN " + D.Str(TargetTypeEnum.Comment.ToString()) + " THEN ' reacted to your comment' WHEN " + D.Str(TargetTypeEnum.FeedPost.ToString()) +
                " THEN ' reacted to your post' WHEN " + D.Str(TargetTypeEnum.User.ToString()) + " THEN ' reacted to your member' ELSE ' reacted to your item' END AS suffix" +
                " FROM notifications n WHERE " + Scope("n") + " AND n.type = " + D.Enum(NotificationTypeEnum.Reaction) + " AND n.recipientuserid <> " + z +
                " AND EXISTS (SELECT 1 FROM notificationactors y WHERE " + Scope("y") + " AND y.notificationid = n.id AND y.userid = " + z + ") AND " + others + ") r" +
                " WHERE " + Scope() + " AND notifications.id = r.id;";
            List<string> statements = new List<string>
            {
                "DELETE FROM pushdeliveries WHERE " + Scope() + " AND notificationid IN (" + sole + ");",
                "DELETE FROM notificationactors WHERE " + Scope() + " AND notificationid IN (" + sole + ");"
            };
            statements.AddRange(Driver.Metadata.DeleteWhereStatements(TargetTypeEnum.Notification, sole));
            statements.Add("DELETE FROM notifications WHERE " + Scope() + " AND id IN (" + sole + ");");
            statements.Add(recount);
            statements.Add("UPDATE notifications SET actoruserid = NULL WHERE " + Scope() + " AND actoruserid = " + z + " AND recipientuserid <> " + z + ";");
            statements.Add("DELETE FROM notificationactors WHERE " + Scope() + " AND userid = " + z + ";");
            statements.Add("DELETE FROM notificationactors WHERE " + Scope() + " AND notificationid IN (" + mine + ");");
            statements.Add("DELETE FROM pushdeliveries WHERE " + Scope() + " AND notificationid IN (" + mine + ");");
            statements.AddRange(Driver.Metadata.DeleteWhereStatements(TargetTypeEnum.Notification, mine));
            statements.Add("DELETE FROM notifications WHERE " + Scope() + " AND recipientuserid = " + z + " RETURNING id;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public async Task<int> PruneAsync(DateTime readBeforeUtc, DateTime unreadBeforeUtc, int limit, CancellationToken token = default)
        {
            DataTable candidates = await QueryAsync("SELECT id FROM notifications WHERE " + Scope() + " AND ((readutc IS NOT NULL AND updatedutc < " + D.Ts(readBeforeUtc) + ") OR (readutc IS NULL AND updatedutc < " +
                D.Ts(unreadBeforeUtc) + ")) ORDER BY id LIMIT " + D.Num(Math.Max(1, limit)) + ";", token).ConfigureAwait(false);
            List<string> ids = candidates.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "id")).ToList();
            if (ids.Count == 0) return 0;
            string list = D.InList(ids);
            List<string> statements = new List<string>
            {
                "DELETE FROM notificationactors WHERE " + Scope() + " AND notificationid IN " + list + ";",
                "DELETE FROM pushdeliveries WHERE " + Scope() + " AND notificationid IN " + list + ";"
            };
            statements.AddRange(Driver.Metadata.DeleteWhereStatements(TargetTypeEnum.Notification, "SELECT id FROM notifications WHERE " + Scope() + " AND id IN " + list));
            statements.Add("DELETE FROM notifications WHERE " + Scope() + " AND id IN " + list + " RETURNING id;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        private string Values(Notification n)
        {
            return "(" + Pid + ", " + D.Str(n.Id) + ", " + D.Str(n.RecipientUserId) + ", " + D.Enum(n.Type) + ", " + D.Enum(n.Category) + ", " + D.Str(n.ActorUserId) + ", " +
                D.Num(n.ActorCount) + ", " + D.Str(n.TargetType) + ", " + D.Str(n.TargetId) + ", " + D.Str(n.FeedPostId) + ", " + D.Str(n.CommentId) + ", " +
                D.Str(n.ParentCommentId) + ", " + D.Str(n.Title) + ", " + D.Str(n.Body) + ", " + D.Str(n.Path) + ", " + D.Str(n.GroupKey) + ", " + D.Ts(n.ReadUtc) + ", " +
                D.Enum(n.PushState) + ", " + D.Ts(n.LastPushedUtc) + ", " + D.Ts(n.CreatedUtc) + ", " + D.Ts(n.UpdatedUtc) + ", " + D.Str(n.ThreadId) + ", " + D.Str(n.ForumId) + ", " + D.Str(n.PollId) + ")";
        }

        private async Task FillActorsAsync(List<Notification> notifications, CancellationToken token)
        {
            if (notifications.Count == 0) return;
            DataTable table = await QueryAsync("SELECT notificationid, userid FROM (SELECT notificationid, userid, createdutc, ROW_NUMBER() OVER (PARTITION BY notificationid ORDER BY createdutc DESC, userid) AS rn " +
                "FROM notificationactors WHERE " + Scope() + " AND notificationid IN " + D.InList(notifications.Select(n => n.Id)) + ") ranked WHERE rn <= 3 ORDER BY notificationid, rn;", token).ConfigureAwait(false);
            Dictionary<string, Notification> byId = notifications.GroupBy(n => n.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            foreach (DataRow row in table.Rows)
            {
                if (byId.TryGetValue(RowReader.GetString(row, "notificationid"), out Notification? notification)) notification.RecentActorIds.Add(RowReader.GetString(row, "userid"));
            }
        }

        public async Task<List<Notification>> ListUnreadSinceAsync(string userId, NotificationCategoryEnum category, DateTime sinceUtc, int limit, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("SELECT " + _Columns + " FROM notifications WHERE " + Scope() + " AND recipientuserid = " + D.Str(userId) + " AND category = " + D.Enum(category) +
                " AND readutc IS NULL AND updatedutc > " + D.Ts(sinceUtc) + " ORDER BY updatedutc DESC, id DESC LIMIT " + D.Num(Math.Clamp(limit, 1, 100)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Map).ToList();
        }

        public Task<long> CountUnreadSinceAsync(string userId, NotificationCategoryEnum category, DateTime sinceUtc, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            return CountAsync("SELECT COUNT(*) AS cnt FROM notifications WHERE " + Scope() + " AND recipientuserid = " + D.Str(userId) + " AND category = " + D.Enum(category) +
                " AND readutc IS NULL AND updatedutc > " + D.Ts(sinceUtc) + ";", token);
        }

        private static Notification Map(DataRow row)
        {
            return new Notification
            {
                Id = RowReader.GetString(row, "id"),
                RecipientUserId = RowReader.GetString(row, "recipientuserid"),
                Type = RowReader.GetEnum(row, "type", NotificationTypeEnum.PostComment),
                Category = RowReader.GetEnum(row, "category", NotificationCategoryEnum.Replies),
                ActorUserId = RowReader.GetNullableString(row, "actoruserid"),
                ActorCount = RowReader.GetInt(row, "actorcount"),
                TargetType = RowReader.GetString(row, "targettype"),
                TargetId = RowReader.GetString(row, "targetid"),
                FeedPostId = RowReader.GetNullableString(row, "feedpostid"),
                CommentId = RowReader.GetNullableString(row, "commentid"),
                ParentCommentId = RowReader.GetNullableString(row, "parentcommentid"),
                ThreadId = RowReader.GetNullableString(row, "threadid"),
                ForumId = RowReader.GetNullableString(row, "forumid"),
                PollId = RowReader.GetNullableString(row, "pollid"),
                Title = RowReader.GetString(row, "title"),
                Body = RowReader.GetNullableString(row, "body"),
                Path = RowReader.GetString(row, "path"),
                GroupKey = RowReader.GetNullableString(row, "groupkey"),
                ReadUtc = RowReader.GetNullableDateTime(row, "readutc"),
                PushState = RowReader.GetEnum(row, "pushstate", PushStateEnum.None),
                LastPushedUtc = RowReader.GetNullableDateTime(row, "lastpushedutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }
    }
}
