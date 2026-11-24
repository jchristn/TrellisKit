namespace TrellisKit.Server.Services.Messaging
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Account export sections of direct messaging (docs/PHASE5_CONTRACT.md section 12 and resolved decision 21):
    /// conversations (one row per message the member can still see in conversations they belong to: their own messages
    /// and the other member's, without content hidden for the member or removed by moderators) and messagingSettings.
    /// </summary>
    public static class MessagingExportSections
    {
        #region Public-Members

        /// <summary>
        /// Section names.
        /// </summary>
        public static readonly IReadOnlyList<string> Names = new List<string> { "conversations", "messagingSettings" };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register the sections with the export service.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public static void Register(ApplicationServices services)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.AccountExports.Register(new PagedExportSection<MessageExportRow>("conversations", (userId, after, limit, token) => MessagesAsync(services, userId, after, limit, token), 500));
            services.AccountExports.Register(new PagedExportSection<MemberMessagingSettings>("messagingSettings", (userId, after, limit, token) => SettingsAsync(services, userId, after, token)));
        }

        #endregion

        #region Private-Methods

        private static async Task<ContentExportPage<MessageExportRow>> MessagesAsync(ApplicationServices services, string userId, string? after, int limit, CancellationToken token)
        {
            List<MessageExportRow> rows = await services.Database.Messages.ExportPageAsync(userId, after, limit, token).ConfigureAwait(false);
            return new ContentExportPage<MessageExportRow> { Objects = rows, NextAfterId = rows.Count < limit ? null : rows[rows.Count - 1].Id };
        }

        private static async Task<ContentExportPage<MemberMessagingSettings>> SettingsAsync(ApplicationServices services, string userId, string? after, CancellationToken token)
        {
            if (after != null) return new ContentExportPage<MemberMessagingSettings>();
            Dictionary<string, MemberMessagingSettings> settings = await services.Database.Conversations.ReadSettingsAsync(new[] { userId }, token).ConfigureAwait(false);
            List<MemberMessagingSettings> items = new List<MemberMessagingSettings>();
            if (settings.TryGetValue(userId, out MemberMessagingSettings? row)) items.Add(row);
            return new ContentExportPage<MemberMessagingSettings> { Objects = items, NextAfterId = null };
        }

        #endregion
    }
}
