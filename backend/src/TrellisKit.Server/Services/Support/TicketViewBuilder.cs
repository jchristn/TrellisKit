namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Builds ticket, ticket message, and ticket type payloads for a viewer: members never see internal notes, system
    /// lines, membership snapshots, or staff recipients; staff replies show the support team name (plus the agent when
    /// support.showAgentNames); attachments carry per-object URLs for the viewer.
    /// </summary>
    public class TicketViewBuilder
    {
        #region Public-Members

        /// <summary>
        /// Newest messages included in a ticket detail payload.
        /// </summary>
        public const int PreviewMessages = 20;

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
        public TicketViewBuilder(ApplicationServices services, SupportModule module)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            _Module = module ?? throw new ArgumentNullException(nameof(module));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Payloads of a page of tickets (no messages).
        /// </summary>
        /// <param name="tickets">Tickets.</param>
        /// <param name="actor">Viewer.</param>
        /// <param name="staff">Staff view (membership snapshot, staff metadata, staff viewer flags).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Payloads in order.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<List<TicketView>> TicketsAsync(List<Ticket> tickets, ContentActor actor, bool staff, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(tickets);
            ArgumentNullException.ThrowIfNull(actor);
            if (tickets.Count == 0) return new List<TicketView>();
            Dictionary<string, UserSummary> users = await _Services.Summaries.BuildAsync(tickets.SelectMany(t => new[] { t.RequesterUserId, t.AssigneeUserId }), staff ? ProfileViewerEnum.Staff : ProfileViewerEnum.Member, token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.Ticket, tickets.Select(t => t.Id), staff && actor.IsStaffFor(TargetTypeEnum.Ticket), token).ConfigureAwait(false);
            Dictionary<string, string> labels = await TypeLabelsAsync(token).ConfigureAwait(false);
            DateTime now = _Module.Clock();
            List<TicketView> views = new List<TicketView>();
            foreach (Ticket t in tickets)
            {
                ObjectMetadata? meta = metadata.TryGetValue(t.Id, out ObjectMetadata? found) ? found : null;
                TicketView view = new TicketView
                {
                    Id = t.Id,
                    Number = t.Number,
                    Type = new TicketTypeRef { Key = t.TypeKey, Label = labels.TryGetValue(t.TypeKey.ToLowerInvariant(), out string? label) ? label : t.TypeKey },
                    Title = t.Title,
                    Body = t.Body,
                    Status = t.Status,
                    Priority = t.Priority,
                    Requester = User(users, t.RequesterUserId),
                    Assignee = t.AssigneeUserId == null ? null : User(users, t.AssigneeUserId),
                    Device = t.IncludeDiagnostics ? Device(t) : null,
                    Membership = staff ? new TicketMembershipView { IsActive = t.MembershipActive, Source = t.MembershipSource, Store = t.MembershipStore, ExpiresUtc = t.MembershipExpiresUtc } : null,
                    Idea = t.IdeaId == null ? null : new TicketIdeaRef { Id = t.IdeaId },
                    FirstResponseDueUtc = t.FirstResponseDueUtc,
                    FirstResponseUtc = t.FirstResponseUtc,
                    IsOverdue = IsOverdue(t, now),
                    LastActivityUtc = t.LastActivityUtc,
                    ResolvedUtc = t.ResolvedUtc,
                    ClosedUtc = t.ClosedUtc,
                    Labels = meta?.Labels ?? new List<string>(),
                    Tags = meta?.Tags ?? new Dictionary<string, string>(StringComparer.Ordinal),
                    Viewer = Viewer(t, staff, now),
                    CreatedUtc = t.CreatedUtc,
                    UpdatedUtc = t.UpdatedUtc
                };
                views.Add(view);
            }
            return views;
        }

        /// <summary>
        /// Payload of one ticket with its newest messages (and, for staff, the requester's membership now).
        /// </summary>
        /// <param name="ticket">Ticket.</param>
        /// <param name="actor">Viewer.</param>
        /// <param name="staff">Staff view.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Payload.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<TicketView> DetailAsync(Ticket ticket, ContentActor actor, bool staff, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ticket);
            ArgumentNullException.ThrowIfNull(actor);
            TicketView view = (await TicketsAsync(new List<Ticket> { ticket }, actor, staff, token).ConfigureAwait(false))[0];
            List<TicketMessage> latest = await _Services.Database.Support.LatestMessagesAsync(ticket.Id, staff, PreviewMessages, token).ConfigureAwait(false);
            long total = await _Services.Database.Support.CountMessagesAsync(ticket.Id, staff, token).ConfigureAwait(false);
            view.Messages = new TicketMessagePreview
            {
                Objects = await MessagesAsync(latest, actor, staff, token).ConfigureAwait(false),
                TotalRecords = total,
                Enumerate = Constants.ApiPrefix + (staff ? "/admin/tickets/" : "/tickets/") + ticket.Id + "/messages/enumerate"
            };
            if (staff && !String.IsNullOrEmpty(ticket.RequesterUserId))
            {
                MembershipSummary current = await _Services.Membership.GetSummaryAsync(ticket.RequesterUserId, token).ConfigureAwait(false);
                view.CurrentMembership = new TicketMembershipView { IsActive = current.IsActive, Source = current.Source?.ToString(), Store = current.Store?.ToString(), ExpiresUtc = current.ExpiresUtc };
            }
            return view;
        }

        /// <summary>
        /// Payloads of ticket messages with their attachments.
        /// </summary>
        /// <param name="messages">Messages.</param>
        /// <param name="actor">Viewer.</param>
        /// <param name="staff">Staff view (agent names always shown, staff metadata).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Payloads in order.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<List<TicketMessageView>> MessagesAsync(List<TicketMessage> messages, ContentActor actor, bool staff, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(messages);
            ArgumentNullException.ThrowIfNull(actor);
            List<TicketMessage> visible = staff ? messages : messages.Where(m => m.Kind == TicketMessageKindEnum.Reply).ToList();
            if (visible.Count == 0) return new List<TicketMessageView>();
            Dictionary<string, UserSummary> users = await _Services.Summaries.BuildAsync(visible.Select(m => m.AuthorUserId), staff ? ProfileViewerEnum.Staff : ProfileViewerEnum.Member, token).ConfigureAwait(false);
            Dictionary<string, List<MediaView>> media = await _Services.Media.ListPrivateViewsForTargetsAsync(TargetTypeEnum.TicketMessage, visible.Select(m => m.Id), actor, token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.TicketMessage, visible.Select(m => m.Id), staff && actor.IsStaffFor(TargetTypeEnum.TicketMessage), token).ConfigureAwait(false);
            bool showAgents = _Services.Settings.Support.ShowAgentNames;
            string team = _Module.TeamName();
            List<TicketMessageView> views = new List<TicketMessageView>();
            foreach (TicketMessage m in visible)
            {
                ObjectMetadata? meta = metadata.TryGetValue(m.Id, out ObjectMetadata? found) ? found : null;
                UserSummary? author = m.AuthorUserId == null ? null : User(users, m.AuthorUserId);
                string? authorName = null;
                if (m.AuthorRole == TicketAuthorRoleEnum.Staff)
                {
                    if (!staff && !showAgents) author = null;
                    authorName = author != null && !author.IsDeleted ? author.DisplayName + ", " + team : team;
                }
                else if (m.AuthorRole == TicketAuthorRoleEnum.Member) authorName = author?.DisplayName;
                views.Add(new TicketMessageView
                {
                    Id = m.Id,
                    TicketId = m.TicketId,
                    Kind = m.Kind,
                    Author = author,
                    AuthorRole = m.AuthorRole,
                    AuthorName = authorName,
                    Body = m.Body,
                    StatusChange = m.StatusChange,
                    Attachments = media.TryGetValue(m.Id, out List<MediaView>? list) ? list : new List<MediaView>(),
                    Labels = meta?.Labels ?? new List<string>(),
                    Tags = meta?.Tags ?? new Dictionary<string, string>(StringComparer.Ordinal),
                    CreatedUtc = m.CreatedUtc
                });
            }
            return views;
        }

        /// <summary>
        /// Payloads of ticket types (recipients only for SystemAdmin).
        /// </summary>
        /// <param name="types">Types.</param>
        /// <param name="actor">Viewer.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Payloads in order.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<List<TicketTypeView>> TypesAsync(List<TicketType> types, ContentActor actor, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(types);
            ArgumentNullException.ThrowIfNull(actor);
            Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.TicketType, types.Select(t => t.Id), actor.IsStaffFor(TargetTypeEnum.TicketType), token).ConfigureAwait(false);
            return types.Select(t =>
            {
                ObjectMetadata? meta = metadata.TryGetValue(t.Id, out ObjectMetadata? found) ? found : null;
                return new TicketTypeView
                {
                    Id = t.Id,
                    Key = t.Key,
                    Label = t.Label,
                    Description = t.Description,
                    HelpText = t.HelpText,
                    StaffRecipients = actor.IsSystemAdmin ? t.StaffRecipients.ToList() : null,
                    SortOrder = t.SortOrder,
                    IsRetired = t.IsRetired,
                    Labels = meta?.Labels ?? new List<string>(),
                    Tags = meta?.Tags ?? new Dictionary<string, string>(StringComparer.Ordinal),
                    CreatedUtc = t.CreatedUtc,
                    UpdatedUtc = t.UpdatedUtc
                };
            }).ToList();
        }

        /// <summary>
        /// Whether a ticket is overdue: unanswered past the first response target and not Closed.
        /// </summary>
        /// <param name="ticket">Ticket.</param>
        /// <param name="nowUtc">Evaluation time.</param>
        /// <returns>True when overdue.</returns>
        public static bool IsOverdue(Ticket ticket, DateTime nowUtc)
        {
            ArgumentNullException.ThrowIfNull(ticket);
            return ticket.FirstResponseUtc == null && ticket.Status != TicketStatusEnum.Closed && ticket.FirstResponseDueUtc != null && ticket.FirstResponseDueUtc <= nowUtc;
        }

        /// <summary>
        /// Whether a Closed ticket can still be reopened (or replied to) by its requester.
        /// </summary>
        /// <param name="ticket">Ticket.</param>
        /// <param name="nowUtc">Evaluation time.</param>
        /// <param name="reopenDays">support.reopenDays.</param>
        /// <returns>True within the window, or when the ticket is not Closed.</returns>
        public static bool WithinReopenWindow(Ticket ticket, DateTime nowUtc, int reopenDays)
        {
            ArgumentNullException.ThrowIfNull(ticket);
            if (ticket.Status != TicketStatusEnum.Closed) return true;
            DateTime closed = ticket.ClosedUtc ?? ticket.UpdatedUtc;
            return closed.AddDays(reopenDays) > nowUtc;
        }

        #endregion

        #region Private-Methods

        private TicketViewerView Viewer(Ticket t, bool staff, DateTime now)
        {
            bool reopenable = WithinReopenWindow(t, now, _Services.Settings.Support.ReopenDays);
            bool attach = _Services.Settings.Support.MaxAttachments > 0;
            if (staff)
            {
                return new TicketViewerView { CanReply = true, CanClose = t.Status != TicketStatusEnum.Closed, CanReopen = t.Status == TicketStatusEnum.Closed || t.Status == TicketStatusEnum.Resolved, CanAttach = attach, CanNote = true, CanAssign = true, CanSetStatus = true };
            }
            return new TicketViewerView
            {
                CanReply = t.Status != TicketStatusEnum.Closed || reopenable,
                CanClose = t.Status != TicketStatusEnum.Closed,
                CanReopen = t.Status == TicketStatusEnum.Resolved || (t.Status == TicketStatusEnum.Closed && reopenable),
                CanAttach = attach && (t.Status != TicketStatusEnum.Closed || reopenable)
            };
        }

        private async Task<Dictionary<string, string>> TypeLabelsAsync(CancellationToken token)
        {
            List<TicketType> types = await _Services.Database.Support.ListTypesAsync(true, 200, token).ConfigureAwait(false);
            Dictionary<string, string> labels = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (TicketType type in types) labels[type.Key.ToLowerInvariant()] = type.Label;
            return labels;
        }

        private static TicketDeviceView Device(Ticket t)
        {
            return new TicketDeviceView
            {
                Client = t.DeviceClient,
                ClientVersion = t.DeviceClientVersion,
                Platform = t.DevicePlatform,
                OsVersion = t.DeviceOsVersion,
                DeviceModel = t.DeviceModel,
                UpdateId = t.DeviceUpdateId,
                Locale = t.DeviceLocale,
                TimeZone = t.DeviceTimeZone
            };
        }

        private static UserSummary User(Dictionary<string, UserSummary> users, string? userId)
        {
            if (userId != null && users.TryGetValue(userId, out UserSummary? summary)) return summary;
            return UserSummary.Deleted();
        }

        #endregion
    }
}
