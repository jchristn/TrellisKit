namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Computes effective membership from every entitlement: active when any source is active, coverage extends through
    /// back-to-back entitlements (the latest end wins), and a null end means lifetime.
    /// Thread safety: stateless; safe to call concurrently.
    /// </summary>
    public class MembershipService
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Database;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <exception cref="ArgumentNullException">The database is null.</exception>
        public MembershipService(DatabaseDriverBase database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read a member's entitlements and subscriptions and compute their membership.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>State.</returns>
        /// <exception cref="ArgumentNullException">The identifier is null or empty.</exception>
        public async Task<MembershipState> GetStateAsync(string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            List<Entitlement> entitlements = await _Database.Entitlements.EnumerateByUserAsync(userId, token).ConfigureAwait(false);
            List<Subscription> subscriptions = await _Database.Subscriptions.EnumerateByUserAsync(userId, token).ConfigureAwait(false);
            return new MembershipState
            {
                Entitlements = entitlements,
                Subscriptions = subscriptions,
                Summary = Compute(entitlements, subscriptions, DateTime.UtcNow)
            };
        }

        /// <summary>
        /// Compute a member's membership.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Summary.</returns>
        /// <exception cref="ArgumentNullException">The identifier is null or empty.</exception>
        public async Task<MembershipSummary> GetSummaryAsync(string userId, CancellationToken token = default)
        {
            MembershipState state = await GetStateAsync(userId, token).ConfigureAwait(false);
            return state.Summary;
        }

        /// <summary>
        /// Compute membership from entitlements and subscriptions.
        /// </summary>
        /// <param name="entitlements">Entitlements.</param>
        /// <param name="subscriptions">Subscriptions (for store and auto-renew of subscription entitlements).</param>
        /// <param name="nowUtc">Current time.</param>
        /// <returns>Summary.</returns>
        /// <exception cref="ArgumentNullException">A list is null.</exception>
        public static MembershipSummary Compute(List<Entitlement> entitlements, List<Subscription> subscriptions, DateTime nowUtc)
        {
            ArgumentNullException.ThrowIfNull(entitlements);
            ArgumentNullException.ThrowIfNull(subscriptions);

            List<Entitlement> active = entitlements.Where(e => e.IsActiveAt(nowUtc)).ToList();
            if (active.Count == 0) return new MembershipSummary();

            Dictionary<string, Subscription> byId = subscriptions.ToDictionary(s => s.Id, StringComparer.Ordinal);
            Entitlement? primary = active.Where(e => e.Source == MembershipSourceEnum.Subscription).OrderBy(e => EndKey(e)).LastOrDefault()
                ?? active.OrderBy(e => EndKey(e)).Last();

            MembershipSummary summary = new MembershipSummary
            {
                IsActive = true,
                Source = primary.Source,
                ExpiresUtc = CoverageEnd(entitlements, nowUtc)
            };

            if (primary.Source == MembershipSourceEnum.Subscription && byId.TryGetValue(primary.SourceId, out Subscription? subscription))
            {
                summary.Store = subscription.Store;
                summary.AutoRenew = subscription.AutoRenew;
            }
            else
            {
                summary.WillConvertToPaid = entitlements.Any(e =>
                    e.Source == MembershipSourceEnum.Subscription && e.RevokedUtc == null && (e.EndsUtc == null || e.EndsUtc.Value > nowUtc) &&
                    byId.TryGetValue(e.SourceId, out Subscription? s) && s.AutoRenew);
            }
            return summary;
        }

        /// <summary>
        /// End of continuous membership coverage starting now: follows entitlements that start before the current
        /// coverage ends. Returns null for lifetime coverage or when nothing is active.
        /// </summary>
        /// <param name="entitlements">Entitlements.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <returns>Coverage end, or null.</returns>
        /// <exception cref="ArgumentNullException">The list is null.</exception>
        public static DateTime? CoverageEnd(List<Entitlement> entitlements, DateTime nowUtc)
        {
            ArgumentNullException.ThrowIfNull(entitlements);
            List<Entitlement> candidates = entitlements.Where(e => e.RevokedUtc == null && (e.EndsUtc == null || e.EndsUtc.Value > nowUtc)).ToList();
            if (!candidates.Any(e => e.IsActiveAt(nowUtc))) return null;

            DateTime coverage = nowUtc;
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (Entitlement e in candidates)
                {
                    if (e.StartsUtc > coverage) continue;
                    if (e.EndsUtc == null) return null;
                    if (e.EndsUtc.Value > coverage)
                    {
                        coverage = e.EndsUtc.Value;
                        changed = true;
                    }
                }
            }
            return coverage;
        }

        /// <summary>
        /// End of the current paid coverage: the latest end among active subscription entitlements, or null when no paid
        /// subscription is active.
        /// </summary>
        /// <param name="entitlements">Entitlements.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <returns>Paid coverage end, or null.</returns>
        /// <exception cref="ArgumentNullException">The list is null.</exception>
        public static DateTime? PaidCoverageEnd(List<Entitlement> entitlements, DateTime nowUtc)
        {
            ArgumentNullException.ThrowIfNull(entitlements);
            List<Entitlement> paid = entitlements.Where(e => e.Source == MembershipSourceEnum.Subscription && e.IsActiveAt(nowUtc) && e.EndsUtc != null).ToList();
            if (paid.Count == 0) return null;
            return paid.Max(e => e.EndsUtc!.Value);
        }

        /// <summary>
        /// One page of a member's membership history (collections <see cref="PhaseOneCollections.MembershipHistory"/>
        /// and <see cref="PhaseOneCollections.AdminEntitlements"/>; filters source and status). Labels and tags are left
        /// empty for the caller to attach.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="request">Normalized enumeration.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed for a bad filter value.</exception>
        public async Task<EnumerationResult<MembershipHistoryItem>> EnumerateHistoryAsync(string userId, EnumerationRequest request, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            ArgumentNullException.ThrowIfNull(request);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            EntitlementFilter filter = new EntitlementFilter
            {
                UserId = userId,
                Source = request.Query.FilterEnum<MembershipSourceEnum>("source", errors),
                Status = request.Query.FilterEnum<EntitlementStatusEnum>("status", errors)
            };
            if (errors.Count > 0) throw ApiErrorException.Validation(errors, "One or more filters are invalid.");
            DateTime now = DateTime.UtcNow;
            EnumerationResult<Entitlement> page = await _Database.Entitlements.EnumerateAsync(request, filter, now, token).ConfigureAwait(false);
            Dictionary<string, Subscription> byId = new Dictionary<string, Subscription>(StringComparer.Ordinal);
            foreach (string id in page.Objects.Where(e => e.Source == MembershipSourceEnum.Subscription).Select(e => e.SourceId).Distinct(StringComparer.Ordinal))
            {
                Subscription? subscription = await _Database.Subscriptions.ReadAsync(id, token).ConfigureAwait(false);
                if (subscription != null) byId[id] = subscription;
            }
            return page.With(page.Objects.Select(e => new MembershipHistoryItem
            {
                Id = e.Id,
                Source = e.Source,
                Store = e.Source == MembershipSourceEnum.Subscription && byId.TryGetValue(e.SourceId, out Subscription? s) ? s.Store : null,
                StartsUtc = e.StartsUtc,
                EndsUtc = e.EndsUtc,
                Status = e.StatusAt(now)
            }).ToList());
        }

        #endregion

        #region Private-Methods

        private static DateTime EndKey(Entitlement e)
        {
            return e.EndsUtc ?? DateTime.MaxValue;
        }

        #endregion
    }
}
