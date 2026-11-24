namespace TrellisKit.Core.Database.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database.Interfaces;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Shared moderation snapshot methods.
    /// </summary>
    internal abstract class AiModerationMethodsBase : MethodsBase, IAiModerationMethods
    {
        private const string _SnapshotColumns = "id, reportid, messageid, conversationid, assistantid, owneruserid, reason, endpointid, endpointname, model, messagecount, createdutc, expiresutc";

        private const string _MessageColumns = "snapshotid, position, messageid, role, content, status, createdutc";

        private const string _ToolCallColumns = "snapshotid, position, toolcallid, round, toolname, argumentsjson, status, resultsummary, resultcount, resultcharacters, durationms, error, resultsjson, createdutc";

        protected AiModerationMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        public async Task<bool> CreateSnapshotAsync(AiModerationSnapshot snapshot, IReadOnlyList<AiModerationSnapshotMessage> messages, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(messages);
            RequireText(snapshot.Id, nameof(snapshot.Id));
            RequireText(snapshot.MessageId, nameof(snapshot.MessageId));
            AiModerationSnapshot s = snapshot;
            s.PropertyId = PropertyId;
            List<string> statements = new List<string>
            {
                "INSERT INTO aimoderationsnapshots (propertyid, " + _SnapshotColumns + ") VALUES (" + Pid + ", " + D.Str(s.Id) + ", " + D.Str(s.ReportId) + ", " + D.Str(s.MessageId) + ", " +
                D.Str(s.ConversationId) + ", " + D.Str(s.AssistantId) + ", " + D.Str(s.OwnerUserId) + ", " + D.Str(s.Reason) + ", " + D.Str(s.EndpointId) + ", " + D.Str(s.EndpointName) + ", " +
                D.Str(s.Model) + ", " + D.Num(messages.Count) + ", " + D.Ts(s.CreatedUtc) + ", " + D.Ts(s.ExpiresUtc) + ") ON CONFLICT (propertyid, messageid) DO NOTHING;"
            };
            foreach (AiModerationSnapshotMessage m in messages)
            {
                statements.Add("INSERT INTO aimoderationsnapshotmessages (propertyid, " + _MessageColumns + ") SELECT " + Pid + ", " + D.Str(s.Id) + ", " + D.Num(m.Position) + ", " + D.Str(m.MessageId) + ", " +
                    D.Enum(m.Role) + ", " + D.Str(m.Content) + ", " + D.Enum(m.Status) + ", " + D.Ts(m.CreatedUtc) + " WHERE EXISTS (SELECT 1 FROM aimoderationsnapshots x WHERE " + Scope("x") +
                    " AND x.id = " + D.Str(s.Id) + ") ON CONFLICT DO NOTHING;");
            }
            for (int i = 0; i < s.Citations.Count; i++)
            {
                AssistantCitation c = s.Citations[i];
                statements.Add("INSERT INTO aimoderationsnapshotcitations (propertyid, snapshotid, position, targettype, targetid, title, path) SELECT " + Pid + ", " + D.Str(s.Id) + ", " + D.Num(i) + ", " +
                    D.Str(c.TargetType) + ", " + D.Str(c.TargetId) + ", " + D.Str(c.Title) + ", " + D.Str(c.Path) + " WHERE EXISTS (SELECT 1 FROM aimoderationsnapshots x WHERE " + Scope("x") +
                    " AND x.id = " + D.Str(s.Id) + ") ON CONFLICT DO NOTHING;");
            }
            List<AssistantToolCall> calls = s.ToolCalls.Take(AiConstants.MaxToolCallsPerReply).ToList();
            for (int i = 0; i < calls.Count; i++)
            {
                AssistantToolCall c = calls[i];
                string results = JsonSerializer.Serialize(c.Results.Take(AiConstants.MaxToolResults).Select(r => new Dictionary<string, string> { { "targetType", r.TargetType }, { "targetId", r.TargetId }, { "title", r.Title }, { "path", r.Path } }).ToList());
                statements.Add("INSERT INTO aimoderationsnapshottoolcalls (propertyid, " + _ToolCallColumns + ") SELECT " + Pid + ", " + D.Str(s.Id) + ", " + D.Num(i) + ", " + D.Str(c.Id) + ", " +
                    D.Num(c.Round) + ", " + D.Str(c.ToolName) + ", " + D.Str(c.ArgumentsJson ?? "{}") + ", " + D.Str(c.Status) + ", " + D.Str(c.ResultSummary) + ", " + D.Num(c.ResultCount) + ", " +
                    D.Num(c.ResultCharacters) + ", " + D.Num(c.DurationMs) + ", " + D.Str(c.Error) + ", " + D.Str(results) + ", " + D.Ts(c.CreatedUtc) +
                    " WHERE EXISTS (SELECT 1 FROM aimoderationsnapshots x WHERE " + Scope("x") + " AND x.id = " + D.Str(s.Id) + ") ON CONFLICT DO NOTHING;");
            }
            statements.Add("SELECT id FROM aimoderationsnapshots WHERE " + Scope() + " AND id = " + D.Str(s.Id) + ";");
            DataTable result = await TransactionAsync(statements, token).ConfigureAwait(false);
            return result.Rows.Count > 0;
        }

        public async Task<AiModerationSnapshot?> ReadByMessageAsync(string messageId, CancellationToken token = default)
        {
            RequireText(messageId, nameof(messageId));
            DataTable table = await QueryAsync("SELECT " + _SnapshotColumns + " FROM aimoderationsnapshots WHERE " + Scope() + " AND messageid = " + D.Str(messageId) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            AiModerationSnapshot snapshot = Map(table.Rows[0]);
            DataTable citations = await QueryAsync("SELECT position, targettype, targetid, title, path FROM aimoderationsnapshotcitations WHERE " + Scope() + " AND snapshotid = " + D.Str(snapshot.Id) +
                " ORDER BY position;", token).ConfigureAwait(false);
            foreach (DataRow row in citations.Rows)
            {
                snapshot.Citations.Add(new AssistantCitation
                {
                    Index = RowReader.GetInt(row, "position") + 1,
                    TargetType = RowReader.GetString(row, "targettype"),
                    TargetId = RowReader.GetString(row, "targetid"),
                    Title = RowReader.GetString(row, "title"),
                    Path = RowReader.GetString(row, "path")
                });
            }
            DataTable calls = await QueryAsync("SELECT " + _ToolCallColumns + " FROM aimoderationsnapshottoolcalls WHERE " + Scope() + " AND snapshotid = " + D.Str(snapshot.Id) +
                " ORDER BY position LIMIT " + D.Num(AiConstants.MaxToolCallsPerReply) + ";", token).ConfigureAwait(false);
            foreach (DataRow row in calls.Rows) snapshot.ToolCalls.Add(MapToolCall(row, snapshot.MessageId));
            return snapshot;
        }

        public async Task<Dictionary<string, AiModerationSnapshot>> ReadManyByMessageAsync(IEnumerable<string> messageIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(messageIds);
            List<string> ids = messageIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            Dictionary<string, AiModerationSnapshot> result = new Dictionary<string, AiModerationSnapshot>(StringComparer.Ordinal);
            if (ids.Count == 0) return result;
            DataTable table = await QueryAsync("SELECT " + _SnapshotColumns + " FROM aimoderationsnapshots WHERE " + Scope() + " AND messageid IN " + D.InList(ids) + ";", token).ConfigureAwait(false);
            foreach (DataRow row in table.Rows)
            {
                AiModerationSnapshot snapshot = Map(row);
                result[snapshot.MessageId] = snapshot;
            }
            return result;
        }

        public async Task<List<AiModerationSnapshotMessage>> LastMessagesAsync(string snapshotId, int limit, CancellationToken token = default)
        {
            RequireText(snapshotId, nameof(snapshotId));
            DataTable table = await QueryAsync("SELECT " + _MessageColumns + " FROM aimoderationsnapshotmessages WHERE " + Scope() + " AND snapshotid = " + D.Str(snapshotId) +
                " ORDER BY position DESC LIMIT " + D.Num(Math.Clamp(limit, 1, 100)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapMessage).Reverse().ToList();
        }

        public Task<EnumerationResult<AiModerationSnapshotMessage>> EnumerateMessagesAsync(EnumerationRequest request, string snapshotId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            RequireText(snapshotId, nameof(snapshotId));
            List<string> conditions = new List<string> { "s.snapshotid = " + D.Str(snapshotId) };
            string select = String.Join(", ", _MessageColumns.Split(',').Select(c => "s." + c.Trim()));
            return EnumerateAsync(request, select, "aimoderationsnapshotmessages s", conditions, MapMessage, token);
        }

        public async Task<int> PruneAsync(DateTime beforeUtc, int limit, CancellationToken token = default)
        {
            string expired = "SELECT x.id FROM aimoderationsnapshots x WHERE " + Scope("x") + " AND x.expiresutc < " + D.Ts(beforeUtc) + " ORDER BY x.expiresutc LIMIT " + D.Num(Math.Clamp(limit, 1, 1000));
            List<string> statements = new List<string>
            {
                "DELETE FROM aimoderationsnapshotcitations WHERE " + Scope() + " AND snapshotid IN (" + expired + ");",
                "DELETE FROM aimoderationsnapshottoolcalls WHERE " + Scope() + " AND snapshotid IN (" + expired + ");",
                "DELETE FROM aimoderationsnapshotmessages WHERE " + Scope() + " AND snapshotid IN (" + expired + ");",
                "DELETE FROM aimoderationsnapshots WHERE " + Scope() + " AND id IN (" + expired + ") RETURNING id;"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count;
        }

        private static AssistantToolCall MapToolCall(DataRow row, string messageId)
        {
            AssistantToolCall call = new AssistantToolCall
            {
                Id = RowReader.GetString(row, "toolcallid"),
                MessageId = messageId,
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
            try
            {
                List<Dictionary<string, string>>? links = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(RowReader.GetString(row, "resultsjson"));
                foreach (Dictionary<string, string> link in (links ?? new List<Dictionary<string, string>>()).Take(AiConstants.MaxToolResults))
                {
                    call.Results.Add(new AssistantToolCallResult
                    {
                        TargetType = link.TryGetValue("targetType", out string? type) ? type : String.Empty,
                        TargetId = link.TryGetValue("targetId", out string? id) ? id : String.Empty,
                        Title = link.TryGetValue("title", out string? title) ? title : String.Empty,
                        Path = link.TryGetValue("path", out string? path) ? path : String.Empty
                    });
                }
            }
            catch (JsonException)
            {
                // A damaged links column leaves the call without links.
            }
            return call;
        }

        private AiModerationSnapshot Map(DataRow row)
        {
            return new AiModerationSnapshot
            {
                PropertyId = PropertyId,
                Id = RowReader.GetString(row, "id"),
                ReportId = RowReader.GetNullableString(row, "reportid"),
                MessageId = RowReader.GetString(row, "messageid"),
                ConversationId = RowReader.GetString(row, "conversationid"),
                AssistantId = RowReader.GetString(row, "assistantid"),
                OwnerUserId = RowReader.GetNullableString(row, "owneruserid"),
                Reason = RowReader.GetString(row, "reason"),
                EndpointId = RowReader.GetNullableString(row, "endpointid"),
                EndpointName = RowReader.GetNullableString(row, "endpointname"),
                Model = RowReader.GetNullableString(row, "model"),
                MessageCount = RowReader.GetInt(row, "messagecount"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                ExpiresUtc = RowReader.GetDateTime(row, "expiresutc")
            };
        }

        private static AiModerationSnapshotMessage MapMessage(DataRow row)
        {
            return new AiModerationSnapshotMessage
            {
                SnapshotId = RowReader.GetString(row, "snapshotid"),
                Position = RowReader.GetInt(row, "position"),
                MessageId = RowReader.GetString(row, "messageid"),
                Role = RowReader.GetEnum(row, "role", AssistantMessageRoleEnum.User),
                Content = RowReader.GetString(row, "content"),
                Status = RowReader.GetEnum(row, "status", AssistantMessageStatusEnum.Completed),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
        }
    }
}
