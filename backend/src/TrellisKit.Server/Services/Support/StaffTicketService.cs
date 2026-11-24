namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Staff ticket routes for SystemAdmin and SupportAgent (docs/PHASE5_CONTRACT.md section 6.2, as-built note C): the
    /// inbox (search over number, title, body, requester name and email with PostgreSQL text matching on the property's
    /// own rows; tickets are never in global search), detail with notes and membership, replies and internal notes,
    /// updates (audited ticket.update, with a staff timeline line per change), the metadata CSV export, support metrics,
    /// and deletion of Closed tickets (audited ticket.delete).
    /// </summary>
    public class StaffTicketService
    {
        #region Public-Members

        /// <summary>
        /// Most messages purged with a deleted ticket.
        /// </summary>
        public const int MaxMessagesPerDelete = 10000;

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;
        private readonly SupportModule _Module;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <param name="module">Support module.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public StaffTicketService(ApplicationServices services, SupportModule module)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            _Module = module ?? throw new ArgumentNullException(nameof(module));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// POST /api/v1/admin/tickets/enumerate.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of AdminTicketSummary.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 403 Forbidden, 404 NotAvailable.</exception>
        public async Task<EnumerationResult<TicketView>> InboxAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            Require(actor);
            EnumerationQuery q = query ?? new EnumerationQuery();
            EnumerationResult<Ticket> page = await PageAsync(q, "admin-tickets", token).ConfigureAwait(false);
            return page.With(await _Module.Views.TicketsAsync(page.Objects, actor, true, token).ConfigureAwait(false));
        }

        /// <summary>
        /// GET /api/v1/admin/tickets/{id}: the ticket with every message kind, the membership snapshot, and the requester's
        /// membership now.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Ticket identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Ticket.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound or NotAvailable.</exception>
        public async Task<TicketView> GetAsync(ContentActor actor, string id, CancellationToken token = default)
        {
            Require(actor);
            Ticket ticket = await RequireAsync(id, token).ConfigureAwait(false);
            return await _Module.Views.DetailAsync(ticket, actor, true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/tickets/{id}/messages/enumerate: every message (replies, notes, and system lines); filter kind.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Ticket identifier.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound or NotAvailable.</exception>
        public async Task<EnumerationResult<TicketMessageView>> MessagesAsync(ContentActor actor, string id, EnumerationQuery? query, CancellationToken token = default)
        {
            Require(actor);
            Ticket ticket = await RequireAsync(id, token).ConfigureAwait(false);
            EnumerationQuery q = query ?? new EnumerationQuery();
            Dictionary<string, string> errors = new Dictionary<string, string>();
            TicketMessageKindEnum? kind = q.FilterEnum<TicketMessageKindEnum>("kind", errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, SupportCollections.Messages, true, "admin-ticket-messages|" + ticket.Id);
            EnumerationResult<TicketMessage> page = await _Services.Database.Support.EnumerateMessagesAsync(request, ticket.Id, true, kind, token).ConfigureAwait(false);
            return page.With(await _Module.Views.MessagesAsync(page.Objects, actor, true, token).ConfigureAwait(false));
        }

        /// <summary>
        /// POST /api/v1/admin/tickets/{id}/messages: a Reply (visible to the requester; sets WaitingOnMember unless setStatus
        /// says otherwise; notifies and emails the requester and publishes ticket.updated) or a Note (staff only; changes
        /// nothing and is never returned to members or emailed).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information (audit).</param>
        /// <param name="id">Ticket identifier.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The message.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound or NotAvailable, 409 MediaNotAttachable.</exception>
        public async Task<TicketMessageView> PostAsync(ContentActor actor, RequestInfo info, string id, StaffTicketMessageRequest request, CancellationToken token = default)
        {
            Require(actor);
            ArgumentNullException.ThrowIfNull(request);
            Ticket ticket = await RequireAsync(id, token).ConfigureAwait(false);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            TicketMessageKindEnum kind = TicketMessageKindEnum.Reply;
            if (!String.IsNullOrWhiteSpace(request.Kind))
            {
                if (String.Equals(request.Kind.Trim(), "Note", StringComparison.OrdinalIgnoreCase)) kind = TicketMessageKindEnum.Note;
                else if (!String.Equals(request.Kind.Trim(), "Reply", StringComparison.OrdinalIgnoreCase)) errors["kind"] = "Use Reply or Note.";
            }
            TicketStatusEnum? setStatus = null;
            if (!String.IsNullOrWhiteSpace(request.SetStatus))
            {
                if (ModerationService.TryParseEnum(request.SetStatus, out TicketStatusEnum parsed)) setStatus = parsed;
                else errors["setStatus"] = "Use " + String.Join(", ", Enum.GetNames<TicketStatusEnum>()) + ".";
            }
            string body = MarkdownNormalizer.Normalize(request.Body ?? String.Empty);
            int length = MarkdownNormalizer.Length(body);
            if (length < 1 || length > _Services.Settings.Support.MaxBodyLength) errors["body"] = "1 to " + _Services.Settings.Support.MaxBodyLength + " characters.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            List<string> mediaIds = request.MediaIds ?? new List<string>();
            if (mediaIds.Count > 0) actor.RequireUserId();
            await _Services.Media.ValidatePrivateAttachableAsync(actor, mediaIds, MediaPurposeEnum.Ticket, ticket.Id, _Services.Settings.Support.MaxAttachments, _Services.Settings.Support.AllowVideo ? 1 : 0, token).ConfigureAwait(false);

            DateTime now = _Module.Clock();
            TicketMessage message = new TicketMessage
            {
                Id = IdGenerator.Generate(SupportIdPrefixes.TicketMessage),
                TicketId = ticket.Id,
                Kind = kind,
                AuthorUserId = String.IsNullOrEmpty(actor.UserId) ? null : actor.UserId,
                AuthorRole = TicketAuthorRoleEnum.Staff,
                Body = body,
                CreatedUtc = now
            };
            List<TicketMessage> lines = new List<TicketMessage> { message };
            TicketStatusEnum previous = ticket.Status;
            bool statusChanged = false;
            if (kind == TicketMessageKindEnum.Reply)
            {
                TicketStatusEnum next = setStatus ?? TicketStatusEnum.WaitingOnMember;
                if (next != previous)
                {
                    SetStatus(ticket, next, now);
                    statusChanged = true;
                    lines.Add(TicketService.SystemLine(ticket, StaffName(actor) + " replied and changed the status from " + StatusText(previous) + " to " + StatusText(next) + ".", next, now.AddTicks(1)));
                }
                if (ticket.FirstResponseUtc == null)
                {
                    ticket.FirstResponseUtc = now;
                    SupportTelemetry.RecordFirstResponse((now - ticket.CreatedUtc).TotalHours);
                }
                ticket.LastActivityUtc = now;
            }
            ticket.UpdatedUtc = now;
            if (!await _Services.Database.Support.UpdateTicketAsync(ticket, lines, previous, token).ConfigureAwait(false))
                throw new ApiErrorException(409, "Conflict", "The ticket changed while you were writing; reload and try again.");
            if (mediaIds.Count > 0) await _Services.Media.AttachPrivateAsync(MediaPurposeEnum.Ticket, ticket.Id, TargetTypeEnum.TicketMessage, message.Id, mediaIds, token).ConfigureAwait(false);
            SupportTelemetry.Count(kind == TicketMessageKindEnum.Note ? "note" : "reply", ticket.TypeKey, "staff");
            if (statusChanged)
                await _Services.Audit.WriteAsync(info, "ticket.update", TargetTypeEnum.Ticket.ToString(), ticket.Id, "status " + previous + " to " + ticket.Status, token).ConfigureAwait(false);
            TicketMessageView view = (await _Module.Views.MessagesAsync(new List<TicketMessage> { message }, actor, true, token).ConfigureAwait(false))[0];
            if (kind == TicketMessageKindEnum.Reply)
            {
                string agent = _Services.Settings.Support.ShowAgentNames && !String.IsNullOrEmpty(actor.User?.DisplayName) ? actor.User!.DisplayName + ", " + _Module.TeamName() : _Module.TeamName();
                await _Module.Notifier.StaffRepliedAsync(ticket, message, agent, token).ConfigureAwait(false);
            }
            return view;
        }

        /// <summary>
        /// PUT /api/v1/admin/tickets/{id}: status, priority, type, assigneeUserId (a SupportAgent or SystemAdmin; null
        /// unassigns), labels, tags. Each change writes a staff timeline line and the audit record ticket.update; members are
        /// told about status changes only (TicketUpdate, ticket_resolved for Resolved and Closed, ticket.updated).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information (audit).</param>
        /// <param name="id">Ticket identifier.</param>
        /// <param name="request">Body.</param>
        /// <param name="present">Present fields.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Ticket.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound or NotAvailable, 409 Conflict.</exception>
        public async Task<TicketView> UpdateAsync(ContentActor actor, RequestInfo info, string id, TicketUpdateRequest request, HashSet<string> present, CancellationToken token = default)
        {
            Require(actor);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(present);
            Ticket ticket = await RequireAsync(id, token).ConfigureAwait(false);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            DateTime now = _Module.Clock();
            TicketStatusEnum previous = ticket.Status;
            List<TicketMessage> lines = new List<TicketMessage>();
            List<string> changes = new List<string>();
            string who = StaffName(actor);
            int tick = 0;

            if (present.Contains("status") && request.Status != null)
            {
                if (!ModerationService.TryParseEnum(request.Status, out TicketStatusEnum status)) errors["status"] = "Use " + String.Join(", ", Enum.GetNames<TicketStatusEnum>()) + ".";
                else if (status != ticket.Status)
                {
                    changes.Add("status " + ticket.Status + " to " + status);
                    lines.Add(TicketService.SystemLine(ticket, who + " changed the status from " + StatusText(ticket.Status) + " to " + StatusText(status) + ".", status, now.AddTicks(tick++)));
                    SetStatus(ticket, status, now);
                }
            }
            if (present.Contains("priority") && request.Priority != null)
            {
                if (!ModerationService.TryParseEnum(request.Priority, out TicketPriorityEnum priority)) errors["priority"] = "Use " + String.Join(", ", Enum.GetNames<TicketPriorityEnum>()) + ".";
                else if (priority != ticket.Priority)
                {
                    changes.Add("priority " + ticket.Priority + " to " + priority);
                    lines.Add(TicketService.SystemLine(ticket, who + " changed the priority from " + ticket.Priority + " to " + priority + ".", null, now.AddTicks(tick++)));
                    ticket.Priority = priority;
                }
            }
            if (present.Contains("type") && request.Type != null)
            {
                TicketType? type = await _Module.Types.FindAsync(request.Type, token).ConfigureAwait(false);
                if (type == null || (type.IsRetired && !String.Equals(type.Key, ticket.TypeKey, StringComparison.OrdinalIgnoreCase))) errors["type"] = "Unknown ticket type.";
                else if (!String.Equals(type.Key, ticket.TypeKey, StringComparison.Ordinal))
                {
                    changes.Add("type " + ticket.TypeKey + " to " + type.Key);
                    lines.Add(TicketService.SystemLine(ticket, who + " changed the type from " + ticket.TypeKey + " to " + type.Key + ".", null, now.AddTicks(tick++)));
                    ticket.TypeKey = type.Key;
                }
            }
            if (present.Contains("assigneeUserId"))
            {
                string? assignee = String.IsNullOrWhiteSpace(request.AssigneeUserId) ? null : request.AssigneeUserId.Trim();
                if (assignee != null && !await _Module.IsStaffUserAsync(assignee, token).ConfigureAwait(false)) errors["assigneeUserId"] = "The assignee must be a SupportAgent or SystemAdmin.";
                else if (!String.Equals(assignee, ticket.AssigneeUserId, StringComparison.Ordinal))
                {
                    UserSummary summary = await _Services.Summaries.BuildOneAsync(assignee, token).ConfigureAwait(false);
                    changes.Add(assignee == null ? "unassigned" : "assigned " + assignee);
                    lines.Add(TicketService.SystemLine(ticket, assignee == null ? who + " unassigned the ticket." : who + " assigned the ticket to " + summary.DisplayName + ".", null, now.AddTicks(tick++)));
                    ticket.AssigneeUserId = assignee;
                    SupportTelemetry.Count("assign", ticket.TypeKey, "staff");
                }
            }
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, true, token, TargetTypeEnum.Ticket).ConfigureAwait(false);
            if (lines.Count > 0)
            {
                ticket.UpdatedUtc = now;
                if (!await _Services.Database.Support.UpdateTicketAsync(ticket, lines, previous, token).ConfigureAwait(false))
                    throw new ApiErrorException(409, "Conflict", "The ticket changed while you were editing; reload and try again.");
                await _Services.Audit.WriteAsync(info, "ticket.update", TargetTypeEnum.Ticket.ToString(), ticket.Id, String.Join("; ", changes), token).ConfigureAwait(false);
            }
            if (request.Labels != null || request.Tags != null) await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.Ticket, ticket.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            if (ticket.Status != previous)
            {
                SupportTelemetry.Count("status", ticket.TypeKey, "staff");
                await _Module.Notifier.StatusChangedAsync(ticket, actor.UserId, true, token).ConfigureAwait(false);
            }
            return await _Module.Views.DetailAsync(ticket, actor, true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// GET /api/v1/admin/tickets/export.csv: one bounded page of ticket metadata (never bodies or messages) with the
        /// inbox filters; X-Continuation-Token names the next page.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>CSV page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 403 Forbidden, 404 NotAvailable.</exception>
        public async Task<CsvPage> ExportCsvAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            Require(actor);
            EnumerationQuery q = query ?? new EnumerationQuery();
            if (q.MaxResults == null) q.MaxResults = _Services.Settings.Enumeration.MaxResultsAdmin;
            EnumerationResult<Ticket> page = await PageAsync(q, "admin-tickets-csv", token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.Ticket, page.Objects.Select(t => t.Id), true, token).ConfigureAwait(false);
            DateTime now = _Module.Clock();
            CsvWriter csv = new CsvWriter().Row("id", "number", "type", "status", "priority", "title", "requesterUserId", "assigneeUserId", "ideaId", "isOverdue", "firstResponseDueUtc",
                "firstResponseUtc", "lastActivityUtc", "resolvedUtc", "closedUtc", "createdUtc", "labels", "tags");
            foreach (Ticket t in page.Objects)
            {
                ObjectMetadata? meta = metadata.TryGetValue(t.Id, out ObjectMetadata? found) ? found : null;
                csv.Row(t.Id, t.Number, t.TypeKey, t.Status, t.Priority, t.Title, t.RequesterUserId, t.AssigneeUserId, t.IdeaId, TicketViewBuilder.IsOverdue(t, now), t.FirstResponseDueUtc,
                    t.FirstResponseUtc, t.LastActivityUtc, t.ResolvedUtc, t.ClosedUtc, t.CreatedUtc,
                    String.Join("; ", meta?.Labels ?? new List<string>()), String.Join("; ", (meta?.Tags ?? new Dictionary<string, string>()).Select(p => p.Key + "=" + p.Value)));
            }
            return new CsvPage { Csv = csv.ToString(), ContinuationToken = page.ContinuationToken, TotalRecords = page.TotalRecords };
        }

        /// <summary>
        /// GET /api/v1/admin/metrics/support?days=30 (1 to 365): opened and resolved per day, the first response median,
        /// overdue and open now, and tickets per type (at most 20).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="days">Window in days, or null for 30.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Metrics.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotAvailable.</exception>
        public async Task<SupportMetrics> MetricsAsync(ContentActor actor, int? days, CancellationToken token = default)
        {
            Require(actor);
            int window = days ?? 30;
            if (window < 1 || window > 365) throw ApiErrorException.Field("days", "1 to 365.");
            DateTime now = _Module.Clock();
            DateTime since = now.Date.AddDays(-(window - 1));
            List<TicketCount> opened = await _Services.Database.Support.OpenedByDayAsync(since, token).ConfigureAwait(false);
            List<TicketCount> resolved = await _Services.Database.Support.ResolvedByDayAsync(since, token).ConfigureAwait(false);
            List<TicketCount> byType = await _Services.Database.Support.ByTypeAsync(since, TicketTypeService.MaxTypes, token).ConfigureAwait(false);
            Dictionary<string, string> labels = (await _Services.Database.Support.ListTypesAsync(true, TicketTypeService.MaxTypes, token).ConfigureAwait(false))
                .GroupBy(t => t.Key.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First().Label, StringComparer.Ordinal);
            return new SupportMetrics
            {
                Days = window,
                OpenedByDay = Days(opened, since, window),
                ResolvedByDay = Days(resolved, since, window),
                FirstResponseMedianHours = await _Services.Database.Support.FirstResponseMedianHoursAsync(since, token).ConfigureAwait(false),
                OverdueNow = await _Services.Database.Support.CountOverdueAsync(now, token).ConfigureAwait(false),
                OpenNow = await _Services.Database.Support.CountOpenAsync(token).ConfigureAwait(false),
                ByType = new SupportTypePreview
                {
                    Objects = byType.Take(20).Select(c => new SupportTypeCount { Key = c.Key, Label = labels.TryGetValue(c.Key.ToLowerInvariant(), out string? label) ? label : c.Key, Count = c.Count }).ToList(),
                    TotalRecords = byType.Count,
                    Enumerate = TrellisKit.Core.Constants.ApiPrefix + "/admin/support/ticket-types/enumerate"
                }
            };
        }

        /// <summary>
        /// DELETE /api/v1/admin/tickets/{id}: only Closed tickets (409 TicketNotClosed otherwise); deletes the ticket, its
        /// messages, their attachments (every stored object), and the labels and tags. Audited ticket.delete.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information (audit).</param>
        /// <param name="id">Ticket identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound or NotAvailable, 409 TicketNotClosed.</exception>
        public async Task DeleteAsync(ContentActor actor, RequestInfo info, string id, CancellationToken token = default)
        {
            Require(actor);
            Ticket ticket = await RequireAsync(id, token).ConfigureAwait(false);
            if (ticket.Status != TicketStatusEnum.Closed) throw new ApiErrorException(409, "TicketNotClosed", "Close the ticket before deleting it.");
            List<string> messageIds = await _Services.Database.Support.MessageIdsAsync(ticket.Id, MaxMessagesPerDelete, token).ConfigureAwait(false);
            foreach (string messageId in messageIds)
            {
                token.ThrowIfCancellationRequested();
                await _Services.Media.PurgeTargetAsync(TargetTypeEnum.TicketMessage, messageId, token).ConfigureAwait(false);
            }
            if (!await _Services.Database.Support.DeleteTicketAsync(ticket.Id, token).ConfigureAwait(false)) throw new ApiErrorException(409, "TicketNotClosed", "Close the ticket before deleting it.");
            await _Services.Audit.WriteAsync(info, "ticket.delete", TargetTypeEnum.Ticket.ToString(), ticket.Id, "#" + ticket.Number.ToString(CultureInfo.InvariantCulture) + ", " + messageIds.Count + " messages", token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static string StatusText(TicketStatusEnum status)
        {
            switch (status)
            {
                case TicketStatusEnum.InProgress: return "In progress";
                case TicketStatusEnum.WaitingOnMember: return "Waiting on member";
                default: return status.ToString();
            }
        }

        private void Require(ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Module.RequireEnabled();
            SupportModule.RequireStaff(actor);
        }

        private async Task<Ticket> RequireAsync(string id, CancellationToken token)
        {
            Ticket? ticket = await _Services.Database.Support.ReadTicketAsync((id ?? String.Empty).Trim(), token).ConfigureAwait(false);
            if (ticket == null) throw ApiErrorException.NotFound("Ticket");
            return ticket;
        }

        private async Task<EnumerationResult<Ticket>> PageAsync(EnumerationQuery query, string scope, CancellationToken token)
        {
            TicketFilter filter = TicketService.ParseFilter(query, true, _Module.Clock());
            EnumerationRequest request = _Services.Enumeration.Prepare(query, SupportCollections.Inbox, true, scope);
            return await _Services.Database.Support.EnumerateTicketsAsync(request, filter, true, token).ConfigureAwait(false);
        }

        private static void SetStatus(Ticket ticket, TicketStatusEnum status, DateTime now)
        {
            ticket.Status = status;
            if (status == TicketStatusEnum.Resolved) ticket.ResolvedUtc = now;
            if (status == TicketStatusEnum.Closed)
            {
                ticket.ClosedUtc = now;
                if (ticket.ResolvedUtc == null) ticket.ResolvedUtc = now;
            }
            if (status == TicketStatusEnum.Open || status == TicketStatusEnum.InProgress || status == TicketStatusEnum.WaitingOnMember)
            {
                ticket.ClosedUtc = null;
                if (status != TicketStatusEnum.WaitingOnMember) ticket.ResolvedUtc = null;
            }
            ticket.LastActivityUtc = now;
        }

        private static string StaffName(ContentActor actor)
        {
            return String.IsNullOrEmpty(actor.User?.DisplayName) ? "Support staff" : actor.User!.DisplayName;
        }

        private static List<DailyCount> Days(List<TicketCount> counts, DateTime since, int days)
        {
            Dictionary<string, long> byDay = counts.ToDictionary(c => c.Key, c => c.Count, StringComparer.Ordinal);
            List<DailyCount> result = new List<DailyCount>();
            for (int i = 0; i < days; i++)
            {
                string day = since.AddDays(i).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                result.Add(new DailyCount { Date = day, Count = byDay.TryGetValue(day, out long n) ? n : 0 });
            }
            return result;
        }

        #endregion
    }
}
