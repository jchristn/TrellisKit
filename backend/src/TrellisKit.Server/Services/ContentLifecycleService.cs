namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Account lifecycle of a member's content (contract section 11). <see cref="EraseForAccountAsync"/> runs from
    /// account deletion: comments become Deleted placeholders with bodies, revisions, mentions, labels, and tags cleared
    /// immediately (deletedreason AccountDeleted, counts recomputed), feed posts are soft-deleted (labels, tags, pins,
    /// and public copies removed), reactions are deleted (counts recomputed), mentions of the member are deleted, and the
    /// media attached to the member's posts and comments is purged including originals (account deletion erases
    /// immediately). The export methods page through the member's posts (with revisions), comments (with revisions), and
    /// reactions in keyset batches by identifier, for the account export to call until NextAfterId is null.
    /// Thread safety: safe to call concurrently; every step is idempotent, so a retried deletion finishes the job.
    /// </summary>
    public class ContentLifecycleService
    {
        #region Public-Members

        /// <summary>
        /// Rows per batch while erasing. Default 200, minimum 10, maximum 5000.
        /// </summary>
        public int BatchSize
        {
            get { return _BatchSize; }
            set { _BatchSize = Math.Clamp(value, 10, 5000); }
        }

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;
        private int _BatchSize = 200;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="services">Application services (read on each call).</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public ContentLifecycleService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Erase a member's content for account deletion (see the class summary). Call it before or after the user row
        /// is anonymized; it does not touch the user row.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>What was erased.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        public async Task<ContentErasureResult> EraseForAccountAsync(string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            ContentErasureResult result = new ContentErasureResult();
            using (Activity? activity = TrellisKitTelemetry.StartSpan("content.erase_account"))
            {
                DateTime now = DateTime.UtcNow;
                string? after = null;
                while (true)
                {
                    List<Comment> comments = await _Services.Database.Comments.ListByAuthorAsync(userId, after, _BatchSize, true, token).ConfigureAwait(false);
                    if (comments.Count == 0) break;
                    foreach (Comment comment in comments)
                    {
                        token.ThrowIfCancellationRequested();
                        await _Services.Database.Comments.DeleteAsync(comment.Id, CommentDeletedReasonEnum.AccountDeleted, true, now, token).ConfigureAwait(false);
                        await _Services.Media.PurgeTargetAsync(TargetTypeEnum.Comment, comment.Id, token).ConfigureAwait(false);
                        result.Comments++;
                    }
                    after = comments[comments.Count - 1].Id;
                }

                after = null;
                while (true)
                {
                    List<FeedPost> posts = await _Services.Database.FeedPosts.ListByAuthorAsync(userId, after, _BatchSize, true, token).ConfigureAwait(false);
                    if (posts.Count == 0) break;
                    foreach (FeedPost post in posts)
                    {
                        token.ThrowIfCancellationRequested();
                        if (await _Services.Feed.DeleteCoreAsync(post, userId, token).ConfigureAwait(false)) result.FeedPosts++;
                        await _Services.Media.PurgeTargetAsync(TargetTypeEnum.FeedPost, post.Id, token).ConfigureAwait(false);
                    }
                    after = posts[posts.Count - 1].Id;
                }

                while (true)
                {
                    List<Reaction> reactions = await _Services.Database.Reactions.ListByUserAsync(userId, null, _BatchSize, token).ConfigureAwait(false);
                    if (reactions.Count == 0) break;
                    foreach (IGrouping<string, Reaction> target in reactions.GroupBy(r => r.TargetType + "|" + r.TargetId))
                    {
                        token.ThrowIfCancellationRequested();
                        Reaction first = target.First();
                        await _Services.Database.Reactions.RemoveAllForUserOnTargetAsync(first.TargetType, first.TargetId, userId, token).ConfigureAwait(false);
                    }
                    result.Reactions += reactions.Count;
                }

                result.Mentions = await _Services.Database.Mentions.DeleteForMentionedUserAsync(userId, token).ConfigureAwait(false);
                activity?.SetStatus(ActivityStatusCode.Ok);
                return result;
            }
        }

        /// <summary>
        /// One keyset batch of a member's feed posts (every state, including deleted) with their revisions, oldest
        /// identifier first.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="afterId">NextAfterId of the previous batch, or null.</param>
        /// <param name="limit">Batch size (1 to 500).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Batch.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        public async Task<ContentExportPage<FeedPostExportItem>> ExportFeedPostsAsync(string userId, string? afterId, int limit = 100, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            int size = Math.Clamp(limit, 1, 500);
            List<FeedPost> posts = await _Services.Database.FeedPosts.ListByAuthorAsync(userId, afterId, size, true, token).ConfigureAwait(false);
            List<FeedPostRevision> revisions = await _Services.Database.FeedPosts.ListRevisionsAsync(posts.Select(p => p.Id), token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            return new ContentExportPage<FeedPostExportItem>
            {
                Objects = posts.Select(p => new FeedPostExportItem
                {
                    Id = p.Id,
                    Title = p.Title,
                    Body = p.Body,
                    Visibility = p.Visibility,
                    State = p.StateAt(now),
                    CreatedUtc = p.CreatedUtc,
                    PublishedUtc = p.PublishedUtc,
                    EditedUtc = p.EditedUtc,
                    DeletedUtc = p.DeletedUtc,
                    Revisions = revisions.Where(r => r.FeedPostId == p.Id).Select(r => new FeedPostRevisionView
                    {
                        Id = r.Id,
                        Revision = r.Revision,
                        Title = r.Title,
                        Body = r.Body,
                        AttachmentSummary = r.AttachmentSummary,
                        EditedByUserId = r.EditedByUserId,
                        CreatedUtc = r.CreatedUtc
                    }).ToList()
                }).ToList(),
                NextAfterId = posts.Count < size ? null : posts[posts.Count - 1].Id
            };
        }

        /// <summary>
        /// One keyset batch of a member's comments (every state) with their revisions, oldest identifier first.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="afterId">NextAfterId of the previous batch, or null.</param>
        /// <param name="limit">Batch size (1 to 500).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Batch.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        public async Task<ContentExportPage<CommentExportItem>> ExportCommentsAsync(string userId, string? afterId, int limit = 100, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            int size = Math.Clamp(limit, 1, 500);
            List<Comment> comments = await _Services.Database.Comments.ListByAuthorAsync(userId, afterId, size, false, token).ConfigureAwait(false);
            List<CommentRevision> revisions = await _Services.Database.Comments.ListRevisionsAsync(comments.Select(c => c.Id), token).ConfigureAwait(false);
            return new ContentExportPage<CommentExportItem>
            {
                Objects = comments.Select(c => new CommentExportItem
                {
                    Id = c.Id,
                    TargetType = c.TargetType.ToString(),
                    TargetId = c.TargetId,
                    ParentCommentId = c.ParentCommentId,
                    Body = c.Body,
                    CreatedUtc = c.CreatedUtc,
                    EditedUtc = c.EditedUtc,
                    DeletedUtc = c.DeletedUtc,
                    Revisions = revisions.Where(r => r.CommentId == c.Id).Select(r => new CommentRevisionView
                    {
                        Id = r.Id,
                        Revision = r.Revision,
                        Body = r.Body,
                        AttachmentSummary = r.AttachmentSummary,
                        CreatedUtc = r.CreatedUtc
                    }).ToList()
                }).ToList(),
                NextAfterId = comments.Count < size ? null : comments[comments.Count - 1].Id
            };
        }

        /// <summary>
        /// One keyset batch of a member's reactions, oldest identifier first.
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="afterId">NextAfterId of the previous batch, or null.</param>
        /// <param name="limit">Batch size (1 to 1000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Batch.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        public async Task<ContentExportPage<ReactionExportItem>> ExportReactionsAsync(string userId, string? afterId, int limit = 500, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            int size = Math.Clamp(limit, 1, 1000);
            List<Reaction> reactions = await _Services.Database.Reactions.ListByUserAsync(userId, afterId, size, token).ConfigureAwait(false);
            return new ContentExportPage<ReactionExportItem>
            {
                Objects = reactions.Select(r => new ReactionExportItem { TargetType = r.TargetType.ToString(), TargetId = r.TargetId, Key = r.Key, CreatedUtc = r.CreatedUtc }).ToList(),
                NextAfterId = reactions.Count < size ? null : reactions[reactions.Count - 1].Id
            };
        }

        #endregion
    }
}
