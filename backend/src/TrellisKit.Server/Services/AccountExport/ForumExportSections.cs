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
    /// Registers the Phase 3 account export sections (contract section 13): threads (with revisions), pollVotes,
    /// follows, and forumMutes, each streamed in bounded batches, and adds the member's threads to the metadata section.
    /// </summary>
    public static class ForumExportSections
    {
        #region Public-Members

        /// <summary>
        /// Section names in registration order.
        /// </summary>
        public static readonly IReadOnlyList<string> Names = new List<string> { "threads", "pollVotes", "follows", "forumMutes" };

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
            exports.Register(new PagedExportSection<ThreadExportItem>("threads", (userId, after, limit, token) => ThreadsAsync(services, userId, after, limit, token)));
            exports.Register(new PagedExportSection<PollVoteRecord>("pollVotes", (userId, after, limit, token) => PollVotesAsync(services, userId, after, limit, token), 500));
            exports.Register(new PagedExportSection<FollowRow>("follows", (userId, after, limit, token) => FollowsAsync(services, userId, after, limit, token), 500));
            exports.Register(new PagedExportSection<ForumMute>("forumMutes", (userId, after, limit, token) => MutesAsync(services, userId, after, limit, token)));
            MetadataExportSection? metadata = exports.Sections.OfType<MetadataExportSection>().FirstOrDefault();
            if (metadata != null) metadata.AddOwnedType((userId, token) => OwnedThreadsAsync(services, userId, token));
        }

        #endregion

        #region Private-Methods

        private static async Task<ContentExportPage<ThreadExportItem>> ThreadsAsync(ApplicationServices services, string userId, string? after, int limit, CancellationToken token)
        {
            List<ForumThread> threads = (await services.Database.Threads.ListByAuthorAsync(userId, after, limit, true, token).ConfigureAwait(false));
            List<ThreadRevision> revisions = threads.Count == 0 ? new List<ThreadRevision>() : await services.Database.Threads.ListRevisionsAsync(threads.Select(t => t.Id), token).ConfigureAwait(false);
            List<ThreadExportItem> items = new List<ThreadExportItem>();
            foreach (ForumThread thread in threads)
            {
                if (thread.PurgedUtc != null || thread.Kind != ThreadKindEnum.Original) continue;
                items.Add(new ThreadExportItem
                {
                    Id = thread.Id,
                    ForumId = thread.ForumId,
                    Type = thread.Type,
                    Title = thread.Title,
                    Body = thread.Body,
                    FeedbackFocus = thread.FeedbackFocus,
                    ModerationState = thread.ModerationState,
                    CreatedUtc = thread.CreatedUtc,
                    EditedUtc = thread.EditedUtc,
                    DeletedUtc = thread.DeletedUtc,
                    Revisions = revisions.Where(r => String.Equals(r.ThreadId, thread.Id, StringComparison.Ordinal)).OrderBy(r => r.Revision).ToList()
                });
            }
            return new ContentExportPage<ThreadExportItem> { Objects = items, NextAfterId = threads.Count < limit ? null : threads[threads.Count - 1].Id };
        }

        private static async Task<ContentExportPage<PollVoteRecord>> PollVotesAsync(ApplicationServices services, string userId, string? after, int limit, CancellationToken token)
        {
            List<PollVoteRecord> votes = await services.Database.Polls.ListVotesByUserAsync(userId, after, limit, token).ConfigureAwait(false);
            return new ContentExportPage<PollVoteRecord> { Objects = votes, NextAfterId = votes.Count < limit ? null : votes[votes.Count - 1].Id };
        }

        private static async Task<ContentExportPage<FollowRow>> FollowsAsync(ApplicationServices services, string userId, string? after, int limit, CancellationToken token)
        {
            EnumerationQuery query = new EnumerationQuery { MaxResults = limit, ContinuationToken = after };
            EnumerationRequest request = services.Enumeration.Prepare(query, ForumCollections.Follows, false, "follows|" + userId);
            EnumerationResult<FollowRow> page = await services.Database.Threads.EnumerateFollowsAsync(userId, request, null, token).ConfigureAwait(false);
            return new ContentExportPage<FollowRow> { Objects = page.Objects, NextAfterId = page.EndOfResults ? null : page.ContinuationToken };
        }

        private static async Task<ContentExportPage<ForumMute>> MutesAsync(ApplicationServices services, string userId, string? after, int limit, CancellationToken token)
        {
            if (after != null) return new ContentExportPage<ForumMute>();
            List<ForumMute> mutes = await services.Database.Forums.ListMutesForUserAsync(userId, 1000, token).ConfigureAwait(false);
            foreach (ForumMute mute in mutes)
            {
                mute.MutedByUserId = null;
                mute.EndedByUserId = null;
            }
            return new ContentExportPage<ForumMute> { Objects = mutes, NextAfterId = null };
        }

        private static async IAsyncEnumerable<MetadataTarget> OwnedThreadsAsync(ApplicationServices services, string userId, [EnumeratorCancellation] CancellationToken token)
        {
            string? after = null;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                List<ForumThread> threads = await services.Database.Threads.ListByAuthorAsync(userId, after, 200, false, token).ConfigureAwait(false);
                foreach (ForumThread thread in threads) yield return new MetadataTarget { TargetType = TargetTypeEnum.Thread, TargetId = thread.Id, OwnerUserId = userId };
                if (threads.Count < 200) yield break;
                after = threads[threads.Count - 1].Id;
            }
        }

        #endregion
    }
}
