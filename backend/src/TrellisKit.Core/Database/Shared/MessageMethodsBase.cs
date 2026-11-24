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
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    internal abstract class MessageMethodsBase : MethodsBase, IMessageMethods
    {
        protected MessageMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<Message?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            Dictionary<string, Message> messages = await ReadManyAsync(new[] { id }, token).ConfigureAwait(false);
            return messages.TryGetValue(id, out Message? message) ? message : null;
        }

        public async Task<Dictionary<string, Message>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            List<string> list = ids.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (list.Count == 0) return new Dictionary<string, Message>(StringComparer.Ordinal);
            DataTable table = await QueryAsync("SELECT * FROM messages WHERE " + Scope() + " AND id IN " + D.InList(list) + ";", token).ConfigureAwait(false);
            List<Message> messages = table.Rows.Cast<DataRow>().Select(MessagingRows.Message).ToList();
            await LoadMediaAsync(messages, token).ConfigureAwait(false);
            return messages.ToDictionary(m => m.Id, StringComparer.Ordinal);
        }

        public async Task<Message?> ReadByClientIdAsync(string conversationId, string authorUserId, string clientMessageId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(conversationId) || String.IsNullOrEmpty(authorUserId) || String.IsNullOrEmpty(clientMessageId)) return null;
            DataTable table = await QueryAsync("SELECT * FROM messages WHERE " + Scope() + " AND conversationid = " + D.Str(conversationId) + " AND authoruserid = " + D.Str(authorUserId) +
                " AND clientmessageid = " + D.Str(clientMessageId) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            List<Message> messages = new List<Message> { MessagingRows.Message(table.Rows[0]) };
            await LoadMediaAsync(messages, token).ConfigureAwait(false);
            return messages[0];
        }

        public async Task<MessageSendOutcome> SendAsync(MessageDraft draft, string? notifyStatement, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(draft);
            RequireText(draft.Id, nameof(draft.Id));
            RequireText(draft.ConversationId, nameof(draft.ConversationId));
            RequireText(draft.SenderUserId, nameof(draft.SenderUserId));
            RequireText(draft.ClientMessageId, nameof(draft.ClientMessageId));
            string id = D.Str(draft.Id);
            string conversation = D.Str(draft.ConversationId);
            string sender = D.Str(draft.SenderUserId);
            string now = D.Ts(draft.NowUtc);
            string stored = "EXISTS (SELECT 1 FROM messages k WHERE " + Scope("k") + " AND k.id = " + id + ")";
            string seq = "(SELECT k.seq FROM messages k WHERE " + Scope("k") + " AND k.id = " + id + ")";
            string awaiting = D.Enum(ConversationRequestStateEnum.AwaitingAcceptance);
            string pendingOrDeclined = D.InList(new[] { ConversationRequestStateEnum.Pending.ToString(), ConversationRequestStateEnum.Declined.ToString() });
            List<string> statements = new List<string>
            {
                // Lock and increment the conversation (concurrent sends serialize on this row), unless the sender reached
                // the request message limit.
                "UPDATE conversations SET lastseq = lastseq + 1, lastmessageid = " + id + ", lastmessageutc = " + now + ", purgeafterutc = NULL, updatedutc = " + now + " WHERE " + Scope() + " AND id = " + conversation +
                    " AND NOT EXISTS (SELECT 1 FROM conversationmembers lm WHERE " + Scope("lm") + " AND lm.conversationid = " + conversation + " AND lm.userid = " + sender + " AND lm.requeststate = " + awaiting +
                    " AND lm.requestmessagecount >= " + D.Num(Math.Max(1, draft.RequestMessageLimit)) + ");",
                "INSERT INTO messages (propertyid, id, conversationid, seq, clientmessageid, authoruserid, body, originalbody, excerpt, replytomessageid, linkpreviewid, showlinkpreview, attachmentcount, hasvideo, state, " +
                    "reactiontotal, createdutc, updatedutc) SELECT " + Pid + ", " + id + ", " + conversation + ", c.lastseq, " + D.Str(draft.ClientMessageId) + ", " + sender + ", " + D.Str(draft.Body) + ", NULL, " +
                    D.Str(draft.Excerpt) + ", " + D.Str(draft.ReplyToMessageId) + ", " + D.Str(draft.LinkPreviewId) + ", " + D.Bool(draft.ShowLinkPreview) + ", " + D.Num(draft.MediaIds.Count) + ", " + D.Bool(draft.HasVideo) + ", " +
                    D.Enum(MessageStateEnum.Visible) + ", 0, " + now + ", " + now + " FROM conversations c WHERE " + Scope("c") + " AND c.id = " + conversation + " AND c.lastmessageid = " + id + ";"
            };
            if (draft.MediaIds.Count > 0)
            {
                statements.Add("INSERT INTO messageattachments (propertyid, messageid, mediaid, position) SELECT " + Pid + ", " + id + ", v.mediaid, v.pos FROM (VALUES " +
                    String.Join(", ", draft.MediaIds.Select((m, i) => "(" + D.Str(m) + ", " + D.Num(i) + ")")) + ") AS v(mediaid, pos) WHERE " + stored + ";");
            }
            // Other members: activity, revival after delete for me (never for a member who declined), and a reply by the
            // request recipient accepts the request for the sender too.
            statements.Add("UPDATE conversationmembers o SET lastactivityutc = " + now + ", updatedutc = " + now + ", deletedutc = CASE WHEN o.requeststate = " + D.Enum(ConversationRequestStateEnum.Declined) +
                " THEN o.deletedutc ELSE NULL END, requeststate = CASE WHEN o.requeststate = " + awaiting + " AND EXISTS (SELECT 1 FROM conversationmembers s WHERE " + Scope("s") + " AND s.conversationid = " + conversation +
                " AND s.userid = " + sender + " AND s.requeststate IN " + pendingOrDeclined + ") THEN NULL ELSE o.requeststate END WHERE " + Scope("o") + " AND o.conversationid = " + conversation +
                " AND o.userid <> " + sender + " AND o.leftutc IS NULL AND " + stored + ";");
            statements.Add("UPDATE conversationmembers SET lastreadseq = GREATEST(lastreadseq, " + seq + "), lastdeliveredseq = GREATEST(lastdeliveredseq, " + seq + "), lastactivityutc = " + now + ", updatedutc = " + now +
                ", deletedutc = NULL, requestmessagecount = CASE WHEN requeststate = " + awaiting + " THEN requestmessagecount + 1 ELSE requestmessagecount END, folder = CASE WHEN requeststate IN " + pendingOrDeclined + " THEN " +
                D.Enum(ConversationFolderEnum.Inbox) + " ELSE folder END, acceptedutc = CASE WHEN requeststate IN " + pendingOrDeclined + " THEN " + now + " ELSE acceptedutc END, declinedutc = CASE WHEN requeststate IN " +
                pendingOrDeclined + " THEN NULL ELSE declinedutc END, requeststate = CASE WHEN requeststate IN " + pendingOrDeclined + " THEN NULL ELSE requeststate END WHERE " + Scope() + " AND conversationid = " + conversation +
                " AND userid = " + sender + " AND " + stored + ";");
            statements.Add("INSERT INTO messagingcontacts (propertyid, userid, contactuserid, source, createdutc) SELECT " + Pid + ", " + sender + ", o.userid, " + D.Enum(MessagingContactSourceEnum.Messaged) + ", " + now +
                " FROM conversationmembers o WHERE " + Scope("o") + " AND o.conversationid = " + conversation + " AND o.userid <> " + sender + " AND " + stored + " ON CONFLICT DO NOTHING;");
            if (!String.IsNullOrEmpty(notifyStatement)) statements.Add(notifyStatement);
            statements.Add("SELECT * FROM messages WHERE " + Scope() + " AND id = " + id + ";");
            DataTable table;
            try
            {
                table = await TransactionAsync(statements, token).ConfigureAwait(false);
            }
            catch (Exception e) when (D.IsUniqueViolation(e))
            {
                // A concurrent retry with the same client message id committed first: the whole send rolled back.
                Message? existing = await ReadByClientIdAsync(draft.ConversationId, draft.SenderUserId, draft.ClientMessageId, token).ConfigureAwait(false);
                if (existing == null) throw;
                return new MessageSendOutcome { Message = existing, Created = false };
            }
            if (table.Rows.Count == 0) return new MessageSendOutcome { Message = null, Created = false, LimitReached = true };
            Message message = MessagingRows.Message(table.Rows[0]);
            message.MediaIds = new List<string>(draft.MediaIds);
            return new MessageSendOutcome { Message = message, Created = true };
        }

        public async Task<Message?> EditAsync(string id, string authorUserId, string body, string excerpt, string? linkPreviewId, DateTime editableSinceUtc, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            RequireText(authorUserId, nameof(authorUserId));
            DataTable table = await QueryAsync("UPDATE messages SET originalbody = body, body = " + D.Str(body ?? String.Empty) + ", excerpt = " + D.Str(excerpt ?? String.Empty) + ", linkpreviewid = " + D.Str(linkPreviewId) + ", editedutc = " + D.Ts(nowUtc) +
                ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND authoruserid = " + D.Str(authorUserId) + " AND state = " + D.Enum(MessageStateEnum.Visible) +
                " AND editedutc IS NULL AND createdutc >= " + D.Ts(editableSinceUtc) + " RETURNING *;", token).ConfigureAwait(false);
            return await OneAsync(table, token).ConfigureAwait(false);
        }

        public async Task<Message?> DeleteForEveryoneAsync(string id, string authorUserId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            RequireText(authorUserId, nameof(authorUserId));
            DataTable table = await QueryAsync("UPDATE messages SET state = " + D.Enum(MessageStateEnum.Deleted) + ", deletedutc = " + D.Ts(nowUtc) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() +
                " AND id = " + D.Str(id) + " AND authoruserid = " + D.Str(authorUserId) + " AND state <> " + D.Enum(MessageStateEnum.Deleted) + " RETURNING *;", token).ConfigureAwait(false);
            return await OneAsync(table, token).ConfigureAwait(false);
        }

        public async Task<bool> SetRemovedAsync(string id, bool removed, string? moderatorUserId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            MessageStateEnum from = removed ? MessageStateEnum.Visible : MessageStateEnum.Removed;
            MessageStateEnum to = removed ? MessageStateEnum.Removed : MessageStateEnum.Visible;
            DataTable table = await QueryAsync("UPDATE messages SET state = " + D.Enum(to) + ", moderatedutc = " + D.Ts(nowUtc) + ", moderatedbyuserid = " + D.Str(moderatorUserId) + ", updatedutc = " + D.Ts(nowUtc) +
                " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND state = " + D.Enum(from) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task RecomputeReactionTotalAsync(string id, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            await QueryAsync("UPDATE messages SET reactiontotal = (SELECT COUNT(*) FROM reactions r WHERE " + Scope("r") + " AND r.targettype = " + D.Enum(TargetTypeEnum.Message) + " AND r.targetid = " + D.Str(id) +
                "), updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
        }

        public async Task<EnumerationResult<Message>> EnumerateHistoryAsync(string conversationId, long hiddenBeforeSeq, EnumerationRequest request, long? beforeSeq, long? afterSeq, string? search, CancellationToken token = default)
        {
            RequireText(conversationId, nameof(conversationId));
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = new List<string> { "m.conversationid = " + D.Str(conversationId), "m.seq > " + D.Num(hiddenBeforeSeq) };
            if (beforeSeq != null) conditions.Add("m.seq < " + D.Num(beforeSeq.Value));
            if (afterSeq != null) conditions.Add("m.seq > " + D.Num(afterSeq.Value));
            if (!String.IsNullOrWhiteSpace(search)) conditions.Add(Matches(search) + " AND m.state = " + D.Enum(MessageStateEnum.Visible));
            EnumerationResult<Message> page = await EnumerateAsync(request, "m.*", "messages m", conditions, MessagingRows.Message, token).ConfigureAwait(false);
            await LoadMediaAsync(page.Objects, token).ConfigureAwait(false);
            return page;
        }

        public async Task<EnumerationResult<Message>> AroundAsync(string conversationId, long hiddenBeforeSeq, long seq, int half, EnumerationRequest request, CancellationToken token = default)
        {
            RequireText(conversationId, nameof(conversationId));
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = EnumerationConditions(request);
            conditions.Add("m.conversationid = " + D.Str(conversationId));
            conditions.Add("m.seq > " + D.Num(hiddenBeforeSeq));
            int limit = Math.Clamp(half, 1, 50);
            string before = "SELECT m.* FROM messages m" + Where(conditions) + " AND m.seq <= " + D.Num(seq) + " ORDER BY m.seq DESC LIMIT " + D.Num(limit);
            string after = "SELECT m.* FROM messages m" + Where(conditions) + " AND m.seq > " + D.Num(seq) + " ORDER BY m.seq ASC LIMIT " + D.Num(limit);
            string from = "((" + before + ") UNION ALL (" + after + ")) m";
            EnumerationResult<Message> page = await EnumerateAsync(request, "m.*", from, new List<string>(), MessagingRows.Message, token).ConfigureAwait(false);
            await LoadMediaAsync(page.Objects, token).ConfigureAwait(false);
            return page;
        }

        public async Task<EnumerationResult<MessageSearchRow>> SearchAsync(string userId, EnumerationRequest request, string? conversationId, string? withUserId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            ArgumentNullException.ThrowIfNull(request);
            string text = request.Query.Search ?? String.Empty;
            string me = D.Str(userId);
            if (String.Equals(request.Ordering.Name, "Relevance", StringComparison.Ordinal))
            {
                request.Ordering = new EnumerationOrdering { Name = "Relevance", Expression = "(ts_rank(m.searchvector, " + Query(text) + ") * 1000000)::bigint", Descending = true, KeyKind = EnumerationKeyKindEnum.Number };
            }
            string from = "messages m JOIN conversationmembers cm ON " + Scope("cm") + " AND cm.conversationid = m.conversationid AND cm.userid = " + me + " LEFT JOIN conversationmembers om ON " + Scope("om") +
                " AND om.conversationid = m.conversationid AND om.userid <> " + me;
            List<string> conditions = new List<string>
            {
                "m.seq > cm.hiddenbeforeseq",
                "cm.deletedutc IS NULL AND cm.leftutc IS NULL AND (cm.requeststate IS NULL OR cm.requeststate <> " + D.Enum(ConversationRequestStateEnum.Declined) + ")",
                "m.state = " + D.Enum(MessageStateEnum.Visible),
                Matches(text),
                "NOT EXISTS (SELECT 1 FROM blocks bk WHERE " + Scope("bk") + " AND bk.blockeruserid = " + me + " AND bk.blockeduserid = om.userid)"
            };
            if (!String.IsNullOrEmpty(conversationId)) conditions.Add("m.conversationid = " + D.Str(conversationId));
            if (!String.IsNullOrEmpty(withUserId)) conditions.Add("om.userid = " + D.Str(withUserId));
            string select = "m.*, ts_headline('simple', m.body, " + Query(text) + ", " + D.Str("StartSel=**, StopSel=**, MaxWords=24, MinWords=6, MaxFragments=1") + ") AS headline";
            EnumerationResult<MessageSearchRow> page = await EnumerateAsync(request, select, from, conditions, row => new MessageSearchRow
            {
                Message = MessagingRows.Message(row),
                Headline = RowReader.GetNullableString(row, "headline") ?? String.Empty
            }, token).ConfigureAwait(false);
            await LoadMediaAsync(page.Objects.Select(o => o.Message).ToList(), token).ConfigureAwait(false);
            return page;
        }

        public async Task<List<Message>> LatestUpToAsync(string conversationId, long atSeq, long minSeqExclusive, int limit, CancellationToken token = default)
        {
            RequireText(conversationId, nameof(conversationId));
            DataTable table = await QueryAsync("SELECT * FROM messages WHERE " + Scope() + " AND conversationid = " + D.Str(conversationId) + " AND seq <= " + D.Num(atSeq) + " AND seq > " + D.Num(minSeqExclusive) +
                " ORDER BY seq DESC LIMIT " + D.Num(Math.Clamp(limit, 1, 200)) + ";", token).ConfigureAwait(false);
            List<Message> messages = table.Rows.Cast<DataRow>().Select(MessagingRows.Message).ToList();
            await LoadMediaAsync(messages, token).ConfigureAwait(false);
            return messages;
        }

        public async Task<bool> CreateSnapshotAsync(MessageReportSnapshot snapshot, List<MessageReportSnapshotItem> items, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(items);
            RequireText(snapshot.Id, nameof(snapshot.Id));
            string id = D.Str(snapshot.Id);
            List<string> statements = new List<string>
            {
                "INSERT INTO messagereportsnapshots (propertyid, id, reportid, conversationid, reporteruserid, capturedutc, purgeafterutc) VALUES (" + Pid + ", " + id + ", " + D.Str(snapshot.ReportId) + ", " +
                    D.Str(snapshot.ConversationId) + ", " + D.Str(snapshot.ReporterUserId) + ", " + D.Ts(snapshot.CapturedUtc) + ", NULL) ON CONFLICT DO NOTHING;"
            };
            string mine = "EXISTS (SELECT 1 FROM messagereportsnapshots k WHERE " + Scope("k") + " AND k.id = " + id + ")";
            foreach (MessageReportSnapshotItem item in items)
            {
                statements.Add("INSERT INTO messagereportsnapshotitems (propertyid, id, snapshotid, messageid, seq, authoruserid, body, originalbody, state, isreported, attachmentsummary, editedutc, createdutc) SELECT " + Pid + ", " +
                    D.Str(item.Id) + ", " + id + ", " + D.Str(item.MessageId) + ", " + D.Num(item.Seq) + ", " + D.Str(item.AuthorUserId) + ", " + D.Str(item.Body) + ", " + D.Str(item.OriginalBody) + ", " + D.Enum(item.State) + ", " +
                    D.Bool(item.IsReported) + ", " + D.Str(item.AttachmentSummary) + ", " + D.Ts(item.EditedUtc) + ", " + D.Ts(item.CreatedUtc) + " WHERE " + mine + ";");
            }
            statements.Add("SELECT COUNT(*) AS cnt FROM messagereportsnapshots WHERE " + Scope() + " AND id = " + id + ";");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return RowReader.GetLong(table.Rows[0], "cnt") > 0;
        }

        public async Task<MessageReportSnapshot?> ReadSnapshotByReportAsync(string reportId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(reportId)) return null;
            Dictionary<string, MessageReportSnapshot> snapshots = await ReadSnapshotsByReportsAsync(new[] { reportId }, token).ConfigureAwait(false);
            return snapshots.TryGetValue(reportId, out MessageReportSnapshot? snapshot) ? snapshot : null;
        }

        public async Task<Dictionary<string, MessageReportSnapshot>> ReadSnapshotsByReportsAsync(IEnumerable<string> reportIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(reportIds);
            List<string> ids = reportIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return new Dictionary<string, MessageReportSnapshot>(StringComparer.Ordinal);
            DataTable table = await QueryAsync("SELECT * FROM messagereportsnapshots WHERE " + Scope() + " AND reportid IN " + D.InList(ids) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MessagingRows.Snapshot).ToDictionary(s => s.ReportId, StringComparer.Ordinal);
        }

        public Task<EnumerationResult<Report>> EnumerateReportsAsync(EnumerationRequest request, ReportStatusEnum status, TargetTypeEnum? targetType, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = new List<string> { "r.scopetype = " + D.Str("Messaging"), "r.status = " + D.Enum(status) };
            conditions.Add(targetType == null
                ? "r.targettype IN " + D.InList(new[] { TargetTypeEnum.Message.ToString(), TargetTypeEnum.Conversation.ToString() })
                : "r.targettype = " + D.Enum(targetType.Value));
            return EnumerateAsync(request, "r.*", "reports r", conditions, row => new Report
            {
                Id = RowReader.GetString(row, "id"),
                ReporterUserId = RowReader.GetNullableString(row, "reporteruserid"),
                TargetType = RowReader.GetEnum(row, "targettype", TargetTypeEnum.Message),
                TargetId = RowReader.GetString(row, "targetid"),
                TargetAuthorUserId = RowReader.GetNullableString(row, "targetauthoruserid"),
                ScopeType = RowReader.GetString(row, "scopetype"),
                ScopeId = RowReader.GetString(row, "scopeid"),
                Reason = RowReader.GetEnum(row, "reason", ReportReasonEnum.Other),
                Details = RowReader.GetNullableString(row, "details"),
                Status = RowReader.GetEnum(row, "status", ReportStatusEnum.Open),
                ResolvedByUserId = RowReader.GetNullableString(row, "resolvedbyuserid"),
                ResolvedUtc = RowReader.GetNullableDateTime(row, "resolvedutc"),
                ResolutionNote = RowReader.GetNullableString(row, "resolutionnote"),
                ModerationActionId = RowReader.GetNullableString(row, "moderationactionid"),
                DueUtc = RowReader.GetDateTime(row, "dueutc"),
                OverdueNotifiedUtc = RowReader.GetNullableDateTime(row, "overduenotifiedutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            }, token);
        }

        public async Task<List<MessageReportSnapshotItem>> SnapshotItemsAsync(string snapshotId, int limit, CancellationToken token = default)
        {
            RequireText(snapshotId, nameof(snapshotId));
            DataTable table = await QueryAsync("SELECT * FROM messagereportsnapshotitems WHERE " + Scope() + " AND snapshotid = " + D.Str(snapshotId) + " ORDER BY seq, id LIMIT " + D.Num(Math.Clamp(limit, 1, 200)) + ";",
                token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MessagingRows.SnapshotItem).ToList();
        }

        public async Task<MessageReportSnapshotItem?> LatestSnapshotItemAsync(string messageId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(messageId)) return null;
            DataTable table = await QueryAsync("SELECT i.* FROM messagereportsnapshotitems i JOIN messagereportsnapshots s ON " + Scope("s") + " AND s.id = i.snapshotid WHERE " + Scope("i") + " AND i.messageid = " +
                D.Str(messageId) + " ORDER BY s.capturedutc DESC, i.id DESC LIMIT 1;", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : MessagingRows.SnapshotItem(table.Rows[0]);
        }

        public async Task<int> PruneSnapshotsAsync(int retainDays, DateTime nowUtc, int limit, CancellationToken token = default)
        {
            int days = Math.Clamp(retainDays, 1, 3650);
            await QueryAsync("UPDATE messagereportsnapshots s SET purgeafterutc = r.resolvedutc + INTERVAL '" + days.ToString(CultureInfo.InvariantCulture) + " days' FROM reports r WHERE " + Scope("s") + " AND " + Scope("r") +
                " AND r.id = s.reportid AND s.purgeafterutc IS NULL AND r.status <> " + D.Enum(ReportStatusEnum.Open) + " AND r.resolvedutc IS NOT NULL;", token).ConfigureAwait(false);
            string due = "SELECT d.id FROM messagereportsnapshots d WHERE " + Scope("d") + " AND d.purgeafterutc IS NOT NULL AND d.purgeafterutc <= " + D.Ts(nowUtc) + " ORDER BY d.purgeafterutc, d.id LIMIT " + D.Num(Math.Clamp(limit, 1, 1000));
            List<string> statements = new List<string>
            {
                "CREATE TEMP TABLE IF NOT EXISTS snapshotprune (id TEXT) ON COMMIT DROP;",
                "INSERT INTO snapshotprune (id) " + due + ";",
                "DELETE FROM messagereportsnapshotitems WHERE " + Scope() + " AND snapshotid IN (SELECT id FROM snapshotprune);",
                "DELETE FROM messagereportsnapshots WHERE " + Scope() + " AND id IN (SELECT id FROM snapshotprune);",
                "SELECT COUNT(*) AS cnt FROM snapshotprune;"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return (int)RowReader.GetLong(table.Rows[0], "cnt");
        }

        public async Task<List<Message>> ListRetentionDueAsync(DateTime createdBeforeUtc, int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT * FROM messages WHERE " + Scope() + " AND createdutc < " + D.Ts(createdBeforeUtc) + " AND (body <> '' OR originalbody IS NOT NULL OR attachmentcount > 0) ORDER BY createdutc, id LIMIT " +
                D.Num(Math.Clamp(limit, 1, 1000)) + ";", token).ConfigureAwait(false);
            List<Message> messages = table.Rows.Cast<DataRow>().Select(MessagingRows.Message).ToList();
            await LoadMediaAsync(messages, token).ConfigureAwait(false);
            return messages;
        }

        public async Task<int> PurgeContentAsync(IEnumerable<string> ids, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            List<string> list = ids.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (list.Count == 0) return 0;
            string now = D.Ts(nowUtc);
            List<string> statements = new List<string>
            {
                "DELETE FROM messageattachments WHERE " + Scope() + " AND messageid IN " + D.InList(list) + ";",
                "WITH purged AS (UPDATE messages SET body = '', originalbody = NULL, excerpt = '', attachmentcount = 0, hasvideo = " + D.Bool(false) + ", state = CASE WHEN state = " + D.Enum(MessageStateEnum.Removed) +
                    " THEN state ELSE " + D.Enum(MessageStateEnum.Deleted) + " END, deletedutc = COALESCE(deletedutc, " + now + "), updatedutc = " + now + " WHERE " + Scope() + " AND id IN " + D.InList(list) +
                    " RETURNING id) SELECT COUNT(*) AS cnt FROM purged;"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return (int)RowReader.GetLong(table.Rows[0], "cnt");
        }

        public async Task<List<string>> WithAttachmentsAsync(string? conversationId, string? authorUserId, string? afterId, int limit, CancellationToken token = default)
        {
            List<string> conditions = new List<string> { Scope("m"), "m.attachmentcount > 0" };
            if (!String.IsNullOrEmpty(conversationId)) conditions.Add("m.conversationid = " + D.Str(conversationId));
            if (!String.IsNullOrEmpty(authorUserId)) conditions.Add("m.authoruserid = " + D.Str(authorUserId));
            if (!String.IsNullOrEmpty(afterId)) conditions.Add("m.id > " + D.Str(afterId));
            DataTable table = await QueryAsync("SELECT m.id FROM messages m" + Where(conditions) + " ORDER BY m.id LIMIT " + D.Num(Math.Clamp(limit, 1, 1000)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "id")).ToList();
        }

        public async Task<List<MessageExportRow>> ExportPageAsync(string userId, string? afterId, int limit, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            string me = D.Str(userId);
            DataTable table = await QueryAsync("SELECT m.* FROM messages m JOIN conversationmembers cm ON " + Scope("cm") + " AND cm.conversationid = m.conversationid AND cm.userid = " + me + " WHERE " + Scope("m") +
                " AND m.seq > cm.hiddenbeforeseq" + (afterId == null ? String.Empty : " AND m.id > " + D.Str(afterId)) + " ORDER BY m.id LIMIT " + D.Num(Math.Clamp(limit, 1, 1000)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MessagingRows.Message).Select(m => new MessageExportRow
            {
                Id = m.Id,
                ConversationId = m.ConversationId,
                Seq = m.Seq,
                AuthorUserId = m.AuthorUserId,
                IsMine = String.Equals(m.AuthorUserId, userId, StringComparison.Ordinal),
                Body = m.State == MessageStateEnum.Visible ? m.Body : String.Empty,
                OriginalBody = m.State == MessageStateEnum.Visible ? m.OriginalBody : null,
                State = m.State,
                AttachmentCount = m.State == MessageStateEnum.Visible ? m.AttachmentCount : 0,
                CreatedUtc = m.CreatedUtc,
                EditedUtc = m.EditedUtc
            }).ToList();
        }

        private string Query(string text)
        {
            return "plainto_tsquery('simple', " + D.Str(text) + ")";
        }

        private string Matches(string text)
        {
            return "m.searchvector @@ " + Query(text);
        }

        private async Task<Message?> OneAsync(DataTable table, CancellationToken token)
        {
            if (table.Rows.Count == 0) return null;
            List<Message> messages = new List<Message> { MessagingRows.Message(table.Rows[0]) };
            await LoadMediaAsync(messages, token).ConfigureAwait(false);
            return messages[0];
        }

        private async Task LoadMediaAsync(List<Message> messages, CancellationToken token)
        {
            List<string> ids = messages.Where(m => m.AttachmentCount > 0).Select(m => m.Id).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return;
            DataTable table = await QueryAsync("SELECT messageid, mediaid FROM messageattachments WHERE " + Scope() + " AND messageid IN " + D.InList(ids) + " ORDER BY messageid, position;", token).ConfigureAwait(false);
            Dictionary<string, List<string>> byMessage = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (DataRow row in table.Rows)
            {
                string messageId = RowReader.GetString(row, "messageid");
                if (!byMessage.TryGetValue(messageId, out List<string>? list))
                {
                    list = new List<string>();
                    byMessage[messageId] = list;
                }
                list.Add(RowReader.GetString(row, "mediaid"));
            }
            foreach (Message message in messages)
                if (byMessage.TryGetValue(message.Id, out List<string>? media)) message.MediaIds = media;
        }
    }
}
