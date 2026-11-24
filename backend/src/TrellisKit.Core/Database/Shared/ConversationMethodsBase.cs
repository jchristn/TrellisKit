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

    internal abstract class ConversationMethodsBase : MethodsBase, IConversationMethods
    {
        protected ConversationMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<Conversation?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            DataTable table = await QueryAsync("SELECT * FROM conversations WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : MessagingRows.Conversation(table.Rows[0]);
        }

        public async Task<Conversation?> ReadByDirectKeyAsync(string directKey, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(directKey)) return null;
            DataTable table = await QueryAsync("SELECT * FROM conversations WHERE " + Scope() + " AND directkey = " + D.Str(directKey) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : MessagingRows.Conversation(table.Rows[0]);
        }

        public async Task<bool> CreateDirectAsync(Conversation conversation, List<ConversationMember> members, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(conversation);
            ArgumentNullException.ThrowIfNull(members);
            RequireText(conversation.Id, nameof(conversation.Id));
            string id = D.Str(conversation.Id);
            string created = "EXISTS (SELECT 1 FROM conversations k WHERE " + Scope("k") + " AND k.id = " + id + ")";
            List<string> statements = new List<string>
            {
                "INSERT INTO conversations (propertyid, id, kind, directkey, lastseq, lastmessageid, lastmessageutc, createdbyuserid, purgeafterutc, createdutc, updatedutc) VALUES (" + Pid + ", " + id + ", " +
                    D.Enum(conversation.Kind) + ", " + D.Str(conversation.DirectKey) + ", 0, NULL, NULL, " + D.Str(conversation.CreatedByUserId) + ", NULL, " + D.Ts(conversation.CreatedUtc) + ", " +
                    D.Ts(conversation.UpdatedUtc) + ") ON CONFLICT DO NOTHING;"
            };
            foreach (ConversationMember m in members)
            {
                statements.Add("INSERT INTO conversationmembers (propertyid, id, conversationid, userid, role, folder, requeststate, requestmessagecount, lastreadseq, lastdeliveredseq, hiddenbeforeseq, ismuted, " +
                    "lastactivityutc, joinedutc, updatedutc) SELECT " + Pid + ", " + D.Str(m.Id) + ", " + id + ", " + D.Str(m.UserId) + ", " + D.Enum(m.Role) + ", " + D.Enum(m.Folder) + ", " +
                    D.Enum(m.RequestState) + ", 0, 0, 0, 0, " + D.Bool(false) + ", " + D.Ts(m.LastActivityUtc) + ", " + D.Ts(m.JoinedUtc) + ", " + D.Ts(m.UpdatedUtc) + " WHERE " + created + ";");
            }
            statements.Add("SELECT COUNT(*) AS cnt FROM conversations WHERE " + Scope() + " AND id = " + id + ";");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return RowReader.GetLong(table.Rows[0], "cnt") > 0;
        }

        public async Task<ConversationEntry?> ReadEntryAsync(string conversationId, string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(conversationId) || String.IsNullOrEmpty(userId)) return null;
            Dictionary<string, ConversationEntry> entries = await ReadEntriesAsync(userId, new[] { conversationId }, token).ConfigureAwait(false);
            return entries.TryGetValue(conversationId, out ConversationEntry? entry) ? entry : null;
        }

        public async Task<Dictionary<string, ConversationEntry>> ReadEntriesAsync(string userId, IEnumerable<string> conversationIds, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            ArgumentNullException.ThrowIfNull(conversationIds);
            Dictionary<string, ConversationEntry> result = new Dictionary<string, ConversationEntry>(StringComparer.Ordinal);
            List<string> ids = conversationIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return result;
            DataTable conversations = await QueryAsync("SELECT * FROM conversations WHERE " + Scope() + " AND id IN " + D.InList(ids) + ";", token).ConfigureAwait(false);
            if (conversations.Rows.Count == 0) return result;
            DataTable members = await QueryAsync("SELECT * FROM conversationmembers WHERE " + Scope() + " AND conversationid IN " + D.InList(ids) + " ORDER BY conversationid, joinedutc, id;", token).ConfigureAwait(false);
            Dictionary<string, List<ConversationMember>> byConversation = members.Rows.Cast<DataRow>().Select(MessagingRows.Member)
                .GroupBy(m => m.ConversationId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            foreach (DataRow row in conversations.Rows)
            {
                Conversation conversation = MessagingRows.Conversation(row);
                if (!byConversation.TryGetValue(conversation.Id, out List<ConversationMember>? list)) continue;
                ConversationMember? mine = list.FirstOrDefault(m => String.Equals(m.UserId, userId, StringComparison.Ordinal));
                if (mine == null) continue;
                result[conversation.Id] = new ConversationEntry { Conversation = conversation, Mine = mine, Others = list.Where(m => !ReferenceEquals(m, mine)).ToList() };
            }
            return result;
        }

        public async Task<List<ConversationMember>> ReadMembersAsync(string conversationId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(conversationId)) return new List<ConversationMember>();
            DataTable table = await QueryAsync("SELECT * FROM conversationmembers WHERE " + Scope() + " AND conversationid = " + D.Str(conversationId) + " ORDER BY joinedutc, id LIMIT 100;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MessagingRows.Member).ToList();
        }

        public Task<EnumerationResult<ConversationMember>> EnumerateAsync(string userId, EnumerationRequest request, ConversationListFilter filter, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            string me = D.Str(userId);
            string from = "conversationmembers cm JOIN conversations c ON " + Scope("c") + " AND c.id = cm.conversationid LEFT JOIN conversationmembers om ON " + Scope("om") +
                " AND om.conversationid = cm.conversationid AND om.userid <> cm.userid LEFT JOIN users ou ON " + Scope("ou") + " AND ou.id = om.userid";
            List<string> conditions = new List<string> { "cm.userid = " + me };
            if (filter.Changes)
            {
                // Sync: every row of the caller (Removed changes included).
            }
            else if (filter.Declined)
            {
                conditions.Add("cm.leftutc IS NULL AND cm.deletedutc IS NULL AND cm.requeststate = " + D.Enum(ConversationRequestStateEnum.Declined));
            }
            else
            {
                conditions.Add(VisibleCondition(me));
                if (filter.Folder == ConversationFolderEnum.Requests) conditions.Add("cm.requeststate = " + D.Enum(ConversationRequestStateEnum.Pending));
                else if (filter.Folder != null) conditions.Add("cm.folder = " + D.Enum(filter.Folder.Value) + " AND (cm.requeststate IS NULL OR cm.requeststate <> " + D.Enum(ConversationRequestStateEnum.Pending) + ")");
                if (filter.UnreadOnly) conditions.Add("EXISTS (SELECT 1 FROM messages um WHERE " + Scope("um") + " AND um.conversationid = cm.conversationid AND um.seq > cm.lastreadseq AND um.seq > cm.hiddenbeforeseq AND um.state = " +
                    D.Enum(MessageStateEnum.Visible) + " AND (um.authoruserid IS NULL OR um.authoruserid <> cm.userid))");
            }
            if (!String.IsNullOrEmpty(filter.WithUserId)) conditions.Add("om.userid = " + D.Str(filter.WithUserId));
            return EnumerateAsync(request, "cm.*", from, conditions, MessagingRows.Member, token);
        }

        public async Task<Dictionary<string, int>> UnreadCountsAsync(string userId, IEnumerable<string> conversationIds, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            ArgumentNullException.ThrowIfNull(conversationIds);
            Dictionary<string, int> result = new Dictionary<string, int>(StringComparer.Ordinal);
            List<string> ids = conversationIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return result;
            string me = D.Str(userId);
            DataTable table = await QueryAsync("SELECT m.conversationid, LEAST(COUNT(*), 999) AS cnt FROM messages m JOIN conversationmembers cm ON " + Scope("cm") + " AND cm.conversationid = m.conversationid AND cm.userid = " + me +
                " WHERE " + Scope("m") + " AND m.conversationid IN " + D.InList(ids) + " AND m.seq > cm.lastreadseq AND m.seq > cm.hiddenbeforeseq AND m.state = " + D.Enum(MessageStateEnum.Visible) +
                " AND (m.authoruserid IS NULL OR m.authoruserid <> " + me + ") GROUP BY m.conversationid;", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows) result[RowReader.GetString(row, "conversationid")] = (int)Math.Min(999, RowReader.GetLong(row, "cnt"));
            return result;
        }

        public async Task<MessagingUnreadCounts> UnreadSummaryAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            string me = D.Str(userId);
            string from = " FROM conversationmembers cm JOIN conversations c ON " + Scope("c") + " AND c.id = cm.conversationid LEFT JOIN conversationmembers om ON " + Scope("om") +
                " AND om.conversationid = cm.conversationid AND om.userid <> cm.userid";
            string where = " WHERE " + Scope("cm") + " AND cm.userid = " + me + " AND " + VisibleCondition(me);
            DataTable unread = await QueryAsync("SELECT COUNT(DISTINCT m.conversationid) AS conversations, COUNT(m.id) AS messages" + from + " JOIN messages m ON " + Scope("m") +
                " AND m.conversationid = cm.conversationid AND m.seq > cm.lastreadseq AND m.seq > cm.hiddenbeforeseq AND m.state = " + D.Enum(MessageStateEnum.Visible) +
                " AND (m.authoruserid IS NULL OR m.authoruserid <> " + me + ")" + where + " AND (cm.requeststate IS NULL OR cm.requeststate <> " + D.Enum(ConversationRequestStateEnum.Pending) + ");", token).ConfigureAwait(false);
            long requests = await CountAsync("SELECT COUNT(*) AS cnt" + from + where + " AND cm.requeststate = " + D.Enum(ConversationRequestStateEnum.Pending) + ";", token).ConfigureAwait(false);
            DataRow row = unread.Rows[0];
            return new MessagingUnreadCounts { Conversations = RowReader.GetLong(row, "conversations"), Messages = RowReader.GetLong(row, "messages"), Requests = requests };
        }

        public async Task<long?> MarkReadAsync(string conversationId, string userId, long seq, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(conversationId, nameof(conversationId));
            RequireText(userId, nameof(userId));
            string conversation = D.Str(conversationId);
            string me = D.Str(userId);
            string target = "LEAST(GREATEST(" + D.Num(seq) + ", 0), c.lastseq)";
            DataTable table = await QueryAsync("WITH moved AS (UPDATE conversationmembers cm SET lastreadseq = " + target + ", lastdeliveredseq = GREATEST(cm.lastdeliveredseq, " + target + "), updatedutc = " + D.Ts(nowUtc) +
                " FROM conversations c WHERE " + Scope("cm") + " AND " + Scope("c") + " AND c.id = cm.conversationid AND cm.conversationid = " + conversation + " AND cm.userid = " + me + " AND " + target + " > cm.lastreadseq RETURNING cm.lastreadseq), " +
                "touched AS (UPDATE conversationmembers o SET updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope("o") + " AND o.conversationid = " + conversation + " AND o.userid <> " + me +
                " AND EXISTS (SELECT 1 FROM moved) RETURNING o.id) SELECT (SELECT lastreadseq FROM moved) AS moved, (SELECT r.lastreadseq FROM conversationmembers r WHERE " + Scope("r") +
                " AND r.conversationid = " + conversation + " AND r.userid = " + me + ") AS current;", token).ConfigureAwait(false);
            DataRow row = table.Rows[0];
            long? moved = RowReader.GetNullableLong(row, "moved");
            return moved ?? RowReader.GetNullableLong(row, "current");
        }

        public async Task<long?> MarkDeliveredAsync(string conversationId, string userId, long seq, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(conversationId, nameof(conversationId));
            RequireText(userId, nameof(userId));
            string target = "LEAST(GREATEST(" + D.Num(seq) + ", 0), c.lastseq)";
            DataTable table = await QueryAsync("UPDATE conversationmembers cm SET lastdeliveredseq = " + target + " FROM conversations c WHERE " + Scope("cm") + " AND " + Scope("c") +
                " AND c.id = cm.conversationid AND cm.conversationid = " + D.Str(conversationId) + " AND cm.userid = " + D.Str(userId) + " AND " + target + " > cm.lastdeliveredseq RETURNING cm.lastdeliveredseq;", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : RowReader.GetLong(table.Rows[0], "lastdeliveredseq");
        }

        public async Task<bool> SetFolderAsync(string memberId, ConversationFolderEnum folder, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(memberId, nameof(memberId));
            DataTable table = await QueryAsync("UPDATE conversationmembers SET folder = " + D.Enum(folder) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(memberId) +
                " AND (requeststate IS NULL OR requeststate = " + D.Enum(ConversationRequestStateEnum.AwaitingAcceptance) + ") RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> SetMuteAsync(string memberId, bool muted, DateTime? untilUtc, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(memberId, nameof(memberId));
            DataTable table = await QueryAsync("UPDATE conversationmembers SET ismuted = " + D.Bool(muted) + ", muteduntilutc = " + D.Ts(muted ? untilUtc : null) + ", updatedutc = " + D.Ts(nowUtc) +
                " WHERE " + Scope() + " AND id = " + D.Str(memberId) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> DeleteForMemberAsync(string conversationId, string userId, DateTime nowUtc, DateTime purgeAfterUtc, CancellationToken token = default)
        {
            RequireText(conversationId, nameof(conversationId));
            RequireText(userId, nameof(userId));
            string conversation = D.Str(conversationId);
            List<string> statements = new List<string>
            {
                "UPDATE conversationmembers cm SET hiddenbeforeseq = c.lastseq, lastreadseq = GREATEST(cm.lastreadseq, c.lastseq), lastdeliveredseq = GREATEST(cm.lastdeliveredseq, c.lastseq), deletedutc = " + D.Ts(nowUtc) +
                    ", updatedutc = " + D.Ts(nowUtc) + " FROM conversations c WHERE " + Scope("cm") + " AND " + Scope("c") + " AND c.id = cm.conversationid AND cm.conversationid = " + conversation + " AND cm.userid = " + D.Str(userId) + ";",
                SchedulePurge("c.id = " + conversation, purgeAfterUtc),
                "SELECT COUNT(*) AS cnt FROM conversationmembers WHERE " + Scope() + " AND conversationid = " + conversation + " AND userid = " + D.Str(userId) + ";"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return RowReader.GetLong(table.Rows[0], "cnt") > 0;
        }

        public async Task<bool> RestoreForMemberAsync(string conversationId, string userId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(conversationId, nameof(conversationId));
            RequireText(userId, nameof(userId));
            string conversation = D.Str(conversationId);
            DataTable table = await QueryAsync("WITH restored AS (UPDATE conversationmembers SET deletedutc = NULL, updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND conversationid = " + conversation +
                " AND userid = " + D.Str(userId) + " AND deletedutc IS NOT NULL RETURNING id), kept AS (UPDATE conversations SET purgeafterutc = NULL WHERE " + Scope() + " AND id = " + conversation +
                " AND EXISTS (SELECT 1 FROM restored) RETURNING id) SELECT COUNT(*) AS cnt FROM restored;", token).ConfigureAwait(false);
            return RowReader.GetLong(table.Rows[0], "cnt") > 0;
        }

        public async Task<bool> AcceptAsync(string conversationId, string recipientUserId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(conversationId, nameof(conversationId));
            RequireText(recipientUserId, nameof(recipientUserId));
            string conversation = D.Str(conversationId);
            string me = D.Str(recipientUserId);
            string now = D.Ts(nowUtc);
            DataTable table = await QueryAsync("WITH acc AS (UPDATE conversationmembers SET requeststate = NULL, folder = " + D.Enum(ConversationFolderEnum.Inbox) + ", acceptedutc = " + now + ", declinedutc = NULL, updatedutc = " + now +
                " WHERE " + Scope() + " AND conversationid = " + conversation + " AND userid = " + me + " AND requeststate IN " + D.InList(new[] { ConversationRequestStateEnum.Pending.ToString(), ConversationRequestStateEnum.Declined.ToString() }) +
                " RETURNING id), snd AS (UPDATE conversationmembers SET requeststate = NULL, updatedutc = " + now + " WHERE " + Scope() + " AND conversationid = " + conversation + " AND userid <> " + me +
                " AND requeststate = " + D.Enum(ConversationRequestStateEnum.AwaitingAcceptance) + " AND EXISTS (SELECT 1 FROM acc) RETURNING id), con AS (INSERT INTO messagingcontacts (propertyid, userid, contactuserid, source, createdutc) SELECT " +
                Pid + ", " + me + ", o.userid, " + D.Enum(MessagingContactSourceEnum.Accepted) + ", " + now + " FROM conversationmembers o WHERE " + Scope("o") + " AND o.conversationid = " + conversation + " AND o.userid <> " + me +
                " AND EXISTS (SELECT 1 FROM acc) ON CONFLICT DO NOTHING RETURNING userid) SELECT COUNT(*) AS cnt FROM acc;", token).ConfigureAwait(false);
            return RowReader.GetLong(table.Rows[0], "cnt") > 0;
        }

        public async Task<bool> DeclineAsync(string conversationId, string recipientUserId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(conversationId, nameof(conversationId));
            RequireText(recipientUserId, nameof(recipientUserId));
            DataTable table = await QueryAsync("UPDATE conversationmembers SET requeststate = " + D.Enum(ConversationRequestStateEnum.Declined) + ", declinedutc = " + D.Ts(nowUtc) + ", updatedutc = " + D.Ts(nowUtc) +
                " WHERE " + Scope() + " AND conversationid = " + D.Str(conversationId) + " AND userid = " + D.Str(recipientUserId) + " AND requeststate = " + D.Enum(ConversationRequestStateEnum.Pending) + " RETURNING id;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public Task<long> CountPendingSentAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            return CountAsync("SELECT COUNT(*) AS cnt FROM conversationmembers WHERE " + Scope() + " AND userid = " + D.Str(userId) + " AND requeststate = " + D.Enum(ConversationRequestStateEnum.AwaitingAcceptance) +
                " AND deletedutc IS NULL AND leftutc IS NULL;", token);
        }

        public async Task<HashSet<string>> ContactsAmongAsync(string userId, IEnumerable<string> candidateUserIds, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            ArgumentNullException.ThrowIfNull(candidateUserIds);
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            List<string> ids = candidateUserIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT contactuserid FROM messagingcontacts WHERE " + Scope() + " AND userid = " + D.Str(userId) + " AND contactuserid IN " + D.InList(ids) + ";", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows) result.Add(RowReader.GetString(row, "contactuserid"));
            return result;
        }

        public async Task<HashSet<string>> KnownByAmongAsync(string userId, IEnumerable<string> candidateUserIds, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            ArgumentNullException.ThrowIfNull(candidateUserIds);
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            List<string> ids = candidateUserIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT userid FROM messagingcontacts WHERE " + Scope() + " AND contactuserid = " + D.Str(userId) + " AND userid IN " + D.InList(ids) + ";", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows) result.Add(RowReader.GetString(row, "userid"));
            return result;
        }

        public async Task<Dictionary<string, MemberMessagingSettings>> ReadSettingsAsync(IEnumerable<string> userIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(userIds);
            Dictionary<string, MemberMessagingSettings> result = new Dictionary<string, MemberMessagingSettings>(StringComparer.Ordinal);
            List<string> ids = userIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT * FROM messagingsettings WHERE " + Scope() + " AND userid IN " + D.InList(ids) + ";", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows)
            {
                MemberMessagingSettings settings = MessagingRows.Settings(row);
                result[settings.UserId] = settings;
            }
            return result;
        }

        public async Task WriteSettingsAsync(MemberMessagingSettings settings, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(settings);
            RequireText(settings.UserId, nameof(settings.UserId));
            DateTime updated = settings.UpdatedUtc ?? DateTime.UtcNow;
            await QueryAsync("INSERT INTO messagingsettings (propertyid, userid, dmpolicy, readreceipts, typingindicators, pushpreview, ignorenotificationhours, updatedutc) VALUES (" + Pid + ", " + D.Str(settings.UserId) + ", " +
                D.Enum(settings.DmPolicy) + ", " + D.Bool(settings.ReadReceipts) + ", " + D.Bool(settings.TypingIndicators) + ", " + D.Enum(settings.PushPreview) + ", " + D.Bool(settings.IgnoreNotificationHours) + ", " + D.Ts(updated) +
                ") ON CONFLICT (propertyid, userid) DO UPDATE SET dmpolicy = EXCLUDED.dmpolicy, readreceipts = EXCLUDED.readreceipts, typingindicators = EXCLUDED.typingindicators, pushpreview = EXCLUDED.pushpreview, " +
                "ignorenotificationhours = EXCLUDED.ignorenotificationhours, updatedutc = EXCLUDED.updatedutc;", token).ConfigureAwait(false);
        }

        public async Task<int> TouchSharedAsync(string userId, string otherUserId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            RequireText(otherUserId, nameof(otherUserId));
            DataTable table = await QueryAsync("UPDATE conversationmembers cm SET updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope("cm") + " AND cm.userid = " + D.Str(userId) +
                " AND EXISTS (SELECT 1 FROM conversationmembers o WHERE " + Scope("o") + " AND o.conversationid = cm.conversationid AND o.userid = " + D.Str(otherUserId) + ") RETURNING cm.id;", token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        public async Task<bool> TryClaimPushAsync(string conversationId, string userId, long seq, DateTime nowUtc, int windowSeconds, CancellationToken token = default)
        {
            RequireText(conversationId, nameof(conversationId));
            RequireText(userId, nameof(userId));
            DateTime cutoff = nowUtc.AddSeconds(-Math.Max(0, windowSeconds));
            DataTable table = await QueryAsync("INSERT INTO messagepushstate (propertyid, conversationid, userid, lastpushutc, lastpushseq) VALUES (" + Pid + ", " + D.Str(conversationId) + ", " + D.Str(userId) + ", " +
                D.Ts(nowUtc) + ", " + D.Num(seq) + ") ON CONFLICT (propertyid, conversationid, userid) DO UPDATE SET lastpushutc = EXCLUDED.lastpushutc, lastpushseq = EXCLUDED.lastpushseq WHERE messagepushstate.lastpushutc <= " +
                D.Ts(cutoff) + " OR EXISTS (SELECT 1 FROM conversationmembers r WHERE " + Scope("r") + " AND r.conversationid = messagepushstate.conversationid AND r.userid = messagepushstate.userid AND r.lastreadseq >= messagepushstate.lastpushseq) RETURNING lastpushseq;",
                token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<List<MessageEmailCandidate>> ListEmailCandidatesAsync(DateTime unreadBeforeUtc, DateTime sentBeforeUtc, string? afterUserId, int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT cm.userid, COUNT(DISTINCT cm.conversationid) AS conversations, COUNT(m.id) AS messages, MAX(es.lastsentutc) AS lastsent" + UnreadFrom(unreadBeforeUtc) +
                " AND (es.lastsentutc IS NULL OR es.lastsentutc <= " + D.Ts(sentBeforeUtc) + ")" + (afterUserId == null ? String.Empty : " AND cm.userid > " + D.Str(afterUserId)) +
                " GROUP BY cm.userid ORDER BY cm.userid LIMIT " + D.Num(Math.Clamp(limit, 1, 5000)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => new MessageEmailCandidate
            {
                UserId = RowReader.GetString(r, "userid"),
                Conversations = RowReader.GetLong(r, "conversations"),
                Messages = RowReader.GetLong(r, "messages"),
                LastSentUtc = RowReader.GetNullableDateTime(r, "lastsent")
            }).ToList();
        }

        public async Task<List<string>> UnreadSendersAsync(string userId, DateTime unreadBeforeUtc, int limit, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("SELECT m.authoruserid, MAX(m.createdutc) AS latest" + UnreadFrom(unreadBeforeUtc) + " AND cm.userid = " + D.Str(userId) +
                " GROUP BY m.authoruserid ORDER BY latest DESC, m.authoruserid LIMIT " + D.Num(Math.Clamp(limit, 1, 100)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "authoruserid")).ToList();
        }

        public Task<long> CountUnreadSendersAsync(string userId, DateTime unreadBeforeUtc, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            return CountAsync("SELECT COUNT(DISTINCT m.authoruserid) AS cnt" + UnreadFrom(unreadBeforeUtc) + " AND cm.userid = " + D.Str(userId) + ";", token);
        }

        public async Task MarkEmailSentAsync(string userId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            await QueryAsync("INSERT INTO messageemailstate (propertyid, userid, lastsentutc, lastcheckedutc) VALUES (" + Pid + ", " + D.Str(userId) + ", " + D.Ts(nowUtc) + ", " + D.Ts(nowUtc) +
                ") ON CONFLICT (propertyid, userid) DO UPDATE SET lastsentutc = EXCLUDED.lastsentutc, lastcheckedutc = EXCLUDED.lastcheckedutc;", token).ConfigureAwait(false);
        }

        public async Task<List<string>> MemberUserIdsAsync(string? afterUserId, int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT DISTINCT userid FROM conversationmembers WHERE " + Scope() + " AND leftutc IS NULL AND deletedutc IS NULL" +
                (afterUserId == null ? String.Empty : " AND userid > " + D.Str(afterUserId)) + " ORDER BY userid LIMIT " + D.Num(Math.Clamp(limit, 1, 5000)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "userid")).ToList();
        }

        public async Task<bool> RecordLapseAsync(string userId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("INSERT INTO messaginglapses (propertyid, userid, lapsedutc, purgedutc) VALUES (" + Pid + ", " + D.Str(userId) + ", " + D.Ts(nowUtc) + ", NULL) ON CONFLICT DO NOTHING RETURNING userid;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> ClearLapseAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("DELETE FROM messaginglapses WHERE " + Scope() + " AND userid = " + D.Str(userId) + " RETURNING userid;", token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<MessagingLapse?> ReadLapseAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            DataTable table = await QueryAsync("SELECT * FROM messaginglapses WHERE " + Scope() + " AND userid = " + D.Str(userId) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : Lapse(table.Rows[0]);
        }

        public async Task<List<MessagingLapse>> ListLapsesDueAsync(DateTime lapsedBeforeUtc, int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT * FROM messaginglapses WHERE " + Scope() + " AND purgedutc IS NULL AND lapsedutc <= " + D.Ts(lapsedBeforeUtc) + " ORDER BY lapsedutc, userid LIMIT " +
                D.Num(Math.Clamp(limit, 1, 1000)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(Lapse).ToList();
        }

        public async Task<int> PurgeForLapsedAsync(string userId, DateTime nowUtc, DateTime purgeAfterUtc, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            string me = D.Str(userId);
            string now = D.Ts(nowUtc);
            List<string> statements = new List<string>
            {
                "CREATE TEMP TABLE IF NOT EXISTS lapsepurge (cnt BIGINT) ON COMMIT DROP;",
                "WITH removed AS (UPDATE conversationmembers cm SET hiddenbeforeseq = c.lastseq, lastreadseq = GREATEST(cm.lastreadseq, c.lastseq), lastdeliveredseq = GREATEST(cm.lastdeliveredseq, c.lastseq), deletedutc = " + now +
                    ", updatedutc = " + now + " FROM conversations c WHERE " + Scope("cm") + " AND " + Scope("c") + " AND c.id = cm.conversationid AND cm.userid = " + me + " AND cm.deletedutc IS NULL RETURNING cm.id) " +
                    "INSERT INTO lapsepurge (cnt) SELECT COUNT(*) FROM removed;",
                SchedulePurge("EXISTS (SELECT 1 FROM conversationmembers mine WHERE " + Scope("mine") + " AND mine.conversationid = c.id AND mine.userid = " + me + ")", purgeAfterUtc),
                "DELETE FROM messagingcontacts WHERE " + Scope() + " AND userid = " + me + ";",
                "UPDATE messaginglapses SET purgedutc = " + now + " WHERE " + Scope() + " AND userid = " + me + ";",
                "SELECT cnt FROM lapsepurge;"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? 0 : (int)RowReader.GetLong(table.Rows[0], "cnt");
        }

        public async Task<List<string>> ListPurgeDueAsync(DateTime nowUtc, int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT id FROM conversations WHERE " + Scope() + " AND purgeafterutc IS NOT NULL AND purgeafterutc <= " + D.Ts(nowUtc) + " ORDER BY purgeafterutc, id LIMIT " +
                D.Num(Math.Clamp(limit, 1, 1000)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "id")).ToList();
        }

        public async Task<bool> PurgeConversationAsync(string conversationId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(conversationId, nameof(conversationId));
            string conversation = D.Str(conversationId);
            string due = "EXISTS (SELECT 1 FROM conversations g WHERE " + Scope("g") + " AND g.id = " + conversation + " AND g.purgeafterutc IS NOT NULL AND g.purgeafterutc <= " + D.Ts(nowUtc) +
                " AND NOT EXISTS (SELECT 1 FROM conversationmembers k WHERE " + Scope("k") + " AND k.conversationid = g.id AND k.deletedutc IS NULL AND k.leftutc IS NULL))";
            string messageIds = "SELECT mi.id FROM messages mi WHERE " + Scope("mi") + " AND mi.conversationid = " + conversation;
            string memberIds = "SELECT mm.id FROM conversationmembers mm WHERE " + Scope("mm") + " AND mm.conversationid = " + conversation;
            List<string> statements = new List<string> { "CREATE TEMP TABLE IF NOT EXISTS conversationpurge (due BOOLEAN) ON COMMIT DROP;", "INSERT INTO conversationpurge (due) SELECT " + due + ";" };
            string gate = " AND EXISTS (SELECT 1 FROM conversationpurge WHERE due)";
            foreach (string table in new[] { "labels", "tags" })
            {
                statements.Add("DELETE FROM " + table + " WHERE " + Scope() + " AND targettype = " + D.Enum(TargetTypeEnum.Message) + " AND targetid IN (" + messageIds + ")" + gate + ";");
                statements.Add("DELETE FROM " + table + " WHERE " + Scope() + " AND targettype = " + D.Enum(TargetTypeEnum.ConversationMember) + " AND targetid IN (" + memberIds + ")" + gate + ";");
            }
            statements.Add("DELETE FROM reactions WHERE " + Scope() + " AND targettype = " + D.Enum(TargetTypeEnum.Message) + " AND targetid IN (" + messageIds + ")" + gate + ";");
            statements.Add("DELETE FROM reactioncounts WHERE " + Scope() + " AND targettype = " + D.Enum(TargetTypeEnum.Message) + " AND targetid IN (" + messageIds + ")" + gate + ";");
            statements.Add("DELETE FROM messageattachments WHERE " + Scope() + " AND messageid IN (" + messageIds + ")" + gate + ";");
            statements.Add("DELETE FROM messages WHERE " + Scope() + " AND conversationid = " + conversation + gate + ";");
            statements.Add("DELETE FROM messagepushstate WHERE " + Scope() + " AND conversationid = " + conversation + gate + ";");
            statements.Add("DELETE FROM conversationmembers WHERE " + Scope() + " AND conversationid = " + conversation + gate + ";");
            statements.Add("DELETE FROM conversations WHERE " + Scope() + " AND id = " + conversation + gate + ";");
            statements.Add("SELECT due FROM conversationpurge;");
            DataTable result = await TransactionAsync(statements, token).ConfigureAwait(false);
            return result.Rows.Count > 0 && RowReader.GetBool(result.Rows[0], "due");
        }

        public async Task<int> EraseForAccountAsync(string userId, DateTime nowUtc, DateTime purgeAfterUtc, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            string me = D.Str(userId);
            string now = D.Ts(nowUtc);
            string myMessages = "SELECT mi.id FROM messages mi WHERE " + Scope("mi") + " AND mi.authoruserid = " + me;
            string myRows = "SELECT mm.id FROM conversationmembers mm WHERE " + Scope("mm") + " AND mm.userid = " + me;
            List<string> statements = new List<string>();
            foreach (string table in new[] { "labels", "tags" })
            {
                statements.Add("DELETE FROM " + table + " WHERE " + Scope() + " AND targettype = " + D.Enum(TargetTypeEnum.Message) + " AND targetid IN (" + myMessages + ");");
                statements.Add("DELETE FROM " + table + " WHERE " + Scope() + " AND targettype = " + D.Enum(TargetTypeEnum.ConversationMember) + " AND targetid IN (" + myRows + ");");
            }
            statements.Add("DELETE FROM messageattachments WHERE " + Scope() + " AND messageid IN (" + myMessages + ");");
            statements.Add("UPDATE conversations c SET purgeafterutc = " + D.Ts(purgeAfterUtc) + " WHERE " + Scope("c") + " AND c.purgeafterutc IS NULL AND EXISTS (SELECT 1 FROM conversationmembers mine WHERE " + Scope("mine") +
                " AND mine.conversationid = c.id AND mine.userid = " + me + ") AND NOT EXISTS (SELECT 1 FROM conversationmembers k WHERE " + Scope("k") + " AND k.conversationid = c.id AND k.userid <> " + me +
                " AND k.deletedutc IS NULL AND k.leftutc IS NULL);");
            statements.Add("DELETE FROM conversationmembers WHERE " + Scope() + " AND userid = " + me + ";");
            statements.Add("DELETE FROM messagingsettings WHERE " + Scope() + " AND userid = " + me + ";");
            statements.Add("DELETE FROM messagingcontacts WHERE " + Scope() + " AND (userid = " + me + " OR contactuserid = " + me + ");");
            statements.Add("DELETE FROM messageemailstate WHERE " + Scope() + " AND userid = " + me + ";");
            statements.Add("DELETE FROM messagepushstate WHERE " + Scope() + " AND userid = " + me + ";");
            statements.Add("DELETE FROM messaginglapses WHERE " + Scope() + " AND userid = " + me + ";");
            statements.Add("WITH erased AS (UPDATE messages SET body = '', originalbody = NULL, excerpt = '', authoruserid = NULL, attachmentcount = 0, hasvideo = " + D.Bool(false) + ", state = " + D.Enum(MessageStateEnum.Deleted) +
                ", deletedutc = COALESCE(deletedutc, " + now + "), updatedutc = " + now + " WHERE " + Scope() + " AND authoruserid = " + me + " RETURNING id) SELECT COUNT(*) AS cnt FROM erased;");
            DataTable result = await TransactionAsync(statements, token).ConfigureAwait(false);
            return (int)RowReader.GetLong(result.Rows[0], "cnt");
        }

        private string VisibleCondition(string me)
        {
            // docs/PHASE5_CONTRACT.md sections 3.1, 3.2: not left, not deleted for the caller, not declined, the other
            // member not blocked by the caller, and a visible message (or the caller started it).
            return "cm.leftutc IS NULL AND cm.deletedutc IS NULL AND (cm.requeststate IS NULL OR cm.requeststate <> " + D.Enum(ConversationRequestStateEnum.Declined) + ")" +
                " AND NOT EXISTS (SELECT 1 FROM blocks bk WHERE " + Scope("bk") + " AND bk.blockeruserid = " + me + " AND bk.blockeduserid = om.userid)" +
                " AND (c.lastseq > cm.hiddenbeforeseq OR (c.createdbyuserid = " + me + " AND cm.hiddenbeforeseq = 0))";
        }

        private string SchedulePurge(string conversationCondition, DateTime purgeAfterUtc)
        {
            return "UPDATE conversations c SET purgeafterutc = " + D.Ts(purgeAfterUtc) + " WHERE " + Scope("c") + " AND " + conversationCondition + " AND c.purgeafterutc IS NULL AND NOT EXISTS (SELECT 1 FROM conversationmembers k WHERE " +
                Scope("k") + " AND k.conversationid = c.id AND k.deletedutc IS NULL AND k.leftutc IS NULL);";
        }

        private string UnreadFrom(DateTime unreadBeforeUtc)
        {
            // Unread messages by others older than the email delay, newer than the member's last unread email, outside
            // requests, muted conversations, and blocked members.
            return " FROM conversationmembers cm JOIN messages m ON " + Scope("m") + " AND m.conversationid = cm.conversationid AND m.seq > cm.lastreadseq AND m.seq > cm.hiddenbeforeseq AND m.state = " +
                D.Enum(MessageStateEnum.Visible) + " AND m.authoruserid IS NOT NULL AND m.authoruserid <> cm.userid AND m.createdutc <= " + D.Ts(unreadBeforeUtc) +
                " LEFT JOIN messageemailstate es ON " + Scope("es") + " AND es.userid = cm.userid WHERE " + Scope("cm") + " AND cm.deletedutc IS NULL AND cm.leftutc IS NULL AND (cm.requeststate IS NULL OR cm.requeststate = " +
                D.Enum(ConversationRequestStateEnum.AwaitingAcceptance) + ") AND (cm.ismuted = " + D.Bool(false) + " OR (cm.muteduntilutc IS NOT NULL AND cm.muteduntilutc <= " + D.Ts(unreadBeforeUtc) + "))" +
                " AND (es.lastsentutc IS NULL OR m.createdutc > es.lastsentutc) AND NOT EXISTS (SELECT 1 FROM blocks bk WHERE " + Scope("bk") + " AND bk.blockeruserid = cm.userid AND bk.blockeduserid = m.authoruserid)";
        }

        private static MessagingLapse Lapse(DataRow row)
        {
            return new MessagingLapse
            {
                UserId = RowReader.GetString(row, "userid"),
                LapsedUtc = RowReader.GetDateTime(row, "lapsedutc"),
                PurgedUtc = RowReader.GetNullableDateTime(row, "purgedutc")
            };
        }
    }
}
