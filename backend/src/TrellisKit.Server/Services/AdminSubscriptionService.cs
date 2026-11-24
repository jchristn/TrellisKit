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
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Admin subscription list and detail, and membership metrics.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class AdminSubscriptionService
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly PropertyMembership _Product;
        private readonly EnumerationService _Enumeration;
        private readonly MetadataService _Metadata;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="product">The property membership product (price for estimated ARR).</param>
        /// <param name="enumeration">Enumeration service.</param>
        /// <param name="metadata">Labels and tags.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public AdminSubscriptionService(DatabaseDriverBase database, PropertyMembership product, EnumerationService enumeration, MetadataService metadata)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Product = product ?? throw new ArgumentNullException(nameof(product));
            _Enumeration = enumeration ?? throw new ArgumentNullException(nameof(enumeration));
            _Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// GET /api/v1/admin/subscriptions (collection <see cref="PhaseOneCollections.Subscriptions"/>).
        /// </summary>
        /// <param name="query">Enumeration query; filters store, status, userId.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page with labels and tags (staff view).</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken.</exception>
        public async Task<EnumerationResult<AdminSubscriptionItem>> ListAsync(EnumerationQuery query, CancellationToken token = default)
        {
            EnumerationRequest request = _Enumeration.Prepare(query, PhaseOneCollections.Subscriptions, true);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            SubscriptionFilter filter = new SubscriptionFilter
            {
                Store = request.Query.FilterEnum<StoreEnum>("store", errors),
                Status = request.Query.FilterEnum<SubscriptionStatusEnum>("status", errors),
                UserId = request.Query.FilterString("userId")
            };
            if (errors.Count > 0) throw ApiErrorException.Validation(errors, "One or more filters are invalid.");
            EnumerationResult<Subscription> page = await _Database.Subscriptions.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            Dictionary<string, User> users = (await _Database.Users.ReadManyAsync(page.Objects.Select(s => s.UserId), token).ConfigureAwait(false)).ToDictionary(u => u.Id);
            List<AdminSubscriptionItem> items = page.Objects.Select(s => MemberViewBuilder.ToItem(s, users.GetValueOrDefault(s.UserId))).ToList();
            await _Metadata.AttachAsync(TargetTypeEnum.Subscription, items, i => i.Id, true, token).ConfigureAwait(false);
            return page.With(items);
        }

        /// <summary>
        /// GET /api/v1/admin/subscriptions/{id}: item fields, storeSubscriptionId, labels and tags, and a bounded preview
        /// of the newest events (at most 5, with totalRecords and the enumerate path).
        /// </summary>
        /// <param name="id">Subscription identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Detail.</returns>
        /// <exception cref="ApiErrorException">404.</exception>
        public async Task<AdminSubscriptionDetail> DetailAsync(string id, CancellationToken token = default)
        {
            Subscription subscription = await RequireAsync(id, token).ConfigureAwait(false);
            User? user = await _Database.Users.ReadAsync(subscription.UserId, token).ConfigureAwait(false);
            AdminSubscriptionDetail detail = new AdminSubscriptionDetail { StoreSubscriptionId = subscription.StoreSubscriptionId };
            MemberViewBuilder.FillItem(detail, subscription, user);
            await _Metadata.AttachOneAsync(TargetTypeEnum.Subscription, detail, subscription.Id, true, token).ConfigureAwait(false);
            EnumerationRequest preview = _Enumeration.Prepare(new EnumerationQuery { MaxResults = BoundedPreview<SubscriptionEventView>.MaxObjects }, PhaseOneCollections.SubscriptionEvents, true, "events|" + subscription.Id);
            detail.Events = BoundedPreview<SubscriptionEventView>.From(await EventsPageAsync(subscription.Id, preview, token).ConfigureAwait(false),
                TrellisKit.Core.Constants.ApiPrefix + "/admin/subscriptions/" + subscription.Id + "/events/enumerate");
            return detail;
        }

        /// <summary>
        /// POST /api/v1/admin/subscriptions/{id}/events/enumerate (collection <see cref="PhaseOneCollections.SubscriptionEvents"/>;
        /// filter outcome).
        /// </summary>
        /// <param name="id">Subscription identifier.</param>
        /// <param name="query">Enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of events with raw payloads.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 404 NotFound.</exception>
        public async Task<EnumerationResult<SubscriptionEventView>> EnumerateEventsAsync(string id, EnumerationQuery query, CancellationToken token = default)
        {
            Subscription subscription = await RequireAsync(id, token).ConfigureAwait(false);
            return await EventsPageAsync(subscription.Id, _Enumeration.Prepare(query, PhaseOneCollections.SubscriptionEvents, true, "events|" + subscription.Id), token).ConfigureAwait(false);
        }

        /// <summary>
        /// Membership metrics.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Metrics.</returns>
        public async Task<MembershipMetrics> MetricsAsync(CancellationToken token = default)
        {
            DateTime now = DateTime.UtcNow;
            long paid = await _Database.Entitlements.CountActiveUsersAsync(now, MembershipSourceEnum.Subscription, token).ConfigureAwait(false);
            Dictionary<StoreEnum, long> byStore = await _Database.Entitlements.CountActiveUsersByStoreAsync(now, token).ConfigureAwait(false);
            MembershipMetrics metrics = new MembershipMetrics
            {
                TotalMembers = await _Database.Users.CountAsync(null, token).ConfigureAwait(false),
                ActiveMembers = await _Database.Entitlements.CountActiveUsersAsync(now, null, token).ConfigureAwait(false),
                PaidActive = paid,
                FreeActive = await _Database.Entitlements.CountFreeOnlyUsersAsync(now, token).ConfigureAwait(false),
                NewMembers30d = await _Database.Users.CountAsync(now.AddDays(-30), token).ConfigureAwait(false),
                EstimatedArrUsd = Math.Round(paid * AnnualCents(_Product) / 100m, 2),
                Lapsed30d = await _Database.Entitlements.CountLapsedUsersAsync(now.AddDays(-30), now, token).ConfigureAwait(false)
            };
            foreach (StoreEnum store in Enum.GetValues<StoreEnum>()) metrics.ByStore[store.ToString()] = byStore.GetValueOrDefault(store);
            foreach (MembershipSourceEnum source in Enum.GetValues<MembershipSourceEnum>())
            {
                metrics.BySource[source.ToString()] = await _Database.Entitlements.CountActiveUsersAsync(now, source, token).ConfigureAwait(false);
            }
            return metrics;
        }

        #endregion

        #region Private-Methods

        private async Task<Subscription> RequireAsync(string id, CancellationToken token)
        {
            return (String.IsNullOrWhiteSpace(id) ? null : await _Database.Subscriptions.ReadAsync(id, token).ConfigureAwait(false))
                ?? throw ApiErrorException.NotFound("Subscription");
        }

        private async Task<EnumerationResult<SubscriptionEventView>> EventsPageAsync(string subscriptionId, EnumerationRequest request, CancellationToken token)
        {
            EnumerationResult<SubscriptionEvent> page = await _Database.SubscriptionEvents.EnumerateBySubscriptionAsync(subscriptionId, request, request.Query.FilterString("outcome"), token).ConfigureAwait(false);
            return page.With(page.Objects.Select(e => new SubscriptionEventView
            {
                Id = e.Id,
                Source = e.Source,
                Type = e.EventType,
                ReceivedUtc = e.ReceivedUtc,
                ProcessedUtc = e.ProcessedUtc,
                Outcome = e.Outcome,
                RawPayload = e.RawPayloadJson
            }).ToList());
        }

        private static long AnnualCents(PropertyMembership product)
        {
            return String.Equals(product.Interval, "Month", StringComparison.Ordinal) ? product.PriceUsdCents * 12L : product.PriceUsdCents;
        }

        #endregion
    }
}
