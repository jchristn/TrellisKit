namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text.Json;
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
    /// Member ticket routes (docs/PHASE5_CONTRACT.md section 6.1): open, list own, read own (notes excluded; other tickets
    /// are 404), list replies, reply, close, and reopen. Support is an account feature reachable by every signed-in account
    /// in every access model. Rate limits support.ticketsPerDay and support.repliesPerHour (429 with Retry-After).
    /// </summary>
    public class TicketService
    {
        #region Public-Members

        /// <summary>
        /// Shortest title.
        /// </summary>
        public const int MinTitleLength = 3;

        /// <summary>
        /// Longest title.
        /// </summary>
        public const int MaxTitleLength = 140;

        /// <summary>
        /// Shortest opening body.
        /// </summary>
        public const int MinBodyLength = 10;

        /// <summary>
        /// Longest stored diagnostics header value.
        /// </summary>
        public const int MaxHeaderLength = 200;

        #endregion

        #region Private-Members

        private const string _TicketsBucket = "support.tickets";
        private const string _RepliesBucket = "support.replies";
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
        public TicketService(ApplicationServices services, SupportModule module)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            _Module = module ?? throw new ArgumentNullException(nameof(module));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// POST /api/v1/tickets.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="request">Body.</param>
        /// <param name="client">Diagnostics headers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The ticket.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or ContentFiltered, 404 NotAvailable, 409 TicketLimitReached or MediaNotAttachable, 429 RateLimited.</exception>
        public async Task<TicketView> CreateAsync(ContentActor actor, TicketCreateRequest request, TicketClientInfo client, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(client);
            _Module.RequireEnabled();
            string userId = actor.RequireUserId();
            using (Activity? activity = TrellisKitTelemetry.StartSpan("support.ticket.create"))
            {
                _Services.Limits.CheckMember(actor, _TicketsBucket, _Services.Settings.Support.TicketsPerDay, RateLimitService.Day);
                Dictionary<string, string> errors = new Dictionary<string, string>();
                TicketType? type = await _Module.Types.FindAsync(request.Type, token).ConfigureAwait(false);
                if (type == null || type.IsRetired) errors["type"] = String.IsNullOrWhiteSpace(request.Type) ? "Required." : "Unknown ticket type.";
                string title = MarkdownNormalizer.NormalizeSingleLine(request.Title ?? String.Empty);
                int titleLength = MarkdownNormalizer.Length(title);
                if (titleLength < MinTitleLength || titleLength > MaxTitleLength) errors["title"] = MinTitleLength + " to " + MaxTitleLength + " characters.";
                string body = MarkdownNormalizer.Normalize(request.Body ?? String.Empty);
                int bodyLength = MarkdownNormalizer.Length(body);
                if (bodyLength < MinBodyLength || bodyLength > _Services.Settings.Support.MaxBodyLength) errors["body"] = MinBodyLength + " to " + _Services.Settings.Support.MaxBodyLength + " characters.";
                if (errors.Count > 0) throw ApiErrorException.Validation(errors);
                List<string> mediaIds = request.MediaIds ?? new List<string>();
                await _Services.Media.ValidatePrivateAttachableAsync(actor, mediaIds, MediaPurposeEnum.Ticket, null, _Services.Settings.Support.MaxAttachments, _Services.Settings.Support.AllowVideo ? 1 : 0, token).ConfigureAwait(false);
                await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, actor.IsStaffFor(TargetTypeEnum.Ticket), token, TargetTypeEnum.Ticket).ConfigureAwait(false);
                await _Services.WordFilter.CheckAsync(actor, TargetTypeEnum.Ticket, new Dictionary<string, string?> { { "title", title }, { "body", body } }, token).ConfigureAwait(false);
                long open = await _Services.Database.Support.CountOpenForRequesterAsync(userId, token).ConfigureAwait(false);
                if (open >= _Services.Settings.Support.MaxOpenTicketsPerMember)
                    throw new ApiErrorException(409, "TicketLimitReached", "You have " + open + " open tickets. Close one before opening another (at most " + _Services.Settings.Support.MaxOpenTicketsPerMember + ").");

                DateTime now = _Module.Clock();
                MembershipSummary membership = await _Services.Membership.GetSummaryAsync(userId, token).ConfigureAwait(false);
                bool diagnostics = request.IncludeDiagnostics == true;
                Ticket ticket = new Ticket
                {
                    Id = IdGenerator.Generate(SupportIdPrefixes.Ticket),
                    TypeKey = type!.Key,
                    Title = title,
                    Body = body,
                    Status = TicketStatusEnum.Open,
                    Priority = TicketPriorityEnum.Normal,
                    RequesterUserId = userId,
                    IncludeDiagnostics = diagnostics,
                    DeviceClient = diagnostics ? Clip(client.Client) : null,
                    DeviceClientVersion = diagnostics ? Clip(client.ClientVersion) : null,
                    DeviceUpdateId = diagnostics ? Clip(client.UpdateId) : null,
                    DevicePlatform = diagnostics ? Clip(client.Platform) : null,
                    DeviceOsVersion = diagnostics ? Clip(client.OsVersion) : null,
                    DeviceModel = diagnostics ? Clip(client.DeviceModel) : null,
                    DeviceLocale = diagnostics ? Clip(client.Locale) : null,
                    DeviceTimeZone = diagnostics ? Clip(client.TimeZone) : null,
                    MembershipActive = membership.IsActive,
                    MembershipSource = membership.Source?.ToString(),
                    MembershipStore = membership.Store?.ToString(),
                    MembershipExpiresUtc = membership.ExpiresUtc,
                    FirstResponseDueUtc = now.AddHours(_Services.Settings.Support.FirstResponseTargetHours),
                    LastActivityUtc = now,
                    LastMemberReplyUtc = now,
                    CreatedUtc = now,
                    UpdatedUtc = now
                };
                TicketMessage first = new TicketMessage
                {
                    Id = IdGenerator.Generate(SupportIdPrefixes.TicketMessage),
                    TicketId = ticket.Id,
                    Kind = TicketMessageKindEnum.Reply,
                    AuthorUserId = userId,
                    AuthorRole = TicketAuthorRoleEnum.Member,
                    Body = body,
                    CreatedUtc = now
                };
                _Services.Limits.EnforceMember(actor, _TicketsBucket, _Services.Settings.Support.TicketsPerDay, RateLimitService.Day);
                await _Services.Database.Support.CreateTicketAsync(ticket, first, token).ConfigureAwait(false);
                if (mediaIds.Count > 0) await _Services.Media.AttachPrivateAsync(MediaPurposeEnum.Ticket, ticket.Id, TargetTypeEnum.TicketMessage, first.Id, mediaIds, token).ConfigureAwait(false);
                await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.Ticket, ticket.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
                SupportTelemetry.Count("create", ticket.TypeKey, "member");
                activity?.SetTag("ticket.id", ticket.Id);
                await _Module.Notifier.TicketCreatedAsync(ticket, type, token, actor.Request).ConfigureAwait(false);
                return await _Module.Views.DetailAsync(ticket, actor, false, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// POST /api/v1/tickets/enumerate: the caller's own tickets. Orderings LastActivityDescending (default),
        /// CreatedDescending; filters status (one status or a list), type.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of TicketSummary (no messages).</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 404 NotAvailable.</exception>
        public async Task<EnumerationResult<TicketView>> ListOwnAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Module.RequireEnabled();
            string userId = actor.RequireUserId();
            EnumerationQuery q = query ?? new EnumerationQuery();
            TicketFilter filter = ParseFilter(q, false, _Module.Clock());
            filter.RequesterUserId = userId;
            EnumerationRequest request = _Services.Enumeration.Prepare(q, SupportCollections.MemberTickets, false, "tickets|" + userId);
            EnumerationResult<Ticket> page = await _Services.Database.Support.EnumerateTicketsAsync(request, filter, false, token).ConfigureAwait(false);
            return page.With(await _Module.Views.TicketsAsync(page.Objects, actor, false, token).ConfigureAwait(false));
        }

        /// <summary>
        /// GET /api/v1/tickets/{id}: an own ticket with its newest replies (notes excluded; any other ticket is 404).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Ticket identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Ticket.</returns>
        /// <exception cref="ApiErrorException">404 NotFound or NotAvailable.</exception>
        public async Task<TicketView> GetAsync(ContentActor actor, string id, CancellationToken token = default)
        {
            Ticket ticket = await RequireOwnAsync(actor, id, token).ConfigureAwait(false);
            return await _Module.Views.DetailAsync(ticket, actor, false, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/tickets/{id}/messages/enumerate: replies only (CreatedAscending default).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Ticket identifier.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotFound or NotAvailable.</exception>
        public async Task<EnumerationResult<TicketMessageView>> MessagesAsync(ContentActor actor, string id, EnumerationQuery? query, CancellationToken token = default)
        {
            Ticket ticket = await RequireOwnAsync(actor, id, token).ConfigureAwait(false);
            EnumerationRequest request = _Services.Enumeration.Prepare(query, SupportCollections.Messages, false, "ticket-messages|" + ticket.Id);
            EnumerationResult<TicketMessage> page = await _Services.Database.Support.EnumerateMessagesAsync(request, ticket.Id, false, null, token).ConfigureAwait(false);
            return page.With(await _Module.Views.MessagesAsync(page.Objects, actor, false, token).ConfigureAwait(false));
        }

        /// <summary>
        /// POST /api/v1/tickets/{id}/messages: a reply. WaitingOnMember and Resolved move to Open; a Closed ticket reopens
        /// within support.reopenDays, after that 409 TicketClosed. Notifies the assignee (TicketMemberReplied).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Ticket identifier.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The reply.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or ContentFiltered, 404 NotFound or NotAvailable, 409 TicketClosed or MediaNotAttachable, 429 RateLimited.</exception>
        public async Task<TicketMessageView> ReplyAsync(ContentActor actor, string id, TicketReplyRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            Ticket ticket = await RequireOwnAsync(actor, id, token).ConfigureAwait(false);
            DateTime now = _Module.Clock();
            if (!TicketViewBuilder.WithinReopenWindow(ticket, now, _Services.Settings.Support.ReopenDays)) throw Closed();
            _Services.Limits.CheckMember(actor, _RepliesBucket, _Services.Settings.Support.RepliesPerHour, RateLimitService.Hour);
            string body = MarkdownNormalizer.Normalize(request.Body ?? String.Empty);
            int length = MarkdownNormalizer.Length(body);
            if (length < 1 || length > _Services.Settings.Support.MaxBodyLength) throw ApiErrorException.Field("body", "1 to " + _Services.Settings.Support.MaxBodyLength + " characters.");
            List<string> mediaIds = request.MediaIds ?? new List<string>();
            await _Services.Media.ValidatePrivateAttachableAsync(actor, mediaIds, MediaPurposeEnum.Ticket, ticket.Id, _Services.Settings.Support.MaxAttachments, _Services.Settings.Support.AllowVideo ? 1 : 0, token).ConfigureAwait(false);
            await _Services.WordFilter.CheckAsync(actor, TargetTypeEnum.TicketMessage, new Dictionary<string, string?> { { "body", body } }, token).ConfigureAwait(false);
            TicketMessage reply = new TicketMessage
            {
                Id = IdGenerator.Generate(SupportIdPrefixes.TicketMessage),
                TicketId = ticket.Id,
                Kind = TicketMessageKindEnum.Reply,
                AuthorUserId = actor.UserId,
                AuthorRole = TicketAuthorRoleEnum.Member,
                Body = body,
                CreatedUtc = now
            };
            List<TicketMessage> lines = new List<TicketMessage> { reply };
            TicketStatusEnum previous = ticket.Status;
            if (previous == TicketStatusEnum.WaitingOnMember || previous == TicketStatusEnum.Resolved || previous == TicketStatusEnum.Closed)
            {
                ticket.Status = TicketStatusEnum.Open;
                ticket.ResolvedUtc = null;
                ticket.ClosedUtc = null;
                lines.Add(SystemLine(ticket, "The requester replied; status changed from " + previous + " to Open.", TicketStatusEnum.Open, now.AddTicks(1)));
            }
            ticket.LastActivityUtc = now;
            ticket.LastMemberReplyUtc = now;
            ticket.UpdatedUtc = now;
            _Services.Limits.EnforceMember(actor, _RepliesBucket, _Services.Settings.Support.RepliesPerHour, RateLimitService.Hour);
            if (!await _Services.Database.Support.UpdateTicketAsync(ticket, lines, previous, token).ConfigureAwait(false))
                throw new ApiErrorException(409, "Conflict", "The ticket changed while you were replying; try again.");
            if (mediaIds.Count > 0) await _Services.Media.AttachPrivateAsync(MediaPurposeEnum.Ticket, ticket.Id, TargetTypeEnum.TicketMessage, reply.Id, mediaIds, token).ConfigureAwait(false);
            SupportTelemetry.Count("reply", ticket.TypeKey, "member");
            await _Module.Notifier.MemberRepliedAsync(ticket, token).ConfigureAwait(false);
            return (await _Module.Views.MessagesAsync(new List<TicketMessage> { reply }, actor, false, token).ConfigureAwait(false))[0];
        }

        /// <summary>
        /// POST /api/v1/tickets/{id}/close: the requester closes the ticket (idempotent).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Ticket identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Ticket.</returns>
        /// <exception cref="ApiErrorException">404 NotFound or NotAvailable.</exception>
        public async Task<TicketView> CloseAsync(ContentActor actor, string id, CancellationToken token = default)
        {
            Ticket ticket = await RequireOwnAsync(actor, id, token).ConfigureAwait(false);
            if (ticket.Status != TicketStatusEnum.Closed)
            {
                DateTime now = _Module.Clock();
                TicketStatusEnum previous = ticket.Status;
                ticket.Status = TicketStatusEnum.Closed;
                ticket.ClosedUtc = now;
                ticket.LastActivityUtc = now;
                ticket.UpdatedUtc = now;
                await _Services.Database.Support.UpdateTicketAsync(ticket, new List<TicketMessage> { SystemLine(ticket, "The requester closed the ticket (was " + previous + ").", TicketStatusEnum.Closed, now) }, previous, token).ConfigureAwait(false);
                SupportTelemetry.Count("status", ticket.TypeKey, "member");
            }
            return await _Module.Views.DetailAsync(ticket, actor, false, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/tickets/{id}/reopen: Resolved, or Closed within support.reopenDays, moves to Open; Closed after the
        /// window is 409 TicketClosed; other statuses are unchanged.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Ticket identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Ticket.</returns>
        /// <exception cref="ApiErrorException">404 NotFound or NotAvailable, 409 TicketClosed.</exception>
        public async Task<TicketView> ReopenAsync(ContentActor actor, string id, CancellationToken token = default)
        {
            Ticket ticket = await RequireOwnAsync(actor, id, token).ConfigureAwait(false);
            DateTime now = _Module.Clock();
            if (ticket.Status == TicketStatusEnum.Closed && !TicketViewBuilder.WithinReopenWindow(ticket, now, _Services.Settings.Support.ReopenDays)) throw Closed();
            if (ticket.Status == TicketStatusEnum.Closed || ticket.Status == TicketStatusEnum.Resolved)
            {
                TicketStatusEnum previous = ticket.Status;
                ticket.Status = TicketStatusEnum.Open;
                ticket.ResolvedUtc = null;
                ticket.ClosedUtc = null;
                ticket.LastActivityUtc = now;
                ticket.UpdatedUtc = now;
                await _Services.Database.Support.UpdateTicketAsync(ticket, new List<TicketMessage> { SystemLine(ticket, "The requester reopened the ticket (was " + previous + ").", TicketStatusEnum.Open, now) }, previous, token).ConfigureAwait(false);
                SupportTelemetry.Count("status", ticket.TypeKey, "member");
                await _Module.Notifier.MemberRepliedAsync(ticket, token).ConfigureAwait(false);
            }
            return await _Module.Views.DetailAsync(ticket, actor, false, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Parse the ticket list filters: status (one name, a comma-separated list, or a JSON array), type, and for the staff
        /// inbox priority, assigneeUserId, unassigned, requesterUserId, overdue, hasIdea. The inbox excludes Closed unless a
        /// status filter is given.
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="inbox">Staff inbox.</param>
        /// <param name="nowUtc">Evaluation time of the overdue filter.</param>
        /// <returns>Filter.</returns>
        /// <exception cref="ArgumentNullException">The query is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed.</exception>
        public static TicketFilter ParseFilter(EnumerationQuery query, bool inbox, DateTime nowUtc)
        {
            ArgumentNullException.ThrowIfNull(query);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            TicketFilter filter = new TicketFilter { NowUtc = nowUtc, TypeKey = query.FilterString("type") };
            List<string> statuses = new List<string>();
            foreach (KeyValuePair<string, JsonElement> pair in query.Filters)
            {
                if (!String.Equals(pair.Key, "status", StringComparison.OrdinalIgnoreCase)) continue;
                if (pair.Value.ValueKind == JsonValueKind.Array) statuses.AddRange(pair.Value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString() ?? String.Empty));
                else if (pair.Value.ValueKind == JsonValueKind.String) statuses.AddRange((pair.Value.GetString() ?? String.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
            foreach (string raw in statuses)
            {
                if (!Int32.TryParse(raw, out int _) && Enum.TryParse(raw.Trim(), true, out TicketStatusEnum status) && Enum.IsDefined(status)) { if (!filter.Statuses.Contains(status)) filter.Statuses.Add(status); }
                else errors["filters.status"] = "Use " + String.Join(", ", Enum.GetNames<TicketStatusEnum>()) + ".";
            }
            if (inbox)
            {
                filter.ExcludeClosed = filter.Statuses.Count == 0;
                filter.Priority = query.FilterEnum<TicketPriorityEnum>("priority", errors);
                filter.AssigneeUserId = query.FilterString("assigneeUserId");
                filter.Unassigned = query.FilterBool("unassigned", errors);
                filter.RequesterUserId = query.FilterString("requesterUserId");
                filter.Overdue = query.FilterBool("overdue", errors);
                filter.HasIdea = query.FilterBool("hasIdea", errors);
            }
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            return filter;
        }

        /// <summary>
        /// A staff timeline line (kind System, author role System).
        /// </summary>
        /// <param name="ticket">Ticket.</param>
        /// <param name="text">Plain text.</param>
        /// <param name="statusChange">New status when the line records a status change, or null.</param>
        /// <param name="nowUtc">Time.</param>
        /// <returns>Line.</returns>
        public static TicketMessage SystemLine(Ticket ticket, string text, TicketStatusEnum? statusChange, DateTime nowUtc)
        {
            ArgumentNullException.ThrowIfNull(ticket);
            return new TicketMessage
            {
                Id = IdGenerator.Generate(SupportIdPrefixes.TicketMessage),
                TicketId = ticket.Id,
                Kind = TicketMessageKindEnum.System,
                AuthorRole = TicketAuthorRoleEnum.System,
                Body = text ?? String.Empty,
                StatusChange = statusChange,
                CreatedUtc = nowUtc
            };
        }

        #endregion

        #region Private-Methods

        private async Task<Ticket> RequireOwnAsync(ContentActor actor, string id, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Module.RequireEnabled();
            string userId = actor.RequireUserId();
            Ticket? ticket = await _Services.Database.Support.ReadTicketAsync((id ?? String.Empty).Trim(), token).ConfigureAwait(false);
            if (ticket == null || !String.Equals(ticket.RequesterUserId, userId, StringComparison.Ordinal)) throw ApiErrorException.NotFound("Ticket");
            return ticket;
        }

        private ApiErrorException Closed()
        {
            return new ApiErrorException(409, "TicketClosed", "This ticket was closed more than " + _Services.Settings.Support.ReopenDays + " days ago. Open a new ticket instead.");
        }

        private static string? Clip(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            string trimmed = value.Trim();
            return trimmed.Length > MaxHeaderLength ? trimmed.Substring(0, MaxHeaderLength) : trimmed;
        }

        #endregion
    }
}
