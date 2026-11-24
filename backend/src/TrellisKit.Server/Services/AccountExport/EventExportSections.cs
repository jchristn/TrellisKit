namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Registers the Phase 4 event account export sections (docs/PHASE4_CONTRACT.md section 10): rsvps (streamed in
    /// bounded batches) and calendarFeed (created date only, never the secret).
    /// </summary>
    public static class EventExportSections
    {
        #region Public-Members

        /// <summary>
        /// Section names in registration order.
        /// </summary>
        public static readonly IReadOnlyList<string> Names = new List<string> { "rsvps", "calendarFeed" };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register the sections on the services' account export service (call once at startup).
        /// </summary>
        /// <param name="services">Application services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public static void Register(ApplicationServices services)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.AccountExports.Register(new PagedExportSection<EventRsvpExportItem>("rsvps", (userId, after, limit, token) => RsvpsAsync(services, userId, after, limit, token), 500));
            services.AccountExports.Register(new PagedExportSection<CalendarFeedExportItem>("calendarFeed", (userId, after, limit, token) => FeedAsync(services, userId, after, token), 10));
        }

        #endregion

        #region Private-Methods

        private static async Task<ContentExportPage<EventRsvpExportItem>> RsvpsAsync(ApplicationServices services, string userId, string? after, int limit, CancellationToken token)
        {
            List<EventRsvpRecord> rows = await services.Database.Events.Rsvps.ListForUserAsync(userId, after, limit, token).ConfigureAwait(false);
            List<EventRsvpExportItem> items = rows.Select(r => new EventRsvpExportItem
            {
                Id = r.Rsvp.Id,
                EventId = r.Rsvp.EventId,
                EventTitle = r.EventTitle,
                EventStartUtc = r.EventStartUtc,
                EventStatus = r.EventStatus,
                Status = r.Rsvp.Status,
                GuestCount = r.Rsvp.GuestCount,
                HideFromAttendeeLists = r.Rsvp.HideFromAttendeeLists,
                CheckedInUtc = r.Rsvp.CheckedInUtc,
                CreatedUtc = r.Rsvp.CreatedUtc,
                UpdatedUtc = r.Rsvp.UpdatedUtc
            }).ToList();
            return new ContentExportPage<EventRsvpExportItem> { Objects = items, NextAfterId = rows.Count < limit ? null : rows[rows.Count - 1].Rsvp.Id };
        }

        private static async Task<ContentExportPage<CalendarFeedExportItem>> FeedAsync(ApplicationServices services, string userId, string? after, CancellationToken token)
        {
            if (after != null) return new ContentExportPage<CalendarFeedExportItem>();
            CalendarFeed? feed = await services.Database.Events.Feeds.ReadForUserAsync(userId, token).ConfigureAwait(false);
            List<CalendarFeedExportItem> items = feed == null ? new List<CalendarFeedExportItem>() : new List<CalendarFeedExportItem> { new CalendarFeedExportItem { CreatedUtc = feed.CreatedUtc, RotatedUtc = feed.RotatedUtc, IsEnabled = feed.IsEnabled } };
            return new ContentExportPage<CalendarFeedExportItem> { Objects = items, NextAfterId = null };
        }

        #endregion
    }
}
