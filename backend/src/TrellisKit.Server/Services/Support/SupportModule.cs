namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;

    /// <summary>
    /// The Phase 5 support ticket services of one property (docs/PHASE5_CONTRACT.md section 6, as-built note C) and their
    /// wiring into the shared services: private ticket media (purpose Ticket), the Support notification category (shown only
    /// to support staff), and the account export. Tickets are never assistant knowledge or global search, and account
    /// deletion leaves them exactly as they are (the requester renders as Deleted member).
    /// </summary>
    public class SupportModule
    {
        #region Public-Members

        /// <summary>
        /// Clock of the ticket workflow (first response targets, reopen windows, due and auto close passes). Tests replace
        /// it with a fixed clock. Default DateTime.UtcNow.
        /// </summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public Func<DateTime> Clock
        {
            get { return _Clock; }
            set { _Clock = value ?? throw new ArgumentNullException(nameof(Clock)); }
        }

        /// <summary>
        /// Ticket types (member list, admin CRUD, seeding).
        /// </summary>
        public TicketTypeService Types { get; }

        /// <summary>
        /// Member ticket routes.
        /// </summary>
        public TicketService Tickets { get; }

        /// <summary>
        /// Staff ticket routes (inbox, replies, notes, updates, export, metrics, delete).
        /// </summary>
        public StaffTicketService Staff { get; }

        /// <summary>
        /// Due and auto close passes.
        /// </summary>
        public TicketWorkflowService Workflow { get; }

        /// <summary>
        /// Notifications, emails, and live events.
        /// </summary>
        public TicketNotifier Notifier { get; }

        /// <summary>
        /// Payload builders.
        /// </summary>
        public TicketViewBuilder Views { get; }

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;
        private Func<DateTime> _Clock = () => DateTime.UtcNow;
        private int _Attached = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the module (call <see cref="Attach"/> once the shared services exist).
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public SupportModule(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            Views = new TicketViewBuilder(services, this);
            Notifier = new TicketNotifier(services, this);
            Types = new TicketTypeService(services, this);
            Tickets = new TicketService(services, this);
            Staff = new StaffTicketService(services, this);
            Workflow = new TicketWorkflowService(services, this);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Wire the module into the shared services (idempotent): the Ticket private media scope (requester and support
        /// staff), the Support notification category for staff only, and the tickets account export section.
        /// </summary>
        public void Attach()
        {
            if (Interlocked.Exchange(ref _Attached, 1) != 0) return;
            _Services.Media.RegisterPrivateScope(MediaPurposeEnum.Ticket, new PrivateMediaScope
            {
                CanAccess = CanAccessTicketAsync,
                AllowVideo = () => _Services.Settings.Support.AllowVideo,
                RequirePostingPermissions = false
            });
            _Services.NotificationPreferences.StaffCategoryCheck = (userId, token) => IsStaffUserAsync(userId, token);
            TicketExportSection.Register(_Services);
        }

        /// <summary>
        /// Throw 404 NotAvailable when features.support is off.
        /// </summary>
        /// <exception cref="ApiErrorException">404 NotAvailable.</exception>
        public void RequireEnabled()
        {
            if (!_Services.Property.Property.Features.Support) throw new ApiErrorException(404, "NotAvailable", "Support tickets are not available in this community; contact the support email instead.");
        }

        /// <summary>
        /// Whether the caller is support staff (SystemAdmin or SupportAgent).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <returns>True for staff.</returns>
        public static bool IsStaff(ContentActor actor)
        {
            return actor != null && (actor.IsSystemAdmin || actor.IsSupportAgent);
        }

        /// <summary>
        /// Throw 403 Forbidden unless the caller is support staff.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <exception cref="ApiErrorException">403 Forbidden.</exception>
        public static void RequireStaff(ContentActor actor)
        {
            if (!IsStaff(actor)) throw ApiErrorException.Forbidden("Support staff only.");
        }

        /// <summary>
        /// Whether a member holds SupportAgent or SystemAdmin.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True for support staff.</returns>
        public async Task<bool> IsStaffUserAsync(string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) return false;
            List<RoleAssignment> roles = await _Services.Database.RoleAssignments.EnumerateByUserAsync(userId, token).ConfigureAwait(false);
            return roles.Any(r => r.Role == RoleEnum.SystemAdmin || r.Role == RoleEnum.SupportAgent);
        }

        /// <summary>
        /// Every support staff member (SupportAgent and SystemAdmin holders), at most 200, without deleted accounts.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>User identifiers.</returns>
        public async Task<List<string>> StaffUserIdsAsync(CancellationToken token = default)
        {
            List<RoleAssignment> agents = await _Services.Database.RoleAssignments.EnumerateByRoleAsync(RoleEnum.SupportAgent, 200, token).ConfigureAwait(false);
            List<RoleAssignment> admins = await _Services.Database.RoleAssignments.EnumerateByRoleAsync(RoleEnum.SystemAdmin, 200, token).ConfigureAwait(false);
            List<string> ids = agents.Concat(admins).Select(r => r.UserId).Where(i => !String.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal).Take(200).ToList();
            if (ids.Count == 0) return ids;
            List<User> users = await _Services.Database.Users.ReadManyAsync(ids, token).ConfigureAwait(false);
            HashSet<string> live = new HashSet<string>(users.Where(u => !u.IsDeleted).Select(u => u.Id), StringComparer.Ordinal);
            return ids.Where(live.Contains).ToList();
        }

        /// <summary>
        /// The support team name members see on staff replies ("{name} Support").
        /// </summary>
        /// <returns>Name.</returns>
        public string TeamName()
        {
            return _Services.Property.Name + " Support";
        }

        #endregion

        #region Private-Methods

        private async Task<bool> CanAccessTicketAsync(string viewerUserId, string ticketId, CancellationToken token)
        {
            if (String.IsNullOrEmpty(viewerUserId) || String.IsNullOrEmpty(ticketId)) return false;
            Ticket? ticket = await _Services.Database.Support.ReadTicketAsync(ticketId, token).ConfigureAwait(false);
            if (ticket == null) return false;
            if (String.Equals(ticket.RequesterUserId, viewerUserId, StringComparison.Ordinal))
            {
                User? user = await _Services.Database.Users.ReadAsync(viewerUserId, token).ConfigureAwait(false);
                return user != null && !user.IsDeleted;
            }
            return await IsStaffUserAsync(viewerUserId, token).ConfigureAwait(false);
        }

        #endregion
    }
}
