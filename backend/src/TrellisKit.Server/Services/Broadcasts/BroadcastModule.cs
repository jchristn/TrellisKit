namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;

    /// <summary>
    /// The Phase 5 broadcast services of one property (docs/PHASE5_CONTRACT.md sections 8 and 18) and their wiring into
    /// the account lifecycle (account deletion deletes the member's deliveries) and the push pipeline (Essential broadcasts
    /// and test sends ignore the Announcements push preference).
    /// </summary>
    public class BroadcastModule
    {
        #region Public-Members

        /// <summary>
        /// Segments and the audience preview.
        /// </summary>
        public BroadcastSegmentService Segments { get; }

        /// <summary>
        /// Delivery (the send worker's pass).
        /// </summary>
        public BroadcastSendService Sender { get; }

        /// <summary>
        /// Broadcast administration.
        /// </summary>
        public BroadcastService Broadcasts { get; }

        /// <summary>
        /// The member view.
        /// </summary>
        public AnnouncementService Announcements { get; }

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;
        private int _Attached = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the module (call <see cref="Attach"/> once the other services exist).
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public BroadcastModule(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            Segments = new BroadcastSegmentService(services);
            Sender = new BroadcastSendService(services, Segments);
            Broadcasts = new BroadcastService(services, Segments, Sender);
            Announcements = new AnnouncementService(services);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Wire the module into the account lifecycle (idempotent).
        /// </summary>
        public void Attach()
        {
            if (Interlocked.Exchange(ref _Attached, 1) != 0) return;
            Func<string, CancellationToken, Task>? previous = _Services.Accounts.AccountClosed;
            _Services.Accounts.AccountClosed = async (userId, token) =>
            {
                await EraseForAccountAsync(userId, token).ConfigureAwait(false);
                if (previous != null) await previous(userId, token).ConfigureAwait(false);
            };
        }

        /// <summary>
        /// Account deletion (section 12): the member's broadcast deliveries are deleted.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Deliveries deleted.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        public Task<int> EraseForAccountAsync(string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            return _Services.Database.Broadcasts.DeleteDeliveriesForUserAsync(userId, token);
        }

        /// <summary>
        /// The push worker hook: pending Broadcast notifications of Essential broadcasts and test sends are exempt from the
        /// Announcements push preference.
        /// </summary>
        /// <param name="pending">Pending notifications.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Exempt notification identifiers.</returns>
        /// <exception cref="ArgumentNullException">The list is null.</exception>
        public async Task<HashSet<string>> PushExemptionsAsync(IReadOnlyList<Notification> pending, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(pending);
            List<Notification> broadcasts = pending.Where(n => n.Type == NotificationTypeEnum.Broadcast).ToList();
            HashSet<string> exempt = new HashSet<string>(StringComparer.Ordinal);
            if (broadcasts.Count == 0) return exempt;
            HashSet<string> ids = await _Services.Database.Broadcasts.PreferenceExemptAsync(broadcasts.Select(n => n.TargetId), token).ConfigureAwait(false);
            foreach (Notification notification in broadcasts.Where(n => ids.Contains(n.TargetId))) exempt.Add(notification.Id);
            return exempt;
        }

        #endregion
    }
}
