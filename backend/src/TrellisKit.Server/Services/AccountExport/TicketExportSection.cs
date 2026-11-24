namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Registers the tickets account export section (docs/PHASE5_CONTRACT.md section 12): the member's own tickets with
    /// their replies, streamed in bounded batches. Internal notes and staff timeline lines are never exported.
    /// </summary>
    public static class TicketExportSection
    {
        #region Public-Members

        /// <summary>
        /// Section name.
        /// </summary>
        public const string Name = "tickets";

        /// <summary>
        /// Most replies exported per batch of tickets.
        /// </summary>
        public const int MaxRepliesPerBatch = 50000;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register the section (call once at startup).
        /// </summary>
        /// <param name="services">Application services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public static void Register(ApplicationServices services)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.AccountExports.Register(new PagedExportSection<TicketExportItem>(Name, (userId, after, limit, token) => PageAsync(services, userId, after, limit, token), 50));
        }

        #endregion

        #region Private-Methods

        private static async Task<ContentExportPage<TicketExportItem>> PageAsync(ApplicationServices services, string userId, string? after, int limit, CancellationToken token)
        {
            List<Ticket> tickets = await services.Database.Support.ListByRequesterAsync(userId, after, limit, token).ConfigureAwait(false);
            List<TicketMessage> replies = await services.Database.Support.RepliesForTicketsAsync(tickets.Select(t => t.Id), MaxRepliesPerBatch, token).ConfigureAwait(false);
            List<TicketExportItem> items = tickets.Select(t => new TicketExportItem
            {
                Id = t.Id,
                Number = t.Number,
                Type = t.TypeKey,
                Title = t.Title,
                Body = t.Body,
                Status = t.Status,
                Priority = t.Priority,
                Device = t.IncludeDiagnostics ? new TicketDeviceView { Client = t.DeviceClient, ClientVersion = t.DeviceClientVersion, Platform = t.DevicePlatform, OsVersion = t.DeviceOsVersion, DeviceModel = t.DeviceModel, UpdateId = t.DeviceUpdateId, Locale = t.DeviceLocale, TimeZone = t.DeviceTimeZone } : null,
                CreatedUtc = t.CreatedUtc,
                ResolvedUtc = t.ResolvedUtc,
                ClosedUtc = t.ClosedUtc,
                Replies = replies.Where(r => r.TicketId == t.Id && r.Kind == TicketMessageKindEnum.Reply)
                    .Select(r => new TicketExportReply { Id = r.Id, AuthorRole = r.AuthorRole, Body = r.Body, CreatedUtc = r.CreatedUtc }).ToList()
            }).ToList();
            return new ContentExportPage<TicketExportItem> { Objects = items, NextAfterId = tickets.Count < limit ? null : tickets[tickets.Count - 1].Id };
        }

        #endregion
    }
}
