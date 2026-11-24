namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Registers the Phase 2 account export sections (contract section 11): feedPosts and comments (with revisions),
    /// reactions, notificationPreferences, pushTokens, blocks (members the member blocked), reports (filed by the
    /// member), and media (metadata only), each streamed in bounded batches, and adds the member's posts, comments, and
    /// media to the metadata section.
    /// </summary>
    public static class ContentExportSections
    {
        #region Public-Members

        /// <summary>
        /// Section names in registration order.
        /// </summary>
        public static readonly IReadOnlyList<string> Names = new List<string> { "feedPosts", "comments", "reactions", "notificationPreferences", "pushTokens", "blocks", "reports", "media" };

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
            AccountExportService exports = services.AccountExports;
            exports.Register(new PagedExportSection<FeedPostExportItem>("feedPosts", (userId, after, limit, token) => services.ContentLifecycle.ExportFeedPostsAsync(userId, after, limit, token)));
            exports.Register(new PagedExportSection<CommentExportItem>("comments", (userId, after, limit, token) => services.ContentLifecycle.ExportCommentsAsync(userId, after, limit, token)));
            exports.Register(new PagedExportSection<ReactionExportItem>("reactions", (userId, after, limit, token) => services.ContentLifecycle.ExportReactionsAsync(userId, after, limit, token), 500));
            exports.Register(new NotificationSettingsExportSection(services.NotificationPreferences, false));
            exports.Register(new NotificationSettingsExportSection(services.NotificationPreferences, true));
            exports.Register(new PagedExportSection<BlockExportItem>("blocks", (userId, after, limit, token) => BlocksAsync(services, userId, after, limit, token)));
            exports.Register(new PagedExportSection<ReportExportItem>("reports", (userId, after, limit, token) => ReportsAsync(services, userId, after, limit, token)));
            exports.Register(new PagedExportSection<MediaExportItem>("media", (userId, after, limit, token) => MediaAsync(services, userId, after, limit, token)));
            MetadataExportSection? metadata = exports.Sections.OfType<MetadataExportSection>().FirstOrDefault();
            if (metadata != null)
            {
                metadata.AddOwnedType((userId, token) => OwnedAsync(services, TargetTypeEnum.FeedPost, userId, token));
                metadata.AddOwnedType((userId, token) => OwnedAsync(services, TargetTypeEnum.Comment, userId, token));
                metadata.AddOwnedType((userId, token) => OwnedAsync(services, TargetTypeEnum.Media, userId, token));
            }
        }

        #endregion

        #region Private-Methods

        private static async Task<ContentExportPage<BlockExportItem>> BlocksAsync(ApplicationServices services, string userId, string? after, int limit, CancellationToken token)
        {
            EnumerationQuery query = new EnumerationQuery { MaxResults = limit, ContinuationToken = after, Ordering = "CreatedAscending" };
            EnumerationRequest request = services.Enumeration.Prepare(query, SafetyCollections.Blocks, false, "blocks|" + userId);
            EnumerationResult<Block> page = await services.Database.Blocks.EnumerateByBlockerAsync(userId, request, token).ConfigureAwait(false);
            Dictionary<string, UserSummary> users = await services.Summaries.BuildAsync(page.Objects.Select(b => (string?)b.BlockedUserId), token).ConfigureAwait(false);
            return new ContentExportPage<BlockExportItem>
            {
                Objects = page.Objects.Select(b => new BlockExportItem { User = users.TryGetValue(b.BlockedUserId, out UserSummary? u) ? u : UserSummary.Deleted(), CreatedUtc = b.CreatedUtc }).ToList(),
                NextAfterId = page.EndOfResults ? null : page.ContinuationToken
            };
        }

        private static async Task<ContentExportPage<ReportExportItem>> ReportsAsync(ApplicationServices services, string userId, string? after, int limit, CancellationToken token)
        {
            List<Report> reports = await services.Database.Reports.ListByReporterAsync(userId, after, limit, token).ConfigureAwait(false);
            return new ContentExportPage<ReportExportItem>
            {
                Objects = reports.Select(r => new ReportExportItem
                {
                    Id = r.Id,
                    TargetType = r.TargetType.ToString(),
                    TargetId = r.TargetId,
                    Reason = r.Reason.ToString(),
                    Details = r.Details,
                    Status = r.Status.ToString(),
                    CreatedUtc = r.CreatedUtc,
                    ResolvedUtc = r.ResolvedUtc
                }).ToList(),
                NextAfterId = reports.Count < limit ? null : reports[reports.Count - 1].Id
            };
        }

        private static async Task<ContentExportPage<MediaExportItem>> MediaAsync(ApplicationServices services, string userId, string? after, int limit, CancellationToken token)
        {
            List<MediaAttachment> media = await services.Database.Media.ListByOwnerAfterAsync(userId, after, limit, token).ConfigureAwait(false);
            return new ContentExportPage<MediaExportItem>
            {
                Objects = media.Select(m => new MediaExportItem
                {
                    Id = m.Id,
                    Kind = m.Kind.ToString(),
                    Status = m.Status.ToString(),
                    ContentType = m.ContentType,
                    OriginalFileName = m.OriginalFileName,
                    SizeBytes = m.SizeBytes,
                    Width = m.Width,
                    Height = m.Height,
                    DurationSeconds = m.DurationSeconds,
                    AltText = m.AltText,
                    AttachedTargetType = m.AttachedTargetType?.ToString(),
                    AttachedTargetId = m.AttachedTargetId,
                    CreatedUtc = m.CreatedUtc,
                    DeletedUtc = m.DeletedUtc
                }).ToList(),
                NextAfterId = media.Count < limit ? null : media[media.Count - 1].Id
            };
        }

        private static async IAsyncEnumerable<MetadataTarget> OwnedAsync(ApplicationServices services, TargetTypeEnum type, string userId, [EnumeratorCancellation] CancellationToken token)
        {
            string? after = null;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                List<string> ids;
                HashSet<string> skip = new HashSet<string>(StringComparer.Ordinal);
                if (type == TargetTypeEnum.FeedPost) ids = (await services.Database.FeedPosts.ListByAuthorAsync(userId, after, 200, false, token).ConfigureAwait(false)).Select(p => p.Id).ToList();
                else if (type == TargetTypeEnum.Comment) ids = (await services.Database.Comments.ListByAuthorAsync(userId, after, 200, true, token).ConfigureAwait(false)).Select(c => c.Id).ToList();
                else
                {
                    List<MediaAttachment> media = await services.Database.Media.ListByOwnerAfterAsync(userId, after, 200, token).ConfigureAwait(false);
                    ids = media.Select(m => m.Id).ToList();
                    foreach (MediaAttachment m in media) if (m.DeletedUtc != null) skip.Add(m.Id);
                }
                foreach (string id in ids) if (!skip.Contains(id)) yield return new MetadataTarget { TargetType = type, TargetId = id, OwnerUserId = userId };
                if (ids.Count < 200) yield break;
                after = ids[ids.Count - 1];
            }
        }

        #endregion
    }
}
