namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// The member view of broadcasts (docs/PHASE5_CONTRACT.md section 8.2): a signed-in account reads a broadcast only
    /// when it has a delivery of it (404 otherwise, so ids do not leak), and lists the broadcasts it received, newest first.
    /// /announcements* is a SignedIn route group, reachable in every access model.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class AnnouncementService
    {
        #region Private-Members

        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public AnnouncementService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// GET /api/v1/announcements/{id}.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Broadcast.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The announcement.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden (no member session), 404 NotFound or NotAvailable.</exception>
        public async Task<AnnouncementView> GetAsync(ContentActor actor, string id, CancellationToken token = default)
        {
            string userId = Require(actor);
            BroadcastDelivery? delivery = String.IsNullOrWhiteSpace(id) ? null : await _Services.Database.Broadcasts.ReadDeliveryAsync(id.Trim(), userId, token).ConfigureAwait(false);
            Broadcast? broadcast = delivery == null ? null : await _Services.Database.Broadcasts.ReadAsync(delivery.BroadcastId, token).ConfigureAwait(false);
            if (broadcast == null || broadcast.StartedUtc == null) throw ApiErrorException.NotFound("Announcement");
            return await _Services.Metadata.AttachOneAsync(TargetTypeEnum.Broadcast, View(broadcast), broadcast.Id, false, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/announcements/enumerate (and GET /api/v1/announcements): the caller's received broadcasts.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 403 Forbidden, 404 NotAvailable.</exception>
        public async Task<EnumerationResult<AnnouncementView>> EnumerateAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            string userId = Require(actor);
            EnumerationRequest request = _Services.Enumeration.Prepare(query, BroadcastCollections.Announcements, false, userId);
            EnumerationResult<Broadcast> page = await _Services.Database.Broadcasts.EnumerateReceivedAsync(request, userId, token).ConfigureAwait(false);
            List<AnnouncementView> views = page.Objects.Select(View).ToList();
            await _Services.Metadata.AttachAsync(TargetTypeEnum.Broadcast, views, v => v.Id, false, token).ConfigureAwait(false);
            return page.With(views);
        }

        #endregion

        #region Private-Methods

        private string Require(ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (!_Services.Property.Property.Features.Broadcasts) throw new ApiErrorException(404, "NotAvailable", "Announcements are not available in this community.");
            return actor.RequireUserId();
        }

        private static AnnouncementView View(Broadcast broadcast)
        {
            return new AnnouncementView
            {
                Id = broadcast.Id,
                Title = broadcast.InAppTitle ?? broadcast.Title,
                Body = broadcast.InAppBody ?? String.Empty,
                SentUtc = broadcast.SentUtc ?? broadcast.StartedUtc
            };
        }

        #endregion
    }
}
