namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Registers the ideas board account export sections (docs/PHASE5_CONTRACT.md section 12): ideaVotes and ideas, each
    /// streamed in bounded batches, and adds the member's ideas to the metadata section.
    /// </summary>
    public static class IdeaExportSections
    {
        #region Public-Members

        /// <summary>
        /// Section names in registration order.
        /// </summary>
        public static readonly IReadOnlyList<string> Names = new List<string> { "ideaVotes", "ideas" };

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
            exports.Register(new PagedExportSection<IdeaVoteRecord>("ideaVotes", (userId, after, limit, token) => VotesAsync(services, userId, after, limit, token), 500));
            exports.Register(new PagedExportSection<IdeaExportItem>("ideas", (userId, after, limit, token) => IdeasAsync(services, userId, after, limit, token)));
            MetadataExportSection? metadata = exports.Sections.OfType<MetadataExportSection>().FirstOrDefault();
            if (metadata != null) metadata.AddOwnedType((userId, token) => OwnedIdeasAsync(services, userId, token));
        }

        #endregion

        #region Private-Methods

        private static async Task<ContentExportPage<IdeaVoteRecord>> VotesAsync(ApplicationServices services, string userId, string? after, int limit, CancellationToken token)
        {
            List<IdeaVoteRecord> votes = await services.Database.Ideas.ListVotesByUserAsync(userId, after, limit, token).ConfigureAwait(false);
            return new ContentExportPage<IdeaVoteRecord> { Objects = votes, NextAfterId = votes.Count < limit ? null : votes[votes.Count - 1].Id };
        }

        private static async Task<ContentExportPage<IdeaExportItem>> IdeasAsync(ApplicationServices services, string userId, string? after, int limit, CancellationToken token)
        {
            List<Idea> ideas = await services.Database.Ideas.ListByAuthorAsync(userId, after, limit, token).ConfigureAwait(false);
            List<IdeaExportItem> items = ideas.Select(i => new IdeaExportItem
            {
                Id = i.Id,
                Title = i.Title,
                Body = i.Body,
                Status = i.StatusKey,
                Audience = i.Audience,
                VoteCount = i.VoteCount,
                CreatedUtc = i.CreatedUtc,
                EditedUtc = i.EditedUtc
            }).ToList();
            return new ContentExportPage<IdeaExportItem> { Objects = items, NextAfterId = ideas.Count < limit ? null : ideas[ideas.Count - 1].Id };
        }

        private static async IAsyncEnumerable<MetadataTarget> OwnedIdeasAsync(ApplicationServices services, string userId, [EnumeratorCancellation] CancellationToken token)
        {
            string? after = null;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                List<Idea> ideas = await services.Database.Ideas.ListByAuthorAsync(userId, after, 200, token).ConfigureAwait(false);
                foreach (Idea idea in ideas) yield return new MetadataTarget { TargetType = TargetTypeEnum.Idea, TargetId = idea.Id, OwnerUserId = userId };
                if (ideas.Count < 200) yield break;
                after = ideas[ideas.Count - 1].Id;
            }
        }

        #endregion
    }
}
