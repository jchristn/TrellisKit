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

    /// <summary>
    /// Shared label and tag methods.
    /// </summary>
    internal abstract class MetadataMethodsBase : MethodsBase, IMetadataMethods
    {
        private const string _LabelIdPrefix = "lbl_";
        private const string _TagIdPrefix = "tag_";

        protected MetadataMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<ObjectMetadata> ReadAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            RequireText(targetId, nameof(targetId));
            Dictionary<string, ObjectMetadata> map = await ReadManyAsync(targetType, new[] { targetId }, token).ConfigureAwait(false);
            return map[targetId];
        }

        public async Task<Dictionary<string, ObjectMetadata>> ReadManyAsync(TargetTypeEnum targetType, IEnumerable<string> targetIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(targetIds);
            List<string> ids = targetIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            Dictionary<string, ObjectMetadata> result = new Dictionary<string, ObjectMetadata>(StringComparer.Ordinal);
            foreach (string id in ids) result[id] = new ObjectMetadata();
            if (ids.Count == 0) return result;
            string type = D.Enum(targetType);
            DataTable labels = await QueryAsync("SELECT targetid, label FROM labels WHERE " + Scope() + " AND targettype = " + type + " AND targetid IN " + D.InList(ids) + " ORDER BY createdutc, id;", token).ConfigureAwait(false);
            foreach (DataRow row in labels.Rows)
            {
                if (result.TryGetValue(RowReader.GetString(row, "targetid"), out ObjectMetadata? m)) m.Labels.Add(RowReader.GetString(row, "label"));
            }
            DataTable tags = await QueryAsync("SELECT targetid, tagkey, tagvalue FROM tags WHERE " + Scope() + " AND targettype = " + type + " AND targetid IN " + D.InList(ids) + " ORDER BY createdutc, id;", token).ConfigureAwait(false);
            foreach (DataRow row in tags.Rows)
            {
                if (result.TryGetValue(RowReader.GetString(row, "targetid"), out ObjectMetadata? m)) m.Tags[RowReader.GetString(row, "tagkey")] = RowReader.GetString(row, "tagvalue");
            }
            return result;
        }

        public async Task WriteAsync(TargetTypeEnum targetType, string targetId, ObjectMetadata metadata, CancellationToken token = default)
        {
            RequireText(targetId, nameof(targetId));
            ArgumentNullException.ThrowIfNull(metadata);
            await TransactionAsync(WriteStatements(targetType, targetId, metadata, DateTime.UtcNow), token).ConfigureAwait(false);
        }

        public async Task WriteManyAsync(TargetTypeEnum targetType, IEnumerable<string> targetIds, ObjectMetadata metadata, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(targetIds);
            ArgumentNullException.ThrowIfNull(metadata);
            DateTime now = DateTime.UtcNow;
            List<string> statements = new List<string>();
            foreach (string id in targetIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal)) statements.AddRange(WriteStatements(targetType, id, metadata, now));
            if (statements.Count == 0) return;
            await TransactionAsync(statements, token).ConfigureAwait(false);
        }

        public async Task DeletePublicAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            RequireText(targetId, nameof(targetId));
            string where = " WHERE " + Scope() + " AND targettype = " + D.Enum(targetType) + " AND targetid = " + D.Str(targetId);
            await TransactionAsync(new List<string>
            {
                "DELETE FROM labels" + where + " AND substr(labelnormalized, 1, 6) <> " + D.Str(MetadataRules.StaffPrefix) + ";",
                "DELETE FROM tags" + where + " AND substr(tagkeynormalized, 1, 6) <> " + D.Str(MetadataRules.StaffPrefix) + ";"
            }, token).ConfigureAwait(false);
        }

        public List<string> DeleteStatements(TargetTypeEnum targetType, string targetId)
        {
            RequireText(targetId, nameof(targetId));
            string where = " WHERE " + Scope() + " AND targettype = " + D.Enum(targetType) + " AND targetid = " + D.Str(targetId) + ";";
            return new List<string> { "DELETE FROM labels" + where, "DELETE FROM tags" + where };
        }

        public List<string> DeleteWhereStatements(TargetTypeEnum targetType, string idSubquery)
        {
            RequireText(idSubquery, nameof(idSubquery));
            string where = " WHERE " + Scope() + " AND targettype = " + D.Enum(targetType) + " AND targetid IN (" + idSubquery + ");";
            return new List<string> { "DELETE FROM labels" + where, "DELETE FROM tags" + where };
        }

        public async Task DeleteAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            await TransactionAsync(DeleteStatements(targetType, targetId), token).ConfigureAwait(false);
        }

        public async Task<MetadataTarget?> ReadTargetAsync(TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            RequireText(targetId, nameof(targetId));
            string? table = TableFor(targetType);
            if (table == null) return null;
            string? owner = OwnerColumnFor(targetType);
            string? live = LiveConditionFor(targetType);
            DataTable rows = await QueryAsync("SELECT id" + (owner == null ? String.Empty : ", " + owner + " AS ownerid") + " FROM " + table +
                " WHERE " + Scope() + " AND id = " + D.Str(targetId) + (live == null ? String.Empty : " AND " + live) + ";", token).ConfigureAwait(false);
            if (rows.Rows.Count == 0) return null;
            return new MetadataTarget { TargetType = targetType, TargetId = targetId, OwnerUserId = owner == null ? null : RowReader.GetNullableString(rows.Rows[0], "ownerid") };
        }

        public Task<EnumerationResult<LabelCount>> EnumerateLabelsAsync(EnumerationRequest request, TargetTypeEnum? targetType, string? prefix, string? viewerUserId, bool isStaff, CancellationToken token = default)
        {
            return DiscoverAsync(request, "labels", "label", "labelnormalized", targetType, prefix, null, viewerUserId, isStaff,
                row => new LabelCount { Label = RowReader.GetString(row, "display"), Count = RowReader.GetLong(row, "cnt") }, token);
        }

        public Task<EnumerationResult<TagKeyCount>> EnumerateTagKeysAsync(EnumerationRequest request, TargetTypeEnum? targetType, string? prefix, string? viewerUserId, bool isStaff, CancellationToken token = default)
        {
            return DiscoverAsync(request, "tags", "tagkey", "tagkeynormalized", targetType, prefix, null, viewerUserId, isStaff,
                row => new TagKeyCount { Key = RowReader.GetString(row, "display"), Count = RowReader.GetLong(row, "cnt") }, token);
        }

        public Task<EnumerationResult<TagValueCount>> EnumerateTagValuesAsync(EnumerationRequest request, TargetTypeEnum? targetType, string key, string? prefix, string? viewerUserId, bool isStaff, CancellationToken token = default)
        {
            RequireText(key, nameof(key));
            string keyCondition = "m.tagkeynormalized = " + D.Str(MetadataRules.Normalize(key));
            return DiscoverAsync(request, "tags", "tagvalue", "tagvalue", targetType, null, keyCondition + (String.IsNullOrEmpty(prefix) ? String.Empty : " AND " + D.StartsWith("lower(m.tagvalue)", prefix.Trim().ToLowerInvariant())),
                viewerUserId, isStaff, row => new TagValueCount { Value = RowReader.GetString(row, "display"), Count = RowReader.GetLong(row, "cnt") }, token);
        }

        private async Task<EnumerationResult<T>> DiscoverAsync<T>(EnumerationRequest request, string table, string displayColumn, string groupColumn, TargetTypeEnum? targetType,
            string? prefix, string? extraCondition, string? viewerUserId, bool isStaff, Func<DataRow, T> map, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = new List<string> { Scope("m") };
            if (targetType != null) conditions.Add("m.targettype = " + D.Enum(targetType.Value));
            if (!isStaff)
            {
                string staffColumn = table == "labels" ? "m.labelnormalized" : "m.tagkeynormalized";
                conditions.Add("substr(" + staffColumn + ", 1, 6) <> " + D.Str(MetadataRules.StaffPrefix));
                List<string> visible = new List<string>();
                foreach (TargetTypeEnum type in targetType == null ? Enum.GetValues<TargetTypeEnum>() : new[] { targetType.Value })
                {
                    string? subquery = VisibleSubquery(type, viewerUserId);
                    if (subquery != null) visible.Add("(m.targettype = " + D.Enum(type) + " AND m.targetid IN (" + subquery + "))");
                }
                conditions.Add(visible.Count == 0 ? "1 = 0" : "(" + String.Join(" OR ", visible) + ")");
            }
            if (!String.IsNullOrEmpty(prefix)) conditions.Add(D.StartsWith("m." + groupColumn, prefix.Trim().ToLowerInvariant()));
            if (extraCondition != null) conditions.Add(extraCondition);
            if (!String.IsNullOrEmpty(request.Query.Search)) conditions.Add(D.Contains("m." + displayColumn, request.Query.Search));
            if (request.Query.CreatedAfterUtc != null) conditions.Add("m.createdutc >= " + D.Ts(request.Query.CreatedAfterUtc.Value));
            if (request.Query.CreatedBeforeUtc != null) conditions.Add("m.createdutc < " + D.Ts(request.Query.CreatedBeforeUtc.Value));

            long offset = request.Skip;
            if (request.HasPosition)
            {
                if (!Int64.TryParse(request.AfterKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out offset) || offset < 0) throw ContinuationTokens.Invalid();
            }
            string where = Where(conditions);
            DataTable rows = await QueryAsync("SELECT MIN(m." + displayColumn + ") AS display, COUNT(*) AS cnt FROM " + table + " m" + where +
                " GROUP BY m." + groupColumn + " ORDER BY cnt DESC, m." + groupColumn + " ASC LIMIT " + D.Num(request.MaxResults + 1) + " OFFSET " + D.Num(offset) + ";", token).ConfigureAwait(false);
            List<DataRow> list = rows.Rows.Cast<DataRow>().ToList();
            bool more = list.Count > request.MaxResults;
            if (more) list = list.Take(request.MaxResults).ToList();
            long total = await CountAsync("SELECT COUNT(DISTINCT m." + groupColumn + ") AS cnt FROM " + table + " m" + where + ";", token).ConfigureAwait(false);
            long next = offset + list.Count;
            return new EnumerationResult<T>
            {
                MaxResults = request.MaxResults,
                Skip = request.HasPosition ? 0 : request.Skip,
                EndOfResults = !more,
                ContinuationToken = more ? ContinuationTokens.Encode(request.Fingerprint, request.Ordering.Name, next.ToString(CultureInfo.InvariantCulture), "offset") : null,
                TotalRecords = total,
                RecordsRemaining = Math.Max(0, total - next),
                TimestampUtc = DateTime.UtcNow,
                Objects = list.Select(map).ToList()
            };
        }

        private List<string> WriteStatements(TargetTypeEnum targetType, string targetId, ObjectMetadata metadata, DateTime now)
        {
            string type = D.Enum(targetType);
            List<string> statements = DeleteStatements(targetType, targetId);
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string label in metadata.Labels)
            {
                string normalized = MetadataRules.Normalize(label);
                if (!seen.Add(normalized)) continue;
                DateTime at = now.AddMilliseconds(seen.Count);
                statements.Add("INSERT INTO labels (propertyid, id, targettype, targetid, label, labelnormalized, createdutc) VALUES (" +
                    Pid + ", " + D.Str(IdGenerator.Generate(_LabelIdPrefix)) + ", " + type + ", " + D.Str(targetId) + ", " + D.Str(label.Trim()) + ", " + D.Str(normalized) + ", " + D.Ts(at) + ");");
            }
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in metadata.Tags)
            {
                string normalized = MetadataRules.Normalize(pair.Key);
                if (!keys.Add(normalized)) continue;
                statements.Add("INSERT INTO tags (propertyid, id, targettype, targetid, tagkey, tagkeynormalized, tagvalue, createdutc, updatedutc) VALUES (" +
                    Pid + ", " + D.Str(IdGenerator.Generate(_TagIdPrefix)) + ", " + type + ", " + D.Str(targetId) + ", " + D.Str(pair.Key.Trim()) + ", " + D.Str(normalized) + ", " +
                    D.Str(pair.Value ?? String.Empty) + ", " + D.Ts(now.AddMilliseconds(keys.Count)) + ", " + D.Ts(now) + ");");
            }
            return statements;
        }

        private string? VisibleSubquery(TargetTypeEnum type, string? viewerUserId)
        {
            string viewer = D.Str(viewerUserId ?? String.Empty);
            switch (type)
            {
                case TargetTypeEnum.FeedPost:
                    return "SELECT id FROM feedposts WHERE " + Scope() + " AND deletedutc IS NULL AND moderationstate = 'None' AND publishedutc IS NOT NULL";
                case TargetTypeEnum.Comment:
                    return "SELECT id FROM comments WHERE " + Scope() + " AND deletedutc IS NULL AND moderationstate = 'None'";
                case TargetTypeEnum.FeedPostRevision:
                    return "SELECT fr.id FROM feedpostrevisions fr JOIN feedposts fp ON fp.id = fr.feedpostid AND " + Scope("fp") + " WHERE " + Scope("fr") + " AND fp.deletedutc IS NULL AND fp.moderationstate = 'None' AND fp.publishedutc IS NOT NULL";
                case TargetTypeEnum.CommentRevision:
                    return "SELECT cr.id FROM commentrevisions cr JOIN comments cc ON cc.id = cr.commentid AND " + Scope("cc") + " WHERE " + Scope("cr") + " AND cc.deletedutc IS NULL AND cc.moderationstate = 'None'";
                case TargetTypeEnum.User:
                    return "SELECT id FROM users WHERE " + Scope() + " AND isdeleted = " + D.Bool(false);
                case TargetTypeEnum.Forum:
                    return "SELECT id FROM forums WHERE " + Scope() + " AND deletedutc IS NULL";
                case TargetTypeEnum.Thread:
                    return "SELECT id FROM forumthreads WHERE " + Scope() + " AND deletedutc IS NULL AND moderationstate = 'None' AND publishedutc IS NOT NULL";
                case TargetTypeEnum.ForumFollow:
                    return "SELECT id FROM forumfollows WHERE " + Scope() + " AND userid = " + viewer;
                case TargetTypeEnum.ThreadFollow:
                    return "SELECT id FROM threadfollows WHERE " + Scope() + " AND userid = " + viewer;
                case TargetTypeEnum.Media:
                    return "SELECT id FROM mediaattachments WHERE " + Scope() + " AND deletedutc IS NULL AND ((attachedtargetid IS NOT NULL AND purpose = 'Content') OR owneruserid = " + viewer + ")";
                case TargetTypeEnum.Reaction:
                    return "SELECT id FROM reactions WHERE " + Scope();
                case TargetTypeEnum.Notification:
                    return "SELECT id FROM notifications WHERE " + Scope() + " AND recipientuserid = " + viewer;
                case TargetTypeEnum.PushToken:
                    return "SELECT id FROM pushtokens WHERE " + Scope() + " AND userid = " + viewer;
                case TargetTypeEnum.Ticket:
                    return "SELECT id FROM tickets WHERE " + Scope() + " AND requesteruserid = " + viewer;
                case TargetTypeEnum.ConversationMember:
                    return "SELECT id FROM conversationmembers WHERE " + Scope() + " AND userid = " + viewer;
                case TargetTypeEnum.Message:
                    return "SELECT id FROM messages WHERE " + Scope() + " AND authoruserid = " + viewer;
                case TargetTypeEnum.EventRsvp:
                case TargetTypeEnum.CalendarFeed:
                case TargetTypeEnum.LessonProgress:
                case TargetTypeEnum.LessonDownload:
                case TargetTypeEnum.SeriesFollow:
                case TargetTypeEnum.IdeaVote:
                case TargetTypeEnum.DocumentAcceptance:
                case TargetTypeEnum.AssistantConversation:
                case TargetTypeEnum.Favorite:
                case TargetTypeEnum.FavoriteCollection:
                    return "SELECT id FROM " + TableFor(type) + " WHERE " + Scope() + " AND userid = " + viewer;
                case TargetTypeEnum.DirectorySubmission:
                    return "SELECT id FROM directorysubmissions WHERE " + Scope() + " AND submitteruserid = " + viewer;
                case TargetTypeEnum.TicketMessage:
                    return "SELECT id FROM ticketmessages WHERE " + Scope() + " AND authoruserid = " + viewer;
                case TargetTypeEnum.Idea:
                    return "SELECT id FROM ideas WHERE " + Scope() + " AND deletedutc IS NULL AND authoruserid = " + viewer;
                case TargetTypeEnum.Event:
                    return "SELECT id FROM events WHERE " + Scope() + " AND deletedutc IS NULL AND proposedbyuserid = " + viewer;
                default:
                    return null;
            }
        }

        private static string? TableFor(TargetTypeEnum type)
        {
            switch (type)
            {
                case TargetTypeEnum.JobDefinition: return "jobdefinitions";
                case TargetTypeEnum.User: return "users";
                case TargetTypeEnum.FeedPost: return "feedposts";
                case TargetTypeEnum.Forum: return "forums";
                case TargetTypeEnum.Thread: return "forumthreads";
                case TargetTypeEnum.ThreadRevision: return "forumthreadrevisions";
                case TargetTypeEnum.Poll: return "polls";
                case TargetTypeEnum.PollOption: return "polloptions";
                case TargetTypeEnum.PollVote: return "pollvotes";
                case TargetTypeEnum.ForumFollow: return "forumfollows";
                case TargetTypeEnum.ThreadFollow: return "threadfollows";
                case TargetTypeEnum.ForumMute: return "forummutes";
                case TargetTypeEnum.Comment: return "comments";
                case TargetTypeEnum.Media: return "mediaattachments";
                case TargetTypeEnum.Reaction: return "reactions";
                case TargetTypeEnum.Notification: return "notifications";
                case TargetTypeEnum.Report: return "reports";
                case TargetTypeEnum.ModerationAction: return "moderationactions";
                case TargetTypeEnum.WordFilterTerm: return "wordfilterterms";
                case TargetTypeEnum.PushToken: return "pushtokens";
                case TargetTypeEnum.SignupCode: return "signupcodes";
                case TargetTypeEnum.SignupCodeRedemption: return "signupcoderedemptions";
                case TargetTypeEnum.Subscription: return "subscriptions";
                case TargetTypeEnum.Entitlement: return "entitlements";
                case TargetTypeEnum.Session: return "authsessions";
                case TargetTypeEnum.AuditRecord: return "auditrecords";
                case TargetTypeEnum.RequestHistory: return "requesthistory";
                case TargetTypeEnum.SignupCodeBatch: return "signupcodebatches";
                case TargetTypeEnum.UpdateGroup: return "updategroups";
                case TargetTypeEnum.UpdateDeployKey: return "updatedeploykeys";
                case TargetTypeEnum.UpdateSigningKey: return "updatesigningkeys";
                case TargetTypeEnum.UsageRecord: return "usagedaily";
                case TargetTypeEnum.Ticket: return "tickets";
                case TargetTypeEnum.TicketType: return "tickettypes";
                case TargetTypeEnum.ConversationMember: return "conversationmembers";
                case TargetTypeEnum.Message: return "messages";
                case TargetTypeEnum.Conversation: return "conversations";
                case TargetTypeEnum.TicketMessage: return "ticketmessages";
                case TargetTypeEnum.Venue: return "venues";
                case TargetTypeEnum.Instructor: return "instructors";
                case TargetTypeEnum.VenueInstructor: return "venueinstructors";
                case TargetTypeEnum.DirectoryProgram: return "directoryprograms";
                case TargetTypeEnum.DirectorySubmission: return "directorysubmissions";
                case TargetTypeEnum.Event: return "events";
                case TargetTypeEnum.EventCategory: return "eventcategories";
                case TargetTypeEnum.EventRsvp: return "eventrsvps";
                case TargetTypeEnum.EventReminder: return "eventreminders";
                case TargetTypeEnum.EventRecurrence: return "eventrecurrences";
                case TargetTypeEnum.CalendarFeed: return "calendarfeeds";
                case TargetTypeEnum.Series: return "series";
                case TargetTypeEnum.Lesson: return "lessons";
                case TargetTypeEnum.LessonResource: return "lessonresources";
                case TargetTypeEnum.LessonMarker: return "lessonmarkers";
                case TargetTypeEnum.LessonCaption: return "lessoncaptions";
                case TargetTypeEnum.LessonProgress: return "lessonprogress";
                case TargetTypeEnum.LessonDownload: return "lessondownloads";
                case TargetTypeEnum.SeriesFollow: return "seriesfollows";
                case TargetTypeEnum.LibraryLevel: return "librarylevels";
                case TargetTypeEnum.Idea: return "ideas";
                case TargetTypeEnum.IdeaVote: return "ideavotes";
                case TargetTypeEnum.IdeaStatus: return "ideastatuses";
                case TargetTypeEnum.Broadcast: return "broadcasts";
                case TargetTypeEnum.BroadcastSegment: return "broadcastsegments";
                case TargetTypeEnum.BroadcastDelivery: return "broadcastdeliveries";
                case TargetTypeEnum.PropertyDocumentVersion: return "propertydocumentversions";
                case TargetTypeEnum.DocumentAcceptance: return "propertydocumentacceptances";
                case TargetTypeEnum.AssistantConversation: return "aiconversations";
                case TargetTypeEnum.Favorite: return "favorites";
                case TargetTypeEnum.FavoriteCollection: return "favoritecollections";
                default: return null;
            }
        }

        private static string? OwnerColumnFor(TargetTypeEnum type)
        {
            switch (type)
            {
                case TargetTypeEnum.User: return "id";
                case TargetTypeEnum.FeedPost: return "authoruserid";
                case TargetTypeEnum.Thread: return "authoruserid";
                case TargetTypeEnum.ForumFollow: return "userid";
                case TargetTypeEnum.ThreadFollow: return "userid";
                case TargetTypeEnum.Comment: return "authoruserid";
                case TargetTypeEnum.Media: return "owneruserid";
                case TargetTypeEnum.Reaction: return "userid";
                case TargetTypeEnum.Notification: return "recipientuserid";
                case TargetTypeEnum.PushToken: return "userid";
                case TargetTypeEnum.Ticket: return "requesteruserid";
                case TargetTypeEnum.ConversationMember: return "userid";
                case TargetTypeEnum.Message: return "authoruserid";
                case TargetTypeEnum.TicketMessage: return "authoruserid";
                case TargetTypeEnum.DirectorySubmission: return "submitteruserid";
                case TargetTypeEnum.Event: return "proposedbyuserid";
                case TargetTypeEnum.EventRsvp: return "userid";
                case TargetTypeEnum.CalendarFeed: return "userid";
                case TargetTypeEnum.LessonProgress: return "userid";
                case TargetTypeEnum.LessonDownload: return "userid";
                case TargetTypeEnum.SeriesFollow: return "userid";
                case TargetTypeEnum.Idea: return "authoruserid";
                case TargetTypeEnum.IdeaVote: return "userid";
                case TargetTypeEnum.BroadcastDelivery: return "userid";
                case TargetTypeEnum.DocumentAcceptance: return "userid";
                case TargetTypeEnum.AssistantConversation: return "userid";
                case TargetTypeEnum.Favorite: return "userid";
                case TargetTypeEnum.FavoriteCollection: return "userid";
                default: return null;
            }
        }

        private string? LiveConditionFor(TargetTypeEnum type)
        {
            switch (type)
            {
                case TargetTypeEnum.User: return "isdeleted = " + D.Bool(false);
                case TargetTypeEnum.FeedPost:
                case TargetTypeEnum.Comment:
                case TargetTypeEnum.Forum:
                case TargetTypeEnum.Thread:
                case TargetTypeEnum.Media:
                case TargetTypeEnum.Venue:
                case TargetTypeEnum.Instructor:
                case TargetTypeEnum.Event:
                case TargetTypeEnum.EventRecurrence:
                case TargetTypeEnum.Series:
                case TargetTypeEnum.Lesson:
                case TargetTypeEnum.Idea:
                case TargetTypeEnum.BroadcastSegment: return "deletedutc IS NULL";
                default: return null;
            }
        }
    }
}
