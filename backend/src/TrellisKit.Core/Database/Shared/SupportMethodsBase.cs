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

    /// <summary>
    /// Shared support ticket methods (ticket types, tickets, ticket messages).
    /// </summary>
    internal abstract class SupportMethodsBase : MethodsBase, ISupportMethods
    {
        private const string _TypeColumns = "id, key, label, description, helptext, sortorder, isretired, createdutc, updatedutc";

        private const string _TicketColumns = "id, number, typekey, title, body, status, priority, requesteruserid, assigneeuserid, ideaid, includediagnostics, deviceclient, " +
            "deviceclientversion, deviceplatform, deviceosversion, devicemodel, deviceupdateid, devicelocale, devicetimezone, membershipactive, membershipsource, membershipstore, " +
            "membershipexpiresutc, firstresponsedueutc, firstresponseutc, overduenotifiedutc, lastactivityutc, lastmemberreplyutc, resolvedutc, closedutc, createdutc, updatedutc";

        private const string _MessageColumns = "id, ticketid, kind, authoruserid, authorrole, body, statuschange, createdutc";

        protected SupportMethodsBase(DatabaseDriverBase driver, SqlDialect dialect, string? propertyId) : base(driver, dialect, propertyId)
        {
        }

        #region Ticket-Types

        public async Task<int> SeedTypesAsync(List<TicketType> types, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(types);
            if (types.Count == 0) return 0;
            List<string> statements = new List<string>();
            foreach (TicketType type in types)
            {
                RequireText(type.Id, nameof(type.Id));
                statements.Add("INSERT INTO tickettypes (propertyid, " + _TypeColumns + ") SELECT " + Pid + ", " + TypeValues(type) +
                    " WHERE NOT EXISTS (SELECT 1 FROM tickettypes x WHERE " + Scope("x") + " AND lower(x.key) = lower(" + D.Str(type.Key) + ")) ON CONFLICT DO NOTHING;");
                foreach (string email in type.StaffRecipients)
                    statements.Add("INSERT INTO tickettyperecipients (propertyid, tickettypeid, email) SELECT " + Pid + ", " + D.Str(type.Id) + ", " + D.Str(email) +
                        " WHERE EXISTS (SELECT 1 FROM tickettypes x WHERE " + Scope("x") + " AND x.id = " + D.Str(type.Id) + ") ON CONFLICT DO NOTHING;");
            }
            statements.Add("SELECT COUNT(*) AS cnt FROM tickettypes WHERE " + Scope() + " AND id IN " + D.InList(types.Select(t => t.Id)) + ";");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? 0 : (int)RowReader.GetLong(table.Rows[0], "cnt");
        }

        public Task<long> CountTypesAsync(CancellationToken token = default)
        {
            return CountAsync("SELECT COUNT(*) AS cnt FROM tickettypes WHERE " + Scope() + ";", token);
        }

        public async Task<List<TicketType>> ListTypesAsync(bool includeRetired, int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT " + _TypeColumns + " FROM tickettypes WHERE " + Scope() + (includeRetired ? String.Empty : " AND isretired = " + D.Bool(false)) +
                " ORDER BY sortorder, id LIMIT " + D.Num(Math.Clamp(limit, 1, 1000)) + ";", token).ConfigureAwait(false);
            List<TicketType> types = table.Rows.Cast<DataRow>().Select(MapType).ToList();
            await FillRecipientsAsync(types, token).ConfigureAwait(false);
            return types;
        }

        public async Task<TicketType?> ReadTypeAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            DataTable table = await QueryAsync("SELECT " + _TypeColumns + " FROM tickettypes WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            TicketType type = MapType(table.Rows[0]);
            await FillRecipientsAsync(new List<TicketType> { type }, token).ConfigureAwait(false);
            return type;
        }

        public async Task<TicketType?> ReadTypeByKeyAsync(string key, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(key)) return null;
            DataTable table = await QueryAsync("SELECT " + _TypeColumns + " FROM tickettypes WHERE " + Scope() + " AND lower(key) = lower(" + D.Str(key.Trim()) + ");", token).ConfigureAwait(false);
            if (table.Rows.Count == 0) return null;
            TicketType type = MapType(table.Rows[0]);
            await FillRecipientsAsync(new List<TicketType> { type }, token).ConfigureAwait(false);
            return type;
        }

        public async Task<TicketType> CreateTypeAsync(TicketType type, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(type);
            RequireText(type.Id, nameof(type.Id));
            List<string> statements = new List<string> { "INSERT INTO tickettypes (propertyid, " + _TypeColumns + ") VALUES (" + Pid + ", " + TypeValues(type) + ");" };
            statements.AddRange(RecipientStatements(type));
            await UniqueTransactionAsync(statements, "Ticket type " + type.Key, token).ConfigureAwait(false);
            return type;
        }

        public async Task<bool> UpdateTypeAsync(TicketType type, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(type);
            RequireText(type.Id, nameof(type.Id));
            List<string> statements = new List<string> { "DELETE FROM tickettyperecipients WHERE " + Scope() + " AND tickettypeid = " + D.Str(type.Id) + ";" };
            statements.AddRange(RecipientStatements(type));
            statements.Add("UPDATE tickettypes SET key = " + D.Str(type.Key) + ", label = " + D.Str(type.Label) + ", description = " + D.Str(type.Description) + ", helptext = " + D.Str(type.HelpText) +
                ", sortorder = " + D.Num(type.SortOrder) + ", isretired = " + D.Bool(type.IsRetired) + ", updatedutc = " + D.Ts(type.UpdatedUtc) +
                " WHERE " + Scope() + " AND id = " + D.Str(type.Id) + " RETURNING id;");
            DataTable table = await UniqueTransactionAsync(statements, "Ticket type " + type.Key, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<int> OrderTypesAsync(List<string> ids, DateTime nowUtc, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            if (ids.Count == 0) return 0;
            List<string> statements = new List<string>();
            for (int i = 0; i < ids.Count; i++)
                statements.Add("UPDATE tickettypes SET sortorder = " + D.Num((i + 1) * 10) + ", updatedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id = " + D.Str(ids[i]) + ";");
            statements.Add("SELECT COUNT(*) AS cnt FROM tickettypes WHERE " + Scope() + " AND id IN " + D.InList(ids) + ";");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? 0 : (int)RowReader.GetLong(table.Rows[0], "cnt");
        }

        public async Task<EnumerationResult<TicketType>> EnumerateTypesAsync(EnumerationRequest request, bool includeRetired, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            List<string> conditions = new List<string>();
            if (!includeRetired) conditions.Add("tt.isretired = " + D.Bool(false));
            string select = String.Join(", ", _TypeColumns.Split(", ").Select(c => "tt." + c));
            EnumerationResult<TicketType> page = await EnumerateAsync(request, select, "tickettypes tt", conditions, MapType, token).ConfigureAwait(false);
            await FillRecipientsAsync(page.Objects, token).ConfigureAwait(false);
            return page;
        }

        #endregion

        #region Tickets

        public async Task<Ticket> CreateTicketAsync(Ticket ticket, TicketMessage first, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ticket);
            ArgumentNullException.ThrowIfNull(first);
            RequireText(ticket.Id, nameof(ticket.Id));
            string columns = _TicketColumns.Replace("id, number, ", "id, ");
            List<string> statements = new List<string>
            {
                MessageInsert(first),
                "WITH n AS (INSERT INTO ticketcounters (propertyid, nextnumber) VALUES (" + Pid + ", 1) ON CONFLICT (propertyid) DO UPDATE SET nextnumber = ticketcounters.nextnumber + 1 RETURNING nextnumber) " +
                    "INSERT INTO tickets (propertyid, number, " + columns + ") SELECT " + Pid + ", n.nextnumber, " + TicketValues(ticket, false) + " FROM n RETURNING number;"
            };
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            ticket.Number = table.Rows.Count == 0 ? 0 : RowReader.GetLong(table.Rows[0], "number");
            return ticket;
        }

        public async Task<Ticket?> ReadTicketAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) return null;
            DataTable table = await QueryAsync("SELECT " + _TicketColumns + " FROM tickets WHERE " + Scope() + " AND id = " + D.Str(id) + ";", token).ConfigureAwait(false);
            return table.Rows.Count == 0 ? null : MapTicket(table.Rows[0]);
        }

        public Task<long> CountOpenForRequesterAsync(string requesterUserId, CancellationToken token = default)
        {
            RequireText(requesterUserId, nameof(requesterUserId));
            return CountAsync("SELECT COUNT(*) AS cnt FROM tickets WHERE " + Scope() + " AND requesteruserid = " + D.Str(requesterUserId) + " AND status <> " + D.Enum(TicketStatusEnum.Closed) + ";", token);
        }

        public Task<EnumerationResult<Ticket>> EnumerateTicketsAsync(EnumerationRequest request, TicketFilter filter, bool inbox, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(filter);
            List<string> conditions = new List<string>();
            if (filter.Statuses.Count > 0) conditions.Add("t.status IN " + D.InList(filter.Statuses.Select(s => s.ToString())));
            else if (filter.ExcludeClosed) conditions.Add("t.status <> " + D.Enum(TicketStatusEnum.Closed));
            if (!String.IsNullOrEmpty(filter.TypeKey)) conditions.Add("lower(t.typekey) = lower(" + D.Str(filter.TypeKey) + ")");
            if (filter.Priority != null) conditions.Add("t.priority = " + D.Enum(filter.Priority.Value));
            if (!String.IsNullOrEmpty(filter.AssigneeUserId)) conditions.Add("t.assigneeuserid = " + D.Str(filter.AssigneeUserId));
            if (filter.Unassigned == true) conditions.Add("t.assigneeuserid IS NULL");
            if (filter.Unassigned == false) conditions.Add("t.assigneeuserid IS NOT NULL");
            if (!String.IsNullOrEmpty(filter.RequesterUserId)) conditions.Add("t.requesteruserid = " + D.Str(filter.RequesterUserId));
            string overdue = "(t.firstresponseutc IS NULL AND t.status <> " + D.Enum(TicketStatusEnum.Closed) + " AND t.firstresponsedueutc IS NOT NULL AND t.firstresponsedueutc <= " + D.Ts(filter.NowUtc) + ")";
            if (filter.Overdue == true) conditions.Add(overdue);
            if (filter.Overdue == false) conditions.Add("NOT " + overdue);
            if (filter.HasIdea == true) conditions.Add("t.ideaid IS NOT NULL");
            if (filter.HasIdea == false) conditions.Add("t.ideaid IS NULL");
            string select = String.Join(", ", _TicketColumns.Split(", ").Select(c => "t." + c));
            string from = inbox ? "tickets t LEFT JOIN users ru ON ru.propertyid = " + Pid + " AND ru.id = t.requesteruserid" : "tickets t";
            return EnumerateAsync(request, select, from, conditions, MapTicket, token);
        }

        public async Task<bool> UpdateTicketAsync(Ticket ticket, List<TicketMessage> messages, TicketStatusEnum? expectedStatus, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ticket);
            ArgumentNullException.ThrowIfNull(messages);
            RequireText(ticket.Id, nameof(ticket.Id));
            string guard = expectedStatus == null ? String.Empty : " AND status = " + D.Enum(expectedStatus.Value);
            string update = "UPDATE tickets SET typekey = " + D.Str(ticket.TypeKey) + ", status = " + D.Enum(ticket.Status) + ", priority = " + D.Enum(ticket.Priority) +
                ", assigneeuserid = " + D.Str(ticket.AssigneeUserId) + ", ideaid = " + D.Str(ticket.IdeaId) + ", firstresponseutc = " + D.Ts(ticket.FirstResponseUtc) +
                ", overduenotifiedutc = " + D.Ts(ticket.OverdueNotifiedUtc) + ", lastactivityutc = " + D.Ts(ticket.LastActivityUtc) + ", lastmemberreplyutc = " + D.Ts(ticket.LastMemberReplyUtc) +
                ", resolvedutc = " + D.Ts(ticket.ResolvedUtc) + ", closedutc = " + D.Ts(ticket.ClosedUtc) + ", updatedutc = " + D.Ts(ticket.UpdatedUtc) +
                " WHERE " + Scope() + " AND id = " + D.Str(ticket.Id) + guard + " RETURNING id;";
            List<string> statements = new List<string>();
            if (messages.Count == 0)
            {
                DataTable single = await QueryAsync(update, token).ConfigureAwait(false);
                return single.Rows.Count > 0;
            }
            // The first message is inserted only when the guarded update matched (a CTE keeps both in one statement); the
            // others only when the first exists.
            for (int i = 0; i < messages.Count; i++)
            {
                TicketMessage message = messages[i];
                RequireText(message.Id, nameof(message.Id));
                if (i == 0)
                    statements.Add("WITH u AS (" + update.TrimEnd(';') + ") INSERT INTO ticketmessages (propertyid, " + _MessageColumns + ") SELECT " + Pid + ", " + MessageValues(message) + " FROM u;");
                else
                    statements.Add("INSERT INTO ticketmessages (propertyid, " + _MessageColumns + ") SELECT " + Pid + ", " + MessageValues(message) +
                        " WHERE EXISTS (SELECT 1 FROM ticketmessages x WHERE " + Scope("x") + " AND x.id = " + D.Str(messages[0].Id) + ");");
            }
            statements.Add("SELECT id FROM ticketmessages WHERE " + Scope() + " AND id = " + D.Str(messages[0].Id) + ";");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<bool> DeleteTicketAsync(string id, CancellationToken token = default)
        {
            RequireText(id, nameof(id));
            string messageIds = "SELECT id FROM ticketmessages WHERE " + Scope() + " AND ticketid = " + D.Str(id);
            List<string> statements = new List<string>();
            statements.AddRange(Driver.Metadata.DeleteWhereStatements(TargetTypeEnum.TicketMessage, messageIds));
            statements.AddRange(Driver.Metadata.DeleteStatements(TargetTypeEnum.Ticket, id));
            statements.Add("DELETE FROM ticketmessages WHERE " + Scope() + " AND ticketid = " + D.Str(id) + ";");
            statements.Add("DELETE FROM tickets WHERE " + Scope() + " AND id = " + D.Str(id) + " AND status = " + D.Enum(TicketStatusEnum.Closed) + " RETURNING id;");
            DataTable table = await TransactionAsync(statements, token).ConfigureAwait(false);
            return table.Rows.Count > 0;
        }

        public async Task<List<Ticket>> ListByRequesterAsync(string requesterUserId, string? afterId, int limit, CancellationToken token = default)
        {
            RequireText(requesterUserId, nameof(requesterUserId));
            DataTable table = await QueryAsync("SELECT " + _TicketColumns + " FROM tickets WHERE " + Scope() + " AND requesteruserid = " + D.Str(requesterUserId) +
                (String.IsNullOrEmpty(afterId) ? String.Empty : " AND id > " + D.Str(afterId)) + " ORDER BY id LIMIT " + D.Num(Math.Clamp(limit, 1, 1000)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapTicket).ToList();
        }

        public async Task<List<Ticket>> ClaimOverdueAsync(DateTime nowUtc, int limit, CancellationToken token = default)
        {
            string due = "firstresponseutc IS NULL AND status <> " + D.Enum(TicketStatusEnum.Closed) + " AND overduenotifiedutc IS NULL AND firstresponsedueutc IS NOT NULL AND firstresponsedueutc <= " + D.Ts(nowUtc);
            DataTable table = await QueryAsync("UPDATE tickets SET overduenotifiedutc = " + D.Ts(nowUtc) + " WHERE " + Scope() + " AND id IN (SELECT id FROM tickets WHERE " + Scope() + " AND " + due +
                " ORDER BY firstresponsedueutc, id LIMIT " + D.Num(Math.Clamp(limit, 1, 1000)) + ") AND " + due + " RETURNING " + _TicketColumns + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapTicket).ToList();
        }

        public async Task<List<Ticket>> ListAutoCloseAsync(DateTime cutoffUtc, int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT " + _TicketColumns + " FROM tickets WHERE " + Scope() + " AND status = " + D.Enum(TicketStatusEnum.Resolved) +
                " AND resolvedutc IS NOT NULL AND resolvedutc <= " + D.Ts(cutoffUtc) + " AND (lastmemberreplyutc IS NULL OR lastmemberreplyutc < resolvedutc) ORDER BY resolvedutc, id LIMIT " +
                D.Num(Math.Clamp(limit, 1, 1000)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapTicket).ToList();
        }

        #endregion

        #region Messages

        public Task<EnumerationResult<TicketMessage>> EnumerateMessagesAsync(EnumerationRequest request, string ticketId, bool staff, TicketMessageKindEnum? kind, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            RequireText(ticketId, nameof(ticketId));
            List<string> conditions = new List<string> { "m.ticketid = " + D.Str(ticketId) };
            if (!staff) conditions.Add("m.kind = " + D.Enum(TicketMessageKindEnum.Reply));
            else if (kind != null) conditions.Add("m.kind = " + D.Enum(kind.Value));
            string select = String.Join(", ", _MessageColumns.Split(", ").Select(c => "m." + c));
            return EnumerateAsync(request, select, "ticketmessages m", conditions, MapMessage, token);
        }

        public async Task<List<TicketMessage>> LatestMessagesAsync(string ticketId, bool staff, int limit, CancellationToken token = default)
        {
            RequireText(ticketId, nameof(ticketId));
            DataTable table = await QueryAsync("SELECT " + _MessageColumns + " FROM ticketmessages WHERE " + Scope() + " AND ticketid = " + D.Str(ticketId) + KindCondition(staff) +
                " ORDER BY createdutc DESC, id DESC LIMIT " + D.Num(Math.Clamp(limit, 1, 100)) + ";", token).ConfigureAwait(false);
            List<TicketMessage> messages = table.Rows.Cast<DataRow>().Select(MapMessage).ToList();
            messages.Reverse();
            return messages;
        }

        public Task<long> CountMessagesAsync(string ticketId, bool staff, CancellationToken token = default)
        {
            RequireText(ticketId, nameof(ticketId));
            return CountAsync("SELECT COUNT(*) AS cnt FROM ticketmessages WHERE " + Scope() + " AND ticketid = " + D.Str(ticketId) + KindCondition(staff) + ";", token);
        }

        public async Task<List<TicketMessage>> RepliesForTicketsAsync(IEnumerable<string> ticketIds, int limit, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ticketIds);
            List<string> ids = ticketIds.Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return new List<TicketMessage>();
            DataTable table = await QueryAsync("SELECT " + _MessageColumns + " FROM ticketmessages WHERE " + Scope() + " AND ticketid IN " + D.InList(ids) + KindCondition(false) +
                " ORDER BY ticketid, createdutc, id LIMIT " + D.Num(Math.Clamp(limit, 1, 100000)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(MapMessage).ToList();
        }

        public async Task<List<string>> MessageIdsAsync(string ticketId, int limit, CancellationToken token = default)
        {
            RequireText(ticketId, nameof(ticketId));
            DataTable table = await QueryAsync("SELECT id FROM ticketmessages WHERE " + Scope() + " AND ticketid = " + D.Str(ticketId) + " ORDER BY id LIMIT " + D.Num(Math.Clamp(limit, 1, 100000)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => RowReader.GetString(r, "id")).ToList();
        }

        #endregion

        #region Metrics

        public Task<List<TicketCount>> OpenedByDayAsync(DateTime sinceUtc, CancellationToken token = default)
        {
            return ByDayAsync("createdutc", sinceUtc, token);
        }

        public Task<List<TicketCount>> ResolvedByDayAsync(DateTime sinceUtc, CancellationToken token = default)
        {
            return ByDayAsync("resolvedutc", sinceUtc, token);
        }

        public async Task<double?> FirstResponseMedianHoursAsync(DateTime sinceUtc, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT percentile_cont(0.5) WITHIN GROUP (ORDER BY EXTRACT(EPOCH FROM (firstresponseutc - createdutc)) / 3600.0) AS med FROM tickets WHERE " + Scope() +
                " AND firstresponseutc IS NOT NULL AND createdutc >= " + D.Ts(sinceUtc) + ";", token).ConfigureAwait(false);
            if (table.Rows.Count == 0 || table.Rows[0]["med"] == DBNull.Value) return null;
            return Math.Round(Convert.ToDouble(table.Rows[0]["med"], CultureInfo.InvariantCulture), 2);
        }

        public async Task<List<TicketCount>> ByTypeAsync(DateTime sinceUtc, int limit, CancellationToken token = default)
        {
            DataTable table = await QueryAsync("SELECT typekey AS k, COUNT(*) AS cnt FROM tickets WHERE " + Scope() + " AND createdutc >= " + D.Ts(sinceUtc) +
                " GROUP BY typekey ORDER BY cnt DESC, typekey LIMIT " + D.Num(Math.Clamp(limit, 1, 1000)) + ";", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => new TicketCount { Key = RowReader.GetString(r, "k"), Count = RowReader.GetLong(r, "cnt") }).ToList();
        }

        public Task<long> CountOpenAsync(CancellationToken token = default)
        {
            return CountAsync("SELECT COUNT(*) AS cnt FROM tickets WHERE " + Scope() + " AND status <> " + D.Enum(TicketStatusEnum.Closed) + ";", token);
        }

        public Task<long> CountOverdueAsync(DateTime nowUtc, CancellationToken token = default)
        {
            return CountAsync("SELECT COUNT(*) AS cnt FROM tickets WHERE " + Scope() + " AND firstresponseutc IS NULL AND status <> " + D.Enum(TicketStatusEnum.Closed) +
                " AND firstresponsedueutc IS NOT NULL AND firstresponsedueutc <= " + D.Ts(nowUtc) + ";", token);
        }

        #endregion

        #region Private-Methods

        private async Task<List<TicketCount>> ByDayAsync(string column, DateTime sinceUtc, CancellationToken token)
        {
            DataTable table = await QueryAsync("SELECT to_char(" + column + " AT TIME ZONE 'UTC', 'YYYY-MM-DD') AS k, COUNT(*) AS cnt FROM tickets WHERE " + Scope() + " AND " + column +
                " IS NOT NULL AND " + column + " >= " + D.Ts(sinceUtc) + " GROUP BY k ORDER BY k;", token).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => new TicketCount { Key = RowReader.GetString(r, "k"), Count = RowReader.GetLong(r, "cnt") }).ToList();
        }

        private string KindCondition(bool staff)
        {
            return staff ? String.Empty : " AND kind = " + D.Enum(TicketMessageKindEnum.Reply);
        }

        private async Task FillRecipientsAsync(List<TicketType> types, CancellationToken token)
        {
            if (types.Count == 0) return;
            DataTable table = await QueryAsync("SELECT tickettypeid, email FROM tickettyperecipients WHERE " + Scope() + " AND tickettypeid IN " + D.InList(types.Select(t => t.Id)) +
                " ORDER BY tickettypeid, email;", token).ConfigureAwait(false);
            Dictionary<string, List<string>> byType = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (DataRow row in table.Rows)
            {
                string id = RowReader.GetString(row, "tickettypeid");
                if (!byType.TryGetValue(id, out List<string>? list))
                {
                    list = new List<string>();
                    byType[id] = list;
                }
                list.Add(RowReader.GetString(row, "email"));
            }
            foreach (TicketType type in types) type.StaffRecipients = byType.TryGetValue(type.Id, out List<string>? emails) ? emails : new List<string>();
        }

        private List<string> RecipientStatements(TicketType type)
        {
            return type.StaffRecipients.Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(email => "INSERT INTO tickettyperecipients (propertyid, tickettypeid, email) VALUES (" + Pid + ", " + D.Str(type.Id) + ", " + D.Str(email) + ") ON CONFLICT DO NOTHING;")
                .ToList();
        }

        private string TypeValues(TicketType t)
        {
            return String.Join(", ", new[]
            {
                D.Str(t.Id), D.Str(t.Key), D.Str(t.Label), D.Str(t.Description), D.Str(t.HelpText), D.Num(t.SortOrder), D.Bool(t.IsRetired), D.Ts(t.CreatedUtc), D.Ts(t.UpdatedUtc)
            });
        }

        private string TicketValues(Ticket t, bool includeNumber)
        {
            List<string> values = new List<string> { D.Str(t.Id) };
            if (includeNumber) values.Add(D.Num(t.Number));
            values.AddRange(new[]
            {
                D.Str(t.TypeKey), D.Str(t.Title), D.Str(t.Body), D.Enum(t.Status), D.Enum(t.Priority), D.Str(t.RequesterUserId), D.Str(t.AssigneeUserId), D.Str(t.IdeaId),
                D.Bool(t.IncludeDiagnostics), D.Str(t.DeviceClient), D.Str(t.DeviceClientVersion), D.Str(t.DevicePlatform), D.Str(t.DeviceOsVersion), D.Str(t.DeviceModel),
                D.Str(t.DeviceUpdateId), D.Str(t.DeviceLocale), D.Str(t.DeviceTimeZone), D.Bool(t.MembershipActive), D.Str(t.MembershipSource), D.Str(t.MembershipStore),
                D.Ts(t.MembershipExpiresUtc), D.Ts(t.FirstResponseDueUtc), D.Ts(t.FirstResponseUtc), D.Ts(t.OverdueNotifiedUtc), D.Ts(t.LastActivityUtc), D.Ts(t.LastMemberReplyUtc),
                D.Ts(t.ResolvedUtc), D.Ts(t.ClosedUtc), D.Ts(t.CreatedUtc), D.Ts(t.UpdatedUtc)
            });
            return String.Join(", ", values);
        }

        private string MessageInsert(TicketMessage m)
        {
            RequireText(m.Id, nameof(m.Id));
            return "INSERT INTO ticketmessages (propertyid, " + _MessageColumns + ") VALUES (" + Pid + ", " + MessageValues(m) + ");";
        }

        private string MessageValues(TicketMessage m)
        {
            return String.Join(", ", new[]
            {
                D.Str(m.Id), D.Str(m.TicketId), D.Enum(m.Kind), D.Str(m.AuthorUserId), D.Enum(m.AuthorRole), D.Str(m.Body), D.Enum(m.StatusChange), D.Ts(m.CreatedUtc)
            });
        }

        private static TicketType MapType(DataRow row)
        {
            return new TicketType
            {
                Id = RowReader.GetString(row, "id"),
                Key = RowReader.GetString(row, "key"),
                Label = RowReader.GetString(row, "label"),
                Description = RowReader.GetNullableString(row, "description"),
                HelpText = RowReader.GetNullableString(row, "helptext"),
                SortOrder = RowReader.GetInt(row, "sortorder"),
                IsRetired = RowReader.GetBool(row, "isretired"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        private static Ticket MapTicket(DataRow row)
        {
            return new Ticket
            {
                Id = RowReader.GetString(row, "id"),
                Number = RowReader.GetLong(row, "number"),
                TypeKey = RowReader.GetString(row, "typekey"),
                Title = RowReader.GetString(row, "title"),
                Body = RowReader.GetString(row, "body"),
                Status = RowReader.GetEnum(row, "status", TicketStatusEnum.Open),
                Priority = RowReader.GetEnum(row, "priority", TicketPriorityEnum.Normal),
                RequesterUserId = RowReader.GetNullableString(row, "requesteruserid"),
                AssigneeUserId = RowReader.GetNullableString(row, "assigneeuserid"),
                IdeaId = RowReader.GetNullableString(row, "ideaid"),
                IncludeDiagnostics = RowReader.GetBool(row, "includediagnostics"),
                DeviceClient = RowReader.GetNullableString(row, "deviceclient"),
                DeviceClientVersion = RowReader.GetNullableString(row, "deviceclientversion"),
                DevicePlatform = RowReader.GetNullableString(row, "deviceplatform"),
                DeviceOsVersion = RowReader.GetNullableString(row, "deviceosversion"),
                DeviceModel = RowReader.GetNullableString(row, "devicemodel"),
                DeviceUpdateId = RowReader.GetNullableString(row, "deviceupdateid"),
                DeviceLocale = RowReader.GetNullableString(row, "devicelocale"),
                DeviceTimeZone = RowReader.GetNullableString(row, "devicetimezone"),
                MembershipActive = RowReader.GetBool(row, "membershipactive"),
                MembershipSource = RowReader.GetNullableString(row, "membershipsource"),
                MembershipStore = RowReader.GetNullableString(row, "membershipstore"),
                MembershipExpiresUtc = RowReader.GetNullableDateTime(row, "membershipexpiresutc"),
                FirstResponseDueUtc = RowReader.GetNullableDateTime(row, "firstresponsedueutc"),
                FirstResponseUtc = RowReader.GetNullableDateTime(row, "firstresponseutc"),
                OverdueNotifiedUtc = RowReader.GetNullableDateTime(row, "overduenotifiedutc"),
                LastActivityUtc = RowReader.GetDateTime(row, "lastactivityutc"),
                LastMemberReplyUtc = RowReader.GetNullableDateTime(row, "lastmemberreplyutc"),
                ResolvedUtc = RowReader.GetNullableDateTime(row, "resolvedutc"),
                ClosedUtc = RowReader.GetNullableDateTime(row, "closedutc"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc"),
                UpdatedUtc = RowReader.GetDateTime(row, "updatedutc")
            };
        }

        private static TicketMessage MapMessage(DataRow row)
        {
            return new TicketMessage
            {
                Id = RowReader.GetString(row, "id"),
                TicketId = RowReader.GetString(row, "ticketid"),
                Kind = RowReader.GetEnum(row, "kind", TicketMessageKindEnum.Reply),
                AuthorUserId = RowReader.GetNullableString(row, "authoruserid"),
                AuthorRole = RowReader.GetEnum(row, "authorrole", TicketAuthorRoleEnum.Member),
                Body = RowReader.GetString(row, "body"),
                StatusChange = RowReader.GetNullableEnum<TicketStatusEnum>(row, "statuschange"),
                CreatedUtc = RowReader.GetDateTime(row, "createdutc")
            };
        }

        #endregion
    }
}
