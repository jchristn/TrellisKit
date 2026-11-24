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

    /// <summary>
    /// Shared conversation, message, feedback, and daily usage methods.
    /// </summary>
    internal abstract class AiConversationMethodsBase : MethodsBase, IAiConversationMethods
    {
        private const int _PreviewLength = 140;

        private const string _ConversationColumns =
            "id, assistantid, userid, visitorid, title, titlesource, status, isarchived, messagecount, lastmessageutc, lastmessagepreview, surface, createdutc, updatedutc";

        private const string _MessageColumns =
            "id, conversationid, assistantid, role, content, reasoning, status, errorcode, endpointid, endpointname, model, usedfallback, prompttokens, completiontokens, reasoningtokens, " +
            "latencyms, ttftms, ttltms, tokenspersecond, finishreason, costmicros, moderationverdict, moderationcategory, issuperseded, clientmessageid, traceid, replytomessageid, retrievalquery, " +
            "createdutc, completedutc";

        private const string _FeedbackColumns =
            "id, messageid, conversationid, assistantid, userid, visitorid, rating, note, reasons, promptcopy, replycopy, reviewed, reviewedbyuserid, reviewedutc, createdutc, updatedutc";

        protected AiConversationMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public Task CreateVisitorAsync(AssistantVisitor visitor, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(visitor);
            return QueryAsync("INSERT INTO aivisitors (propertyid, id, createdutc, lastseenutc, iphash, surface) VALUES (" + Pid + ", " + D.Str(visitor.Id) + ", " + D.Ts(visitor.CreatedUtc) + ", " +
                D.Ts(visitor.LastSeenUtc) + ", " + D.Str(visitor.IpHash) + ", " + D.Enum(visitor.Surface) + ");", token);
        }

        public async Task<AssistantVisitor?> ReadVisitorAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("SELECT id, createdutc, lastseenutc, iphash, surface FROM aivisitors WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            DataRow row = table.Rows[0];
            return new AssistantVisitor
            {
                Id = RowReader.GetString(row, "id"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                LastSeenUtc = RowReader.GetDateTime(row, "lastseenutc"),
                IpHash = RowReader.GetNullableString(row, "iphash"),
                Surface = RowReader.GetEnum(row, "surface", AiConversationSurfaceEnum.Widget)
            };
        }

        public Task TouchVisitorAsync(string id, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            return QueryAsync("UPDATE aivisitors SET lastseenutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token);
        }

        public async Task<AssistantConversation> CreateConversationAsync(AssistantConversation conversation, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(conversation);
            AssistantConversation c = conversation;
            c.PropertyId = PropertyId;
            await QueryAsync("INSERT INTO aiconversations (propertyid, " + _ConversationColumns + ") VALUES (" + Pid + ", " + D.Str(c.Id) + ", " + D.Str(c.AssistantId) + ", " + D.Str(c.UserId) + ", " +
                D.Str(c.VisitorId) + ", " + D.Str(c.Title) + ", " + D.Enum(c.TitleSource) + ", " + D.Enum(c.Status) + ", " + D.Bool(c.IsArchived) + ", " + D.Num(c.MessageCount) + ", " +
                D.Ts(c.LastMessageUtc) + ", " + D.Str(c.LastMessagePreview) + ", " + D.Enum(c.Surface) + ", " + D.Ts(c.CreatedUtc) + ", " + D.Ts(c.UpdatedUtc) + ");", token).ConfigureAwait(false);
            return conversation;
        }

        public async Task<AssistantConversation?> ReadConversationAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable table = await QueryAsync("SELECT " + _ConversationColumns + " FROM aiconversations WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : MapConversation(table.Rows[0]);
        }

        public Task UpdateConversationAsync(AssistantConversation conversation, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(conversation);
            AssistantConversation c = conversation;
            return QueryAsync("UPDATE aiconversations SET title = " + D.Str(c.Title) + ", titlesource = " + D.Enum(c.TitleSource) + ", status = " + D.Enum(c.Status) + ", isarchived = " + D.Bool(c.IsArchived) +
                ", updatedutc = " + D.Ts(c.UpdatedUtc) + " WHERE " + Scope() + " AND id = " + D.Str(c.Id) + ";", token);
        }

        public async Task<bool> SetGeneratedTitleAsync(string id, string title, AssistantTitleSourceEnum source, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable result = await QueryAsync("UPDATE aiconversations SET title = " + D.Str(title) + ", titlesource = " + D.Enum(source) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() +
                " AND id = " + D.Str(id) + " AND titlesource <> " + D.Enum(AssistantTitleSourceEnum.Member) + " RETURNING id;", token).ConfigureAwait(false);
            return result.Rows.Count > 0;
        }

        public async Task<bool> DeleteConversationAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable result = await TransactionAsync(DeleteConversationStatements(D.InList(new[] { id })), token).ConfigureAwait(false);
            return result.Rows.Count > 0;
        }

        public Task<EnumerationResult<AssistantConversation>> EnumerateConversationsAsync(EnumerationRequest request, AiEnumerationFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = new List<string>();
            if (!String.IsNullOrEmpty(filter.AssistantId)) conditions.Add("c.assistantid = " + D.Str(filter.AssistantId));
            if (!String.IsNullOrEmpty(filter.UserId)) conditions.Add("c.userid = " + D.Str(filter.UserId));
            if (!String.IsNullOrEmpty(filter.VisitorId)) conditions.Add("c.visitorid = " + D.Str(filter.VisitorId));
            if (filter.OwnerType == "Member") conditions.Add("c.userid IS NOT NULL");
            if (filter.OwnerType == "Visitor") conditions.Add("c.visitorid IS NOT NULL");
            if (filter.Status != null) conditions.Add("c.status = " + D.Enum(filter.Status.Value));
            if (filter.Archived != null) conditions.Add("c.isarchived = " + D.Bool(filter.Archived.Value));
            if (filter.Surface != null) conditions.Add("c.surface = " + D.Enum(filter.Surface.Value));
            string select = String.Join(", ", _ConversationColumns.Split(',').Select(c => "c." + c.Trim()));
            return EnumerateAsync(request, select, "aiconversations c", conditions, MapConversation, token);
        }

        public async Task AddExchangeAsync(AssistantMessage userMessage, AssistantMessage reply, int maxMessages, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(userMessage);
            ArgumentNullException.ThrowIfNull(reply);
            List<string> statements = new List<string>
            {
                InsertMessage(userMessage),
                InsertMessage(reply),
                "UPDATE aiconversations SET messagecount = messagecount + 2, lastmessageutc = " + D.Ts(userMessage.CreatedUtc) + ", lastmessagepreview = " + D.Str(Preview(userMessage.Content)) +
                ", status = CASE WHEN messagecount + 2 >= " + D.Num(maxMessages) + " THEN " + D.Enum(AssistantConversationStatusEnum.Full) + " ELSE status END, updatedutc = " + D.Ts(userMessage.CreatedUtc) +
                " WHERE " + Scope() + " AND id = " + D.Str(userMessage.ConversationId) + ";"
            };
            await UniqueTransactionAsync(statements, "A message with that client message id", token).ConfigureAwait(false);
        }

        public Task AddRegeneratedReplyAsync(string supersededId, AssistantMessage reply, CancellationToken token = default)
        {
            RequireText(supersededId, nameof(supersededId));
            ArgumentNullException.ThrowIfNull(reply);
            return TransactionAsync(new List<string>
            {
                "UPDATE aimessages SET issuperseded = " + D.Bool(true) + " WHERE " + Scope() + " AND id = " + D.Str(supersededId) + ";",
                InsertMessage(reply)
            }, token);
        }

        public async Task<AssistantMessage?> ReadMessageAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            return await ReadOneMessageAsync("id = " + D.Str(id), token).ConfigureAwait(false);
        }

        public Task<AssistantMessage?> ReadByClientMessageIdAsync(string conversationId, string clientMessageId, CancellationToken token = default)
        {
            RequireText(conversationId, nameof(conversationId));
            RequireText(clientMessageId, nameof(clientMessageId));
            return ReadOneMessageAsync("conversationid = " + D.Str(conversationId) + " AND clientmessageid = " + D.Str(clientMessageId) + " AND role = " + D.Enum(AssistantMessageRoleEnum.User), token);
        }

        public Task<AssistantMessage?> ReadReplyToAsync(string userMessageId, CancellationToken token = default)
        {
            RequireText(userMessageId, nameof(userMessageId));
            return ReadOneMessageAsync("replytomessageid = " + D.Str(userMessageId) + " AND issuperseded = " + D.Bool(false), token);
        }

        public async Task<bool> HasInFlightAsync(string conversationId, CancellationToken token = default)
        {
            RequireText(conversationId, nameof(conversationId));
            long count = await CountAsync("SELECT COUNT(*) AS cnt FROM aimessages WHERE " + Scope() + " AND conversationid = " + D.Str(conversationId) + " AND status IN ('Pending', 'Streaming');", token).ConfigureAwait(false);
            return count > 0;
        }

        public Task MarkStreamingAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            return QueryAsync("UPDATE aimessages SET status = " + D.Enum(AssistantMessageStatusEnum.Streaming) + " WHERE " + Scope() + " AND id = " + D.Str(id) + " AND status = " +
                D.Enum(AssistantMessageStatusEnum.Pending) + ";", token);
        }

        public async Task<bool> CompleteReplyAsync(AssistantMessage reply, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(reply);
            AssistantMessage m = reply;
            List<string> statements = new List<string>
            {
                "UPDATE aimessages SET content = " + D.Str(m.Content) + ", reasoning = " + D.Str(m.Reasoning) + ", status = " + D.Enum(m.Status) + ", errorcode = " + D.Str(m.ErrorCode) +
                ", endpointid = " + D.Str(m.EndpointId) + ", endpointname = " + D.Str(m.EndpointName) + ", model = " + D.Str(m.Model) + ", usedfallback = " + D.Bool(m.UsedFallback) +
                ", prompttokens = " + D.Num(m.PromptTokens) + ", completiontokens = " + D.Num(m.CompletionTokens) + ", reasoningtokens = " + D.Num(m.ReasoningTokens) +
                ", latencyms = " + AiSql.Long(D, m.LatencyMs) + ", ttftms = " + AiSql.Long(D, m.TimeToFirstTokenMs) + ", ttltms = " + AiSql.Long(D, m.TimeToLastTokenMs) +
                ", tokenspersecond = " + AiSql.Dbl(D, m.TokensPerSecond) + ", finishreason = " + D.Str(m.FinishReason) + ", costmicros = " + D.Num(m.CostMicros) +
                ", moderationverdict = " + D.Enum(m.ModerationVerdict) + ", moderationcategory = " + D.Str(m.ModerationCategory) + ", traceid = " + D.Str(m.TraceId) +
                ", retrievalquery = " + D.Str(m.RetrievalQuery) + ", completedutc = " + D.Ts(m.CompletedUtc) +
                " WHERE " + Scope() + " AND id = " + D.Str(m.Id) + " AND status IN ('Pending', 'Streaming') RETURNING id;"
            };
            DataTable updated = await TransactionAsync(statements, token).ConfigureAwait(false);
            if (updated.Rows.Count == 0) return false;
            List<string> children = new List<string>
            {
                "DELETE FROM aimessagecitations WHERE " + Scope() + " AND messageid = " + D.Str(m.Id) + ";",
                "DELETE FROM aimessagestages WHERE " + Scope() + " AND messageid = " + D.Str(m.Id) + ";",
                "DELETE FROM aitoolcallresults WHERE " + Scope() + " AND toolcallid IN (SELECT tc.id FROM aitoolcalls tc WHERE tc.propertyid = " + Pid + " AND tc.messageid = " + D.Str(m.Id) + ");",
                "DELETE FROM aitoolcalls WHERE " + Scope() + " AND messageid = " + D.Str(m.Id) + ";"
            };
            int position = 0;
            HashSet<int> used = new HashSet<int>();
            foreach (AssistantCitation citation in m.Citations)
            {
                // The stored position is the citation key minus one, so keys survive a read ([7] stays [7]).
                int slot = citation.Index > 0 && !used.Contains(citation.Index - 1) ? citation.Index - 1 : Enumerable.Range(position, 1000).First(i => !used.Contains(i));
                used.Add(slot);
                position = Math.Max(position, slot + 1);
                children.Add("INSERT INTO aimessagecitations (propertyid, messageid, position, targettype, targetid, title, path, score) VALUES (" + Pid + ", " + D.Str(m.Id) + ", " + D.Num(slot) + ", " +
                    D.Str(citation.TargetType) + ", " + D.Str(citation.TargetId) + ", " + D.Str(citation.Title) + ", " + D.Str(citation.Path) + ", " + AiSql.Dbl(D, citation.Score) + ");");
            }
            position = 0;
            foreach (AssistantMessageStage stage in m.Stages)
                children.Add("INSERT INTO aimessagestages (propertyid, messageid, position, stage, endpointid, model, startedutc, durationms, success, detail, prompttokens, completiontokens) VALUES (" + Pid + ", " +
                    D.Str(m.Id) + ", " + D.Num(position++) + ", " + D.Str(stage.Stage) + ", " + D.Str(stage.EndpointId) + ", " + D.Str(stage.Model) + ", " + D.Ts(stage.StartedUtc) + ", " + D.Num(stage.DurationMs) + ", " +
                    D.Bool(stage.Success) + ", " + D.Str(stage.Detail) + ", " + D.Num(stage.PromptTokens) + ", " + D.Num(stage.CompletionTokens) + ");");
            position = 0;
            foreach (AssistantToolCall call in m.ToolCalls.Take(AiConstants.MaxToolCallsPerReply))
            {
                children.Add("INSERT INTO aitoolcalls (propertyid, id, messageid, position, round, toolname, argumentsjson, status, resultsummary, resultcount, resultcharacters, durationms, error, createdutc) VALUES (" +
                    Pid + ", " + D.Str(call.Id) + ", " + D.Str(m.Id) + ", " + D.Num(position++) + ", " + D.Num(call.Round) + ", " + D.Str(Bound(call.ToolName, 100)) + ", " +
                    D.Str(Bound(call.ArgumentsJson, AiConstants.MaxToolArgumentsCharacters)) + ", " + D.Str(call.Status) + ", " + D.Str(call.ResultSummary == null ? null : Bound(call.ResultSummary, AiConstants.ToolResultSummaryCharacters)) + ", " +
                    D.Num(call.ResultCount) + ", " + D.Num(call.ResultCharacters) + ", " + D.Num(call.DurationMs) + ", " + D.Str(call.Error == null ? null : Bound(call.Error, 500)) + ", " + D.Ts(call.CreatedUtc) + ");");
                int resultPosition = 0;
                foreach (AssistantToolCallResult result in call.Results.Take(AiConstants.MaxToolResults))
                    children.Add("INSERT INTO aitoolcallresults (propertyid, toolcallid, position, targettype, targetid, title, path) VALUES (" + Pid + ", " + D.Str(call.Id) + ", " + D.Num(resultPosition++) + ", " +
                        D.Str(Bound(result.TargetType, 64)) + ", " + D.Str(Bound(result.TargetId, 128)) + ", " + D.Str(Bound(result.Title, 300)) + ", " + D.Str(Bound(result.Path, 500)) + ");");
            }
            DateTime last = m.CompletedUtc ?? DateTime.UtcNow;
            children.Add("UPDATE aiconversations SET lastmessageutc = " + D.Ts(last) + ", lastmessagepreview = " + D.Str(Preview(m.Content)) + ", updatedutc = " + D.Ts(last) +
                " WHERE " + Scope() + " AND id = " + D.Str(m.ConversationId) + ";");
            await TransactionAsync(children, token).ConfigureAwait(false);
            return true;
        }

        public async Task<EnumerationResult<AssistantMessage>> EnumerateMessagesAsync(EnumerationRequest request, string conversationId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            RequireText(conversationId, nameof(conversationId));
            List<string> conditions = new List<string> { "m.conversationid = " + D.Str(conversationId) };
            string select = String.Join(", ", _MessageColumns.Split(',').Select(c => "m." + c.Trim()));
            EnumerationResult<AssistantMessage> page = await EnumerateAsync(request, select, "aimessages m", conditions, MapMessage, token).ConfigureAwait(false);
            await AttachMessageChildrenAsync(page.Objects, true, token).ConfigureAwait(false);
            return page;
        }

        public async Task<List<AssistantMessage>> RecentMessagesAsync(string conversationId, int limit, CancellationToken token = default)
        {
            RequireText(conversationId, nameof(conversationId));
            DataTable table = await QueryAsync("SELECT " + _MessageColumns + " FROM aimessages WHERE " + Scope() + " AND conversationid = " + D.Str(conversationId) +
                " ORDER BY createdutc DESC, id DESC LIMIT " + Math.Clamp(limit, 1, 1000) + ";", token).ConfigureAwait(false);
            List<AssistantMessage> messages = table.Rows.Cast<DataRow>().Select(MapMessage).Reverse().ToList();
            await AttachMessageChildrenAsync(messages, true, token).ConfigureAwait(false);
            return messages;
        }

        public async Task<List<AssistantConversation>> ListByUserAsync(string userId, string? afterId, int limit, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            string after = String.IsNullOrEmpty(afterId) ? String.Empty : " AND id > " + D.Str(afterId);
            DataTable table = await QueryAsync("SELECT " + _ConversationColumns + " FROM aiconversations WHERE " + Scope() + " AND userid = " + D.Str(userId) + after +
                " ORDER BY id LIMIT " + D.Num(Math.Clamp(limit, 1, 500)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapConversation).ToList();
        }

        public async Task<List<AssistantMessage>> ListMessagesAfterAsync(string conversationId, string? afterMessageId, int limit, CancellationToken token = default)
        {
            RequireText(conversationId, nameof(conversationId));
            string after = String.IsNullOrEmpty(afterMessageId) ? String.Empty
                : " AND (createdutc, id) > (SELECT a.createdutc, a.id FROM aimessages a WHERE " + Scope("a") + " AND a.id = " + D.Str(afterMessageId) + ")";
            DataTable table = await QueryAsync("SELECT " + _MessageColumns + " FROM aimessages WHERE " + Scope() + " AND conversationid = " + D.Str(conversationId) + after +
                " ORDER BY createdutc, id LIMIT " + D.Num(Math.Clamp(limit, 1, 500)) + ";", token).ConfigureAwait(false);
            List<AssistantMessage> messages = table.Rows.Cast<DataRow>().Select(MapMessage).ToList();
            await AttachMessageChildrenAsync(messages, false, token).ConfigureAwait(false);
            return messages;
        }

        public async Task<Dictionary<string, AssistantFeedback>> ReadFeedbackByMessagesAsync(IEnumerable<string> messageIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(messageIds);
            List<string> ids = messageIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            Dictionary<string, AssistantFeedback> result = new Dictionary<string, AssistantFeedback>(StringComparer.Ordinal);
            if (ids.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT " + _FeedbackColumns + " FROM aifeedback WHERE " + Scope() + " AND messageid IN " + D.InList(ids) + ";", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows)
            {
                AssistantFeedback feedback = MapFeedback(row);
                if (feedback.MessageId != null) result[feedback.MessageId] = feedback;
            }
            return result;
        }

        public Task UpsertFeedbackAsync(AssistantFeedback feedback, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(feedback);
            AssistantFeedback f = feedback;
            f.PropertyId = PropertyId;
            return QueryAsync("INSERT INTO aifeedback (propertyid, " + _FeedbackColumns + ") VALUES (" + Pid + ", " + D.Str(f.Id) + ", " + D.Str(f.MessageId) + ", " + D.Str(f.ConversationId) + ", " +
                D.Str(f.AssistantId) + ", " + D.Str(f.UserId) + ", " + D.Str(f.VisitorId) + ", " + D.Enum(f.Rating) + ", " + D.Str(f.Note) + ", " + D.Str(String.Join(",", f.Reasons)) + ", " +
                D.Str(f.PromptCopy) + ", " + D.Str(f.ReplyCopy) + ", " + D.Bool(f.Reviewed) + ", " + D.Str(f.ReviewedByUserId) + ", " + D.Ts(f.ReviewedUtc) + ", " + D.Ts(f.CreatedUtc) + ", " + D.Ts(f.UpdatedUtc) +
                ") ON CONFLICT (propertyid, messageid) WHERE messageid IS NOT NULL DO UPDATE SET rating = EXCLUDED.rating, note = EXCLUDED.note, reasons = EXCLUDED.reasons, reviewed = " + D.Bool(false) +
                ", reviewedbyuserid = NULL, reviewedutc = NULL, updatedutc = EXCLUDED.updatedutc;", token);
        }

        public Task<AssistantFeedback?> ReadFeedbackByMessageAsync(string messageId, CancellationToken token = default)
        {
            RequireText(messageId, nameof(messageId));
            return ReadOneFeedbackAsync("messageid = " + D.Str(messageId), token);
        }

        public Task<AssistantFeedback?> ReadFeedbackAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            return ReadOneFeedbackAsync("id = " + D.Str(id), token);
        }

        public async Task<bool> DeleteFeedbackByMessageAsync(string messageId, CancellationToken token = default)
        {
            RequireText(messageId, nameof(messageId));
            DataTable result = await QueryAsync("DELETE FROM aifeedback WHERE " + Scope() + " AND messageid = " + D.Str(messageId) + " RETURNING id;", token).ConfigureAwait(false);
            return result.Rows.Count > 0;
        }

        public Task<EnumerationResult<AssistantFeedback>> EnumerateFeedbackAsync(EnumerationRequest request, AiEnumerationFilter filter, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = new List<string>();
            if (!String.IsNullOrEmpty(filter.AssistantId)) conditions.Add("f.assistantid = " + D.Str(filter.AssistantId));
            if (filter.Rating != null) conditions.Add("f.rating = " + D.Enum(filter.Rating.Value));
            if (filter.HasNote == true) conditions.Add("f.note IS NOT NULL AND f.note <> ''");
            if (filter.HasNote == false) conditions.Add("(f.note IS NULL OR f.note = '')");
            if (filter.Reviewed != null) conditions.Add("f.reviewed = " + D.Bool(filter.Reviewed.Value));
            string select = String.Join(", ", _FeedbackColumns.Split(',').Select(c => "f." + c.Trim()));
            return EnumerateAsync(request, select, "aifeedback f", conditions, MapFeedback, token);
        }

        public async Task<bool> ReviewFeedbackAsync(string id, string? reviewerUserId, DateTime nowUtc, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            DataTable result = await QueryAsync("UPDATE aifeedback SET reviewed = " + D.Bool(true) + ", reviewedbyuserid = " + D.Str(reviewerUserId) + ", reviewedutc = " + D.Ts(nowUtc) + ", updatedutc = " +
                D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(id) + " RETURNING id;", token).ConfigureAwait(false);
            return result.Rows.Count > 0;
        }

        public async Task<AiDailyUsage?> ReadDailyUsageAsync(string assistantId, string subjectType, string subjectId, string date, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT assistantid, subjecttype, subjectid, date, messages, prompttokens, completiontokens, costmicros FROM aidailyusage WHERE " + Scope() +
                " AND assistantid = " + D.Str(assistantId) + " AND subjecttype = " + D.Str(subjectType) + " AND subjectid = " + D.Str(subjectId) + " AND date = " + D.Str(date) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            DataRow row = table.Rows[0];
            return new AiDailyUsage
            {
                AssistantId = RowReader.GetString(row, "assistantid"),
                SubjectType = RowReader.GetString(row, "subjecttype"),
                SubjectId = RowReader.GetString(row, "subjectid"),
                Date = RowReader.GetString(row, "date"),
                Messages = RowReader.GetInt(row, "messages"),
                PromptTokens = RowReader.GetLong(row, "prompttokens"),
                CompletionTokens = RowReader.GetLong(row, "completiontokens"),
                CostMicros = RowReader.GetLong(row, "costmicros")
            };
        }

        public Task AddDailyUsageAsync(AiDailyUsage usage, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(usage);
            AiDailyUsage u = usage;
            return QueryAsync("INSERT INTO aidailyusage (propertyid, assistantid, subjecttype, subjectid, date, messages, prompttokens, completiontokens, costmicros) VALUES (" + Pid + ", " +
                D.Str(u.AssistantId) + ", " + D.Str(u.SubjectType) + ", " + D.Str(u.SubjectId) + ", " + D.Str(u.Date) + ", " + D.Num(u.Messages) + ", " + D.Num(u.PromptTokens) + ", " +
                D.Num(u.CompletionTokens) + ", " + D.Num(u.CostMicros) + ") ON CONFLICT (propertyid, assistantid, subjecttype, subjectid, date) DO UPDATE SET messages = aidailyusage.messages + EXCLUDED.messages, " +
                "prompttokens = aidailyusage.prompttokens + EXCLUDED.prompttokens, completiontokens = aidailyusage.completiontokens + EXCLUDED.completiontokens, " +
                "costmicros = aidailyusage.costmicros + EXCLUDED.costmicros;", token);
        }

        public async Task<int> DeleteIdleConversationsAsync(string assistantId, DateTime idleBeforeUtc, int limit, CancellationToken token = default)
        {
            RequireText(assistantId, nameof(assistantId));
            DataTable ids = await QueryAsync("SELECT id FROM aiconversations WHERE " + Scope() + " AND assistantid = " + D.Str(assistantId) + " AND COALESCE(lastmessageutc, createdutc) < " +
                D.Ts(idleBeforeUtc) + " ORDER BY id LIMIT " + Math.Clamp(limit, 1, 1000) + ";", token).ConfigureAwait(false);
            return await DeleteConversationsAsync(ids, token).ConfigureAwait(false);
        }

        public async Task<int> DeleteVisitorConversationsAsync(DateTime idleBeforeUtc, int limit, CancellationToken token = default)
        {
            DataTable ids = await QueryAsync("SELECT id FROM aiconversations WHERE " + Scope() + " AND visitorid IS NOT NULL AND COALESCE(lastmessageutc, createdutc) < " + D.Ts(idleBeforeUtc) +
                " ORDER BY id LIMIT " + Math.Clamp(limit, 1, 1000) + ";", token).ConfigureAwait(false);
            return await DeleteConversationsAsync(ids, token).ConfigureAwait(false);
        }

        public async Task DeleteForUserAsync(string userId, CancellationToken token = default)
        {
            RequireText(userId, nameof(userId));
            string inner = "(SELECT x.id FROM aiconversations x WHERE x.propertyid = " + Pid + " AND x.userid = " + D.Str(userId) + ")";
            List<string> statements = DeleteConversationStatements(inner);
            statements.Insert(0, "DELETE FROM aifeedback WHERE " + Scope() + " AND userid = " + D.Str(userId) + ";");
            statements.Add("DELETE FROM aidailyusage WHERE " + Scope() + " AND subjecttype = 'User' AND subjectid = " + D.Str(userId) + ";");
            await TransactionAsync(statements, token).ConfigureAwait(false);
        }

        public async Task<int> PruneFeedbackAsync(DateTime olderThanUtc, CancellationToken token = default)
        {
            DataTable result = await QueryAsync("DELETE FROM aifeedback WHERE " + Scope() + " AND createdutc < " + D.Ts(olderThanUtc) + " RETURNING id;", token).ConfigureAwait(false);
            return result.Rows.Count;
        }

        public async Task<int> FailStaleRepliesAsync(DateTime beforeUtc, CancellationToken token = default)
        {
            DataTable result = await QueryAsync("UPDATE aimessages SET status = " + D.Enum(AssistantMessageStatusEnum.Failed) + ", errorcode = 'ModelError', completedutc = " + D.Ts(DateTime.UtcNow) +
                " WHERE " + Scope() + " AND status IN ('Pending', 'Streaming') AND createdutc < " + D.Ts(beforeUtc) + " RETURNING id;", token).ConfigureAwait(false);
            return result.Rows.Count;
        }

        private async Task<int> DeleteConversationsAsync(DataTable ids, CancellationToken token)
        {
            if (ids.Rows.Count == 0) return 0;
            List<string> list = ids.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "id")).ToList();
            await TransactionAsync(DeleteConversationStatements(D.InList(list)), token).ConfigureAwait(false);
            return list.Count;
        }

        private List<string> DeleteConversationStatements(string conversationIds)
        {
            string messages = "(SELECT mm.id FROM aimessages mm WHERE mm.propertyid = " + Pid + " AND mm.conversationid IN " + conversationIds + ")";
            return new List<string>
            {
                "DELETE FROM aimessagecitations WHERE " + Scope() + " AND messageid IN " + messages + ";",
                "DELETE FROM aimessagestages WHERE " + Scope() + " AND messageid IN " + messages + ";",
                "DELETE FROM aitoolcallresults WHERE " + Scope() + " AND toolcallid IN (SELECT tc.id FROM aitoolcalls tc WHERE tc.propertyid = " + Pid + " AND tc.messageid IN " + messages + ");",
                "DELETE FROM aitoolcalls WHERE " + Scope() + " AND messageid IN " + messages + ";",
                "UPDATE aifeedback SET messageid = NULL, conversationid = NULL WHERE " + Scope() + " AND conversationid IN " + conversationIds + ";",
                "DELETE FROM aimessages WHERE " + Scope() + " AND conversationid IN " + conversationIds + ";",
                "DELETE FROM aiconversations WHERE " + Scope() + " AND id IN " + conversationIds + " RETURNING id;"
            };
        }

        private async Task<AssistantMessage?> ReadOneMessageAsync(string condition, CancellationToken token)
        {
            DataTable table = await QueryAsync("SELECT " + _MessageColumns + " FROM aimessages WHERE " + Scope() + " AND " + condition + " ORDER BY createdutc DESC LIMIT 1;", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            List<AssistantMessage> list = new List<AssistantMessage> { MapMessage(table.Rows[0]) };
            await AttachMessageChildrenAsync(list, true, token).ConfigureAwait(false);
            return list[0];
        }

        private async Task AttachMessageChildrenAsync(List<AssistantMessage> messages, bool stages, CancellationToken token)
        {
            List<AssistantMessage> replies = messages.Where(m => m.Role == AssistantMessageRoleEnum.Assistant).ToList();
            if (replies.Count == 0) return;
            string ids = D.InList(replies.Select(m => m.Id));
            Dictionary<string, AssistantMessage> byId = replies.ToDictionary(m => m.Id, StringComparer.Ordinal);
            DataTable citations = await QueryAsync("SELECT messageid, position, targettype, targetid, title, path, score FROM aimessagecitations WHERE " + Scope() + " AND messageid IN " + ids +
                " ORDER BY messageid, position;", token).ConfigureAwait(false);
            foreach (DataRow row in citations.Rows)
            {
                if (!byId.TryGetValue(RowReader.GetString(row, "messageid"), out AssistantMessage? m)) continue;
                m.Citations.Add(new AssistantCitation
                {
                    Index = RowReader.GetInt(row, "position") + 1,
                    TargetType = RowReader.GetString(row, "targettype"),
                    TargetId = RowReader.GetString(row, "targetid"),
                    Title = RowReader.GetString(row, "title"),
                    Path = RowReader.GetString(row, "path"),
                    Score = AiSql.NullableDouble(row, "score")
                });
            }
            await AttachToolCallsAsync(byId, ids, token).ConfigureAwait(false);
            if (!stages) return;
            DataTable stageRows = await QueryAsync("SELECT messageid, position, stage, endpointid, model, startedutc, durationms, success, detail, prompttokens, completiontokens FROM aimessagestages WHERE " + Scope() +
                " AND messageid IN " + ids + " ORDER BY messageid, position;", token).ConfigureAwait(false);
            foreach (DataRow row in stageRows.Rows)
            {
                if (!byId.TryGetValue(RowReader.GetString(row, "messageid"), out AssistantMessage? m)) continue;
                m.Stages.Add(new AssistantMessageStage
                {
                    Position = RowReader.GetInt(row, "position"),
                    Stage = RowReader.GetString(row, "stage"),
                    EndpointId = RowReader.GetNullableString(row, "endpointid"),
                    Model = RowReader.GetNullableString(row, "model"),
                    StartedUtc = RowReader.GetDateTime(row, "startedutc"),
                    DurationMs = RowReader.GetLong(row, "durationms"),
                    Success = RowReader.GetBool(row, "success"),
                    Detail = RowReader.GetNullableString(row, "detail"),
                    PromptTokens = RowReader.GetNullableInt(row, "prompttokens"),
                    CompletionTokens = RowReader.GetNullableInt(row, "completiontokens")
                });
            }
        }

        private async Task AttachToolCallsAsync(Dictionary<string, AssistantMessage> byId, string ids, CancellationToken token)
        {
            DataTable rows = await QueryAsync("SELECT id, messageid, position, round, toolname, argumentsjson, status, resultsummary, resultcount, resultcharacters, durationms, error, createdutc FROM aitoolcalls WHERE " +
                Scope() + " AND messageid IN " + ids + " ORDER BY messageid, position LIMIT " + (byId.Count * AiConstants.MaxToolCallsPerReply) + ";", token).ConfigureAwait(false);
            if (rows.Rows.Count == 0) return;
            Dictionary<string, AssistantToolCall> calls = new Dictionary<string, AssistantToolCall>(StringComparer.Ordinal);
            foreach (DataRow row in rows.Rows)
            {
                if (!byId.TryGetValue(RowReader.GetString(row, "messageid"), out AssistantMessage? m)) continue;
                AssistantToolCall call = new AssistantToolCall
                {
                    Id = RowReader.GetString(row, "id"),
                    MessageId = m.Id,
                    Position = RowReader.GetInt(row, "position"),
                    Round = RowReader.GetInt(row, "round"),
                    ToolName = RowReader.GetString(row, "toolname"),
                    ArgumentsJson = RowReader.GetString(row, "argumentsjson"),
                    Status = RowReader.GetString(row, "status"),
                    ResultSummary = RowReader.GetNullableString(row, "resultsummary"),
                    ResultCount = RowReader.GetInt(row, "resultcount"),
                    ResultCharacters = RowReader.GetInt(row, "resultcharacters"),
                    DurationMs = RowReader.GetLong(row, "durationms"),
                    Error = RowReader.GetNullableString(row, "error"),
                    CreatedUtc = RowReader.GetDateTime(row, "createdutc")
                };
                m.ToolCalls.Add(call);
                calls[call.Id] = call;
            }
            DataTable results = await QueryAsync("SELECT toolcallid, targettype, targetid, title, path FROM aitoolcallresults WHERE " + Scope() + " AND toolcallid IN " + D.InList(calls.Keys) +
                " ORDER BY toolcallid, position LIMIT " + (calls.Count * AiConstants.MaxToolResults) + ";", token).ConfigureAwait(false);
            foreach (DataRow row in results.Rows)
            {
                if (!calls.TryGetValue(RowReader.GetString(row, "toolcallid"), out AssistantToolCall? call)) continue;
                call.Results.Add(new AssistantToolCallResult
                {
                    TargetType = RowReader.GetString(row, "targettype"),
                    TargetId = RowReader.GetString(row, "targetid"),
                    Title = RowReader.GetString(row, "title"),
                    Path = RowReader.GetString(row, "path")
                });
            }
        }

        private static string Bound(string value, int max)
        {
            return value.Length <= max ? value : value.Substring(0, max);
        }

        private async Task<AssistantFeedback?> ReadOneFeedbackAsync(string condition, CancellationToken token)
        {
            DataTable table = await QueryAsync("SELECT " + _FeedbackColumns + " FROM aifeedback WHERE " + Scope() + " AND " + condition + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : MapFeedback(table.Rows[0]);
        }

        private string InsertMessage(AssistantMessage m)
        {
            m.PropertyId = PropertyId;
            return "INSERT INTO aimessages (propertyid, " + _MessageColumns + ") VALUES (" + Pid + ", " + D.Str(m.Id) + ", " + D.Str(m.ConversationId) + ", " + D.Str(m.AssistantId) + ", " + D.Enum(m.Role) + ", " +
                D.Str(m.Content) + ", " + D.Str(m.Reasoning) + ", " + D.Enum(m.Status) + ", " + D.Str(m.ErrorCode) + ", " + D.Str(m.EndpointId) + ", " + D.Str(m.EndpointName) + ", " + D.Str(m.Model) + ", " +
                D.Bool(m.UsedFallback) + ", " + D.Num(m.PromptTokens) + ", " + D.Num(m.CompletionTokens) + ", " + D.Num(m.ReasoningTokens) + ", " + AiSql.Long(D, m.LatencyMs) + ", " +
                AiSql.Long(D, m.TimeToFirstTokenMs) + ", " + AiSql.Long(D, m.TimeToLastTokenMs) + ", " + AiSql.Dbl(D, m.TokensPerSecond) + ", " + D.Str(m.FinishReason) + ", " + D.Num(m.CostMicros) + ", " +
                D.Enum(m.ModerationVerdict) + ", " + D.Str(m.ModerationCategory) + ", " + D.Bool(m.IsSuperseded) + ", " + D.Str(m.ClientMessageId) + ", " + D.Str(m.TraceId) + ", " +
                D.Str(m.ReplyToMessageId) + ", " + D.Str(m.RetrievalQuery) + ", " + D.Ts(m.CreatedUtc) + ", " + D.Ts(m.CompletedUtc) + ");";
        }

        private static string Preview(string? content)
        {
            string text = (content ?? String.Empty).Replace('\n', ' ').Replace('\r', ' ').Trim();
            return text.Length <= _PreviewLength ? text : text.Substring(0, _PreviewLength);
        }

        private AssistantConversation MapConversation(DataRow row)
        {
            return new AssistantConversation
            {
                PropertyId = PropertyId,
                Id = RowReader.GetString(row, "id"),
                AssistantId = RowReader.GetString(row, "assistantid"),
                UserId = RowReader.GetNullableString(row, "userid"),
                VisitorId = RowReader.GetNullableString(row, "visitorid"),
                Title = RowReader.GetNullableString(row, "title"),
                TitleSource = RowReader.GetEnum(row, "titlesource", AssistantTitleSourceEnum.Fallback),
                Status = RowReader.GetEnum(row, "status", AssistantConversationStatusEnum.Active),
                IsArchived = RowReader.GetBool(row, "isarchived"),
                MessageCount = RowReader.GetInt(row, "messagecount"),
                LastMessageUtc = RowReader.GetNullableDateTime(row, "lastmessageutc"),
                LastMessagePreview = RowReader.GetNullableString(row, "lastmessagepreview"),
                Surface = RowReader.GetEnum(row, "surface", AiConversationSurfaceEnum.Web),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        private AssistantMessage MapMessage(DataRow row)
        {
            return new AssistantMessage
            {
                PropertyId = PropertyId,
                Id = RowReader.GetString(row, "id"),
                ConversationId = RowReader.GetString(row, "conversationid"),
                AssistantId = RowReader.GetString(row, "assistantid"),
                Role = RowReader.GetEnum(row, "role", AssistantMessageRoleEnum.User),
                Content = RowReader.GetString(row, "content"),
                Reasoning = RowReader.GetNullableString(row, "reasoning"),
                Status = RowReader.GetEnum(row, "status", AssistantMessageStatusEnum.Completed),
                ErrorCode = RowReader.GetNullableString(row, "errorcode"),
                EndpointId = RowReader.GetNullableString(row, "endpointid"),
                EndpointName = RowReader.GetNullableString(row, "endpointname"),
                Model = RowReader.GetNullableString(row, "model"),
                UsedFallback = RowReader.GetBool(row, "usedfallback"),
                PromptTokens = RowReader.GetInt(row, "prompttokens"),
                CompletionTokens = RowReader.GetInt(row, "completiontokens"),
                ReasoningTokens = RowReader.GetInt(row, "reasoningtokens"),
                LatencyMs = RowReader.GetNullableLong(row, "latencyms"),
                TimeToFirstTokenMs = RowReader.GetNullableLong(row, "ttftms"),
                TimeToLastTokenMs = RowReader.GetNullableLong(row, "ttltms"),
                TokensPerSecond = AiSql.NullableDouble(row, "tokenspersecond"),
                FinishReason = RowReader.GetNullableString(row, "finishreason"),
                CostMicros = RowReader.GetLong(row, "costmicros"),
                ModerationVerdict = RowReader.GetNullableEnum<AssistantModerationVerdictEnum>(row, "moderationverdict"),
                ModerationCategory = RowReader.GetNullableString(row, "moderationcategory"),
                IsSuperseded = RowReader.GetBool(row, "issuperseded"),
                ClientMessageId = RowReader.GetNullableString(row, "clientmessageid"),
                TraceId = RowReader.GetNullableString(row, "traceid"),
                ReplyToMessageId = RowReader.GetNullableString(row, "replytomessageid"),
                RetrievalQuery = RowReader.GetNullableString(row, "retrievalquery"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                CompletedUtc = RowReader.GetNullableDateTime(row, "completedutc")
            };
        }

        private AssistantFeedback MapFeedback(DataRow row)
        {
            return new AssistantFeedback
            {
                PropertyId = PropertyId,
                Id = RowReader.GetString(row, "id"),
                MessageId = RowReader.GetNullableString(row, "messageid"),
                ConversationId = RowReader.GetNullableString(row, "conversationid"),
                AssistantId = RowReader.GetString(row, "assistantid"),
                UserId = RowReader.GetNullableString(row, "userid"),
                VisitorId = RowReader.GetNullableString(row, "visitorid"),
                Rating = RowReader.GetEnum(row, "rating", AssistantFeedbackRatingEnum.Up),
                Note = RowReader.GetNullableString(row, "note"),
                Reasons = (RowReader.GetNullableString(row, "reasons") ?? String.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
                PromptCopy = RowReader.GetString(row, "promptcopy"),
                ReplyCopy = RowReader.GetString(row, "replycopy"),
                Reviewed = RowReader.GetBool(row, "reviewed"),
                ReviewedByUserId = RowReader.GetNullableString(row, "reviewedbyuserid"),
                ReviewedUtc = RowReader.GetNullableDateTime(row, "reviewedutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }
    }
}
