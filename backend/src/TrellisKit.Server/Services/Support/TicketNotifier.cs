namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Server.Services.Email;
    using TrellisKit.Server.Services.Realtime;

    /// <summary>
    /// Ticket notifications, emails, and live events (docs/PHASE5_CONTRACT.md sections 6.3, 9, and 10): TicketUpdate to the
    /// requester (category Account) and TicketOpened, TicketMemberReplied, TicketOverdue to support staff (category
    /// Support), each grouped per ticket while unread; the ticket_created, ticket_reply, ticket_resolved,
    /// staff_ticket_opened, and staff_ticket_overdue emails with idempotency keys; and ticket.updated on the realtime bus
    /// after staff replies and status changes. Internal notes never notify or email anyone. Failures are logged and never
    /// fail the write that triggered them.
    /// </summary>
    public class TicketNotifier
    {
        #region Public-Members

        /// <summary>
        /// Longest reply text in a ticket_reply email (characters). Default 4000.
        /// </summary>
        public const int MaxEmailReplyLength = 4000;

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
        public TicketNotifier(ApplicationServices services, SupportModule module)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            _Module = module ?? throw new ArgumentNullException(nameof(module));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// A member opened a ticket: ticket_created to the requester, staff_ticket_opened to the type's staff recipients,
        /// and TicketOpened to every support staff member.
        /// </summary>
        /// <param name="ticket">Ticket.</param>
        /// <param name="type">Its type (recipients), or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task TicketCreatedAsync(Ticket ticket, TicketType? type, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ticket);
            await SafeAsync("ticket_created", async () =>
            {
                User? requester = await RequesterAsync(ticket, token).ConfigureAwait(false);
                if (requester != null)
                {
                    EmailRequest email = MemberEmail("ticket_created", requester, "ticket_created:" + ticket.Id);
                    AddTicketTokens(email, ticket);
                    await _Services.Email.QueueAsync(email, token).ConfigureAwait(false);
                }
                List<string> recipients = (type?.StaffRecipients ?? new List<string>()).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList();
                foreach (string recipient in recipients)
                {
                    EmailRequest staff = new EmailRequest { Template = "staff_ticket_opened", To = recipient, IdempotencyKey = "staff_ticket_opened:" + ticket.Id + ":" + recipient.ToLowerInvariant() };
                    staff.Tokens["memberName"] = "team";
                    staff.Tokens["ticketNumber"] = Number(ticket);
                    staff.Tokens["type"] = type?.Label ?? ticket.TypeKey;
                    staff.Tokens["title"] = ticket.Title;
                    staff.Tokens["requesterName"] = requester?.DisplayName ?? "Deleted member";
                    staff.Tokens["adminLink"] = _Services.Email.Link(AdminPath(ticket));
                    staff.Tokens["link"] = staff.Tokens["adminLink"];
                    await _Services.Email.QueueAsync(staff, token).ConfigureAwait(false);
                }
                List<string> staffIds = await _Module.StaffUserIdsAsync(token).ConfigureAwait(false);
                await StaffNotificationAsync(NotificationTypeEnum.TicketOpened, ticket, staffIds, ticket.RequesterUserId, "New ticket " + Number(ticket) + ": " + ticket.Title, token).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// The requester replied: TicketMemberReplied to the assignee.
        /// </summary>
        /// <param name="ticket">Ticket after the reply.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task MemberRepliedAsync(Ticket ticket, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ticket);
            if (String.IsNullOrEmpty(ticket.AssigneeUserId)) return;
            await SafeAsync("member_replied", () => StaffNotificationAsync(NotificationTypeEnum.TicketMemberReplied, ticket, new List<string> { ticket.AssigneeUserId }, ticket.RequesterUserId,
                "New reply on ticket " + Number(ticket) + ": " + ticket.Title, token)).ConfigureAwait(false);
        }

        /// <summary>
        /// A staff member replied: TicketUpdate and ticket_reply to the requester, and ticket.updated.
        /// </summary>
        /// <param name="ticket">Ticket after the reply.</param>
        /// <param name="reply">Reply.</param>
        /// <param name="agentName">Name shown to the requester.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task StaffRepliedAsync(Ticket ticket, TicketMessage reply, string agentName, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ticket);
            ArgumentNullException.ThrowIfNull(reply);
            await SafeAsync("staff_replied", async () =>
            {
                await MemberNotificationAsync(ticket, reply.AuthorUserId, "Support replied to ticket " + Number(ticket) + ": " + ticket.Title, token).ConfigureAwait(false);
                User? requester = await RequesterAsync(ticket, token).ConfigureAwait(false);
                if (requester != null)
                {
                    EmailRequest email = MemberEmail("ticket_reply", requester, "ticket_reply:" + reply.Id);
                    AddTicketTokens(email, ticket);
                    string text = MarkdownNormalizer.ToPlainText(reply.Body);
                    email.Tokens["replyText"] = text.Length > MaxEmailReplyLength ? text.Substring(0, MaxEmailReplyLength) + "..." : text;
                    email.Tokens["agentName"] = agentName;
                    await _Services.Email.QueueAsync(email, token).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
            await PublishAsync(ticket, token).ConfigureAwait(false);
        }

        /// <summary>
        /// The status changed (staff or the auto close pass): TicketUpdate to the requester, ticket_resolved when the ticket
        /// became Resolved or Closed, and ticket.updated.
        /// </summary>
        /// <param name="ticket">Ticket after the change.</param>
        /// <param name="actorUserId">Staff member, or null for the system.</param>
        /// <param name="sendEmail">Whether to send ticket_resolved for Resolved and Closed.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task StatusChangedAsync(Ticket ticket, string? actorUserId, bool sendEmail, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ticket);
            await SafeAsync("status_changed", async () =>
            {
                await MemberNotificationAsync(ticket, actorUserId, "Ticket " + Number(ticket) + " is now " + StatusText(ticket.Status) + ": " + ticket.Title, token).ConfigureAwait(false);
                bool done = ticket.Status == TicketStatusEnum.Resolved || ticket.Status == TicketStatusEnum.Closed;
                if (!sendEmail || !done) return;
                User? requester = await RequesterAsync(ticket, token).ConfigureAwait(false);
                if (requester == null) return;
                DateTime when = (ticket.Status == TicketStatusEnum.Closed ? ticket.ClosedUtc : ticket.ResolvedUtc) ?? ticket.UpdatedUtc;
                EmailRequest email = MemberEmail("ticket_resolved", requester, "ticket_resolved:" + ticket.Id + ":" + when.Ticks.ToString(CultureInfo.InvariantCulture));
                AddTicketTokens(email, ticket);
                email.Tokens["status"] = StatusText(ticket.Status);
                email.Tokens["reopenDays"] = _Services.Settings.Support.ReopenDays.ToString(CultureInfo.InvariantCulture);
                await _Services.Email.QueueAsync(email, token).ConfigureAwait(false);
            }).ConfigureAwait(false);
            await PublishAsync(ticket, token).ConfigureAwait(false);
        }

        /// <summary>
        /// A ticket passed its first response target: TicketOverdue and staff_ticket_overdue to the assignee, or to every
        /// support staff member when unassigned (email only for staff who keep Support email on).
        /// </summary>
        /// <param name="ticket">Ticket.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task OverdueAsync(Ticket ticket, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ticket);
            await SafeAsync("overdue", async () =>
            {
                List<string> recipients = !String.IsNullOrEmpty(ticket.AssigneeUserId) ? new List<string> { ticket.AssigneeUserId } : await _Module.StaffUserIdsAsync(token).ConfigureAwait(false);
                await StaffNotificationAsync(NotificationTypeEnum.TicketOverdue, ticket, recipients, null, "Ticket " + Number(ticket) + " is past the first response target: " + ticket.Title, token).ConfigureAwait(false);
                if (recipients.Count == 0) return;
                List<User> users = await _Services.Database.Users.ReadManyAsync(recipients, token).ConfigureAwait(false);
                List<NotificationPreference> preferences = await _Services.Database.NotificationPreferences.EnumerateAsync(recipients, token).ConfigureAwait(false);
                foreach (User user in users.Where(u => !u.IsDeleted && !String.IsNullOrWhiteSpace(u.Email)))
                {
                    if (!NotificationPreferenceService.Effective(user.Id, preferences, NotificationCategoryEnum.Support).Email) continue;
                    EmailRequest email = new EmailRequest { Template = "staff_ticket_overdue", To = user.Email!, UserId = user.Id, IdempotencyKey = "staff_ticket_overdue:" + ticket.Id + ":" + user.Id };
                    email.Tokens["memberName"] = user.DisplayName;
                    email.Tokens["ticketNumber"] = Number(ticket);
                    email.Tokens["title"] = ticket.Title;
                    email.Tokens["dueUtc"] = ticket.FirstResponseDueUtc == null ? String.Empty : EmailService.FormatDate(ticket.FirstResponseDueUtc.Value);
                    email.Tokens["adminLink"] = _Services.Email.Link(AdminPath(ticket));
                    email.Tokens["link"] = email.Tokens["adminLink"];
                    await _Services.Email.QueueAsync(email, token).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// The member path of a ticket.
        /// </summary>
        /// <param name="ticket">Ticket.</param>
        /// <returns>Path.</returns>
        public static string MemberPath(Ticket ticket)
        {
            ArgumentNullException.ThrowIfNull(ticket);
            return "/support/tickets/" + ticket.Id;
        }

        /// <summary>
        /// The admin console path of a ticket: the support inbox with the ticket drawer open.
        /// </summary>
        /// <param name="ticket">Ticket.</param>
        /// <returns>Path.</returns>
        public static string AdminPath(Ticket ticket)
        {
            ArgumentNullException.ThrowIfNull(ticket);
            return "/admin/support?ticket=" + Uri.EscapeDataString(ticket.Id);
        }

        /// <summary>
        /// The human ticket number ("#1042").
        /// </summary>
        /// <param name="ticket">Ticket.</param>
        /// <returns>Text.</returns>
        public static string Number(Ticket ticket)
        {
            ArgumentNullException.ThrowIfNull(ticket);
            return "#" + ticket.Number.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The group key of a ticket notification: one unread row per ticket and type (Ticket:&lt;type&gt;:&lt;id&gt;).
        /// </summary>
        /// <param name="type">Notification type.</param>
        /// <param name="ticket">Ticket.</param>
        /// <returns>Group key.</returns>
        public static string GroupKey(NotificationTypeEnum type, Ticket ticket)
        {
            ArgumentNullException.ThrowIfNull(ticket);
            return "Ticket:" + type + ":" + ticket.Id;
        }

        #endregion

        #region Private-Methods

        private async Task MemberNotificationAsync(Ticket ticket, string? actorUserId, string title, CancellationToken token)
        {
            if (String.IsNullOrEmpty(ticket.RequesterUserId)) return;
            await _Services.Notifications.CreateAsync(new NotificationDraft
            {
                RecipientUserId = ticket.RequesterUserId,
                Type = NotificationTypeEnum.TicketUpdate,
                ActorUserId = actorUserId,
                TargetType = TargetTypeEnum.Ticket,
                TargetId = ticket.Id,
                GroupKey = GroupKey(NotificationTypeEnum.TicketUpdate, ticket),
                TitleText = title,
                Path = MemberPath(ticket),
                QueuePush = true
            }, token).ConfigureAwait(false);
        }

        private async Task StaffNotificationAsync(NotificationTypeEnum type, Ticket ticket, IEnumerable<string> recipients, string? actorUserId, string title, CancellationToken token)
        {
            foreach (string recipient in recipients.Where(r => !String.IsNullOrEmpty(r)).Distinct(StringComparer.Ordinal))
            {
                await _Services.Notifications.CreateAsync(new NotificationDraft
                {
                    RecipientUserId = recipient,
                    Type = type,
                    ActorUserId = actorUserId,
                    TargetType = TargetTypeEnum.Ticket,
                    TargetId = ticket.Id,
                    GroupKey = GroupKey(type, ticket),
                    TitleText = title,
                    Path = AdminPath(ticket),
                    QueuePush = true
                }, token).ConfigureAwait(false);
            }
        }

        private async Task PublishAsync(Ticket ticket, CancellationToken token)
        {
            if (String.IsNullOrEmpty(ticket.RequesterUserId)) return;
            try
            {
                await _Services.RealtimeBus.PublishAsync(new RealtimeEvent
                {
                    PropertyId = _Services.Property.Property.Id,
                    Kind = RealtimeEventKinds.TicketUpdated,
                    TargetId = ticket.Id,
                    State = ticket.Status.ToString(),
                    UserIds = new List<string> { ticket.RequesterUserId }
                }, token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _Services.Logger.Warn("[TicketNotifier] [p:" + _Services.Property.Property.Id + "] ticket.updated for " + ticket.Id + " failed: " + e.Message);
            }
        }

        private async Task<User?> RequesterAsync(Ticket ticket, CancellationToken token)
        {
            if (String.IsNullOrEmpty(ticket.RequesterUserId)) return null;
            User? user = await _Services.Database.Users.ReadAsync(ticket.RequesterUserId, token).ConfigureAwait(false);
            return user == null || user.IsDeleted || String.IsNullOrWhiteSpace(user.Email) ? null : user;
        }

        private EmailRequest MemberEmail(string template, User user, string idempotencyKey)
        {
            EmailRequest request = new EmailRequest { Template = template, To = user.Email!, UserId = user.Id, IdempotencyKey = idempotencyKey };
            request.Tokens["memberName"] = user.DisplayName;
            return request;
        }

        private void AddTicketTokens(EmailRequest email, Ticket ticket)
        {
            email.Tokens["ticketNumber"] = Number(ticket);
            email.Tokens["title"] = ticket.Title;
            email.Tokens["link"] = _Services.Email.MemberLink(MemberPath(ticket));
        }

        private static string StatusText(TicketStatusEnum status)
        {
            switch (status)
            {
                case TicketStatusEnum.InProgress:
                    return "in progress";
                case TicketStatusEnum.WaitingOnMember:
                    return "waiting on you";
                case TicketStatusEnum.Resolved:
                    return "resolved";
                case TicketStatusEnum.Closed:
                    return "closed";
                default:
                    return "open";
            }
        }

        private async Task SafeAsync(string what, Func<Task> body)
        {
            try
            {
                await body().ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _Services.Logger.Warn("[TicketNotifier] [p:" + _Services.Property.Property.Id + "] " + what + " failed: " + e.Message);
            }
        }

        #endregion
    }
}
