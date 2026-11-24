namespace TrellisKit.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Who may see feed posts and comments (contract sections 5.1 and 7.2), shared by the feed, comment, reaction, and
    /// media services. Posts: Published and unmoderated posts are visible to callers who meet the post's audience
    /// (<see cref="ContentAudienceRule"/>); the author sees every state; SystemAdmin sees every state; FeedModerators see
    /// Published, Hidden, and Removed posts; deleted posts are never visible. Comments, reactions, revisions, and attached
    /// media follow their post, so the audience applies to all of them.
    /// Comments: their post must be visible; deleted comments are placeholders; Hidden and Removed comments show their
    /// content only to the author and feed moderators. Missing or invisible content is 404 NotFound, never 403.
    /// Thread safety: stateless apart from the database; safe to call concurrently.
    /// </summary>
    public class ContentAccessService
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Database;

        #endregion

        #region Public-Members

        /// <summary>
        /// Whether the caller may see a forum thread (set by the forum module): used for thread comments and thread media.
        /// </summary>
        public Func<ContentActor, string, CancellationToken, Task<bool>>? ThreadVisible { get; set; } = null;

        /// <summary>
        /// Whether the caller may see a feed post beyond its own state and audience (set by the forum module: a promoted
        /// card needs a readable thread, Phase 3 section 8.4).
        /// </summary>
        public Func<FeedPost, ContentActor, CancellationToken, Task<bool>>? PostVisible { get; set; } = null;

        /// <summary>
        /// The content target providers (Phase 4 types): comments and media attached to their objects are visible only
        /// when the provider says the object is (docs/PHASE4_CONTRACT.md section 6). Null before wiring.
        /// </summary>
        public ContentTargetRegistry? Targets { get; set; } = null;

        /// <summary>
        /// Whether the caller may read an idea (set by the ideas module): used for idea comments and their media.
        /// </summary>
        public Func<ContentActor, string, CancellationToken, Task<bool>>? IdeaVisible { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <exception cref="ArgumentNullException">The database is null.</exception>
        public ContentAccessService(DatabaseDriverBase database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a caller may see a post.
        /// </summary>
        /// <param name="post">Post.</param>
        /// <param name="actor">Caller.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <returns>True when visible.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static bool CanSeePost(FeedPost post, ContentActor actor, DateTime nowUtc)
        {
            ArgumentNullException.ThrowIfNull(post);
            ArgumentNullException.ThrowIfNull(actor);
            FeedPostStateEnum state = post.StateAt(nowUtc);
            if (state == FeedPostStateEnum.Deleted) return false;
            if (actor.IsSystemAdmin) return true;
            if (actor.UserId != null && String.Equals(post.AuthorUserId, actor.UserId, StringComparison.Ordinal)) return true;
            if (actor.IsFeedModerator && (state == FeedPostStateEnum.Published || state == FeedPostStateEnum.Hidden || state == FeedPostStateEnum.Removed)) return true;
            return state == FeedPostStateEnum.Published && ContentAudienceRule.Meets(post.Audience, actor);
        }

        /// <summary>
        /// Whether a caller sees a comment's content (rather than a placeholder): not deleted, and unmoderated or the
        /// caller is its author or a feed moderator.
        /// </summary>
        /// <param name="comment">Comment.</param>
        /// <param name="actor">Caller.</param>
        /// <returns>True when the content is visible.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static bool CanSeeCommentContent(Comment comment, ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(comment);
            ArgumentNullException.ThrowIfNull(actor);
            if (comment.DeletedUtc != null) return false;
            if (comment.ModerationState == ModerationStateEnum.None) return true;
            if (actor.CanModerateFeed) return true;
            if (comment.TargetType == TargetTypeEnum.Thread && actor.ModeratesAnyForum) return true;
            if (comment.TargetType != TargetTypeEnum.FeedPost && comment.TargetType != TargetTypeEnum.Thread && actor.ModeratesCommentsOn(comment.TargetType)) return true;
            return actor.UserId != null && String.Equals(comment.AuthorUserId, actor.UserId, StringComparison.Ordinal);
        }

        /// <summary>
        /// Whether a caller may moderate content of an author: SystemAdmin always, FeedModerators when the author is
        /// not a SystemAdmin (decision 12).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="authorIsSystemAdmin">Whether the content's author is a SystemAdmin.</param>
        /// <returns>True when allowed.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        public static bool CanModerate(ContentActor actor, bool authorIsSystemAdmin)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (actor.IsSystemAdmin) return true;
            return actor.IsFeedModerator && !authorIsSystemAdmin;
        }

        /// <summary>
        /// Throw the access model denial for a Published, unmoderated post the caller cannot read only because of its
        /// audience (docs/ACCESS_MODELS_CONTRACT.md section 3.4): 401 Unauthorized with required SignIn for a signed-out
        /// caller, 402 MembershipRequired with required Membership for a signed-in caller who sees it as a locked stub
        /// (PartiallyGated). Returns without throwing otherwise (the caller answers 404 NotFound).
        /// </summary>
        /// <param name="post">Post.</param>
        /// <param name="actor">Caller.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">401 Unauthorized or 402 MembershipRequired.</exception>
        public static void ThrowIfLocked(FeedPost post, ContentActor actor, DateTime nowUtc)
        {
            ArgumentNullException.ThrowIfNull(post);
            ArgumentNullException.ThrowIfNull(actor);
            if (post.StateAt(nowUtc) != FeedPostStateEnum.Published || post.ModerationState != ModerationStateEnum.None) return;
            AccessTierEnum tier = AccessDecider.TierOf(actor);
            AccessTierEnum required = AccessDecider.ReadTier(actor.Access, post.Audience);
            if (AccessDecider.Meets(tier, required)) return;
            if (actor.IsAnonymous)
                throw Denied(401, "Unauthorized", "Sign in to see this.", "SignIn", post.Id);
            if (AccessDecider.ShowsLocked(actor.Access, tier, post.Audience))
                throw Denied(402, "MembershipRequired", "This is for members.", "Membership", post.Id);
        }

        /// <summary>
        /// Load a post the caller may see.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="postId">Post identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Post.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<FeedPost> RequireVisiblePostAsync(ContentActor actor, string postId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            FeedPost? post = String.IsNullOrWhiteSpace(postId) ? null : await _Database.FeedPosts.ReadAsync(postId, token).ConfigureAwait(false);
            if (post != null && !CanSeePost(post, actor, DateTime.UtcNow)) ThrowIfLocked(post, actor, DateTime.UtcNow);
            if (post == null || !CanSeePost(post, actor, DateTime.UtcNow)) throw ApiErrorException.NotFound("Feed post");
            if (PostVisible != null && !await PostVisible(post, actor, token).ConfigureAwait(false)) throw ApiErrorException.NotFound("Feed post");
            return post;
        }

        /// <summary>
        /// Load a comment whose post the caller may see (the comment itself may be a placeholder).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="commentId">Comment identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Comment.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<Comment> RequireCommentAsync(ContentActor actor, string commentId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            Comment? comment = String.IsNullOrWhiteSpace(commentId) ? null : await _Database.Comments.ReadAsync(commentId, token).ConfigureAwait(false);
            if (comment == null) throw ApiErrorException.NotFound("Comment");
            if (comment.TargetType == TargetTypeEnum.FeedPost)
            {
                FeedPost? post = await _Database.FeedPosts.ReadAsync(comment.TargetId, token).ConfigureAwait(false);
                if (post != null && !CanSeePost(post, actor, DateTime.UtcNow)) ThrowIfLocked(post, actor, DateTime.UtcNow);
                if (post == null || !CanSeePost(post, actor, DateTime.UtcNow)) throw ApiErrorException.NotFound("Comment");
            }
            else if (comment.TargetType == TargetTypeEnum.Thread)
            {
                if (ThreadVisible == null || !await ThreadVisible(actor, comment.TargetId, token).ConfigureAwait(false)) throw ApiErrorException.NotFound("Comment");
            }
            else if (comment.TargetType == TargetTypeEnum.Idea)
            {
                if (IdeaVisible == null || !await IdeaVisible(actor, comment.TargetId, token).ConfigureAwait(false)) throw ApiErrorException.NotFound("Comment");
            }
            else if (!await IsTargetVisibleAsync(actor, comment.TargetType, comment.TargetId, token).ConfigureAwait(false))
            {
                throw ApiErrorException.NotFound("Comment");
            }
            return comment;
        }

        /// <summary>
        /// The media visibility rule for attached media (MediaService.AttachedVisibility): media of a post follows the
        /// post; media of a comment follows the comment's content visibility and its post. Other targets are visible to
        /// every member.
        /// </summary>
        /// <param name="media">Media.</param>
        /// <param name="actor">Caller.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the caller may see it.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<bool> CanSeeAttachedMediaAsync(MediaAttachment media, ContentActor actor, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(media);
            ArgumentNullException.ThrowIfNull(actor);
            if (String.IsNullOrEmpty(media.AttachedTargetId)) return true;
            DateTime now = DateTime.UtcNow;
            if (media.AttachedTargetType == TargetTypeEnum.FeedPost)
            {
                FeedPost? post = await _Database.FeedPosts.ReadAsync(media.AttachedTargetId, token).ConfigureAwait(false);
                return post != null && CanSeePost(post, actor, now);
            }
            if (media.AttachedTargetType == TargetTypeEnum.Comment)
            {
                Comment? comment = await _Database.Comments.ReadAsync(media.AttachedTargetId, token).ConfigureAwait(false);
                if (comment == null || !CanSeeCommentContent(comment, actor)) return false;
                if (comment.TargetType == TargetTypeEnum.Thread) return ThreadVisible != null && await ThreadVisible(actor, comment.TargetId, token).ConfigureAwait(false);
                if (comment.TargetType == TargetTypeEnum.Idea) return IdeaVisible != null && await IdeaVisible(actor, comment.TargetId, token).ConfigureAwait(false);
                if (comment.TargetType != TargetTypeEnum.FeedPost) return await IsTargetVisibleAsync(actor, comment.TargetType, comment.TargetId, token).ConfigureAwait(false);
                FeedPost? post = await _Database.FeedPosts.ReadAsync(comment.TargetId, token).ConfigureAwait(false);
                return post != null && CanSeePost(post, actor, now);
            }
            if (media.AttachedTargetType == TargetTypeEnum.Thread) return ThreadVisible != null && await ThreadVisible(actor, media.AttachedTargetId, token).ConfigureAwait(false);
            if (media.AttachedTargetType != null && Targets?.Find(media.AttachedTargetType.Value) != null)
                return await IsTargetVisibleAsync(actor, media.AttachedTargetType.Value, media.AttachedTargetId, token).ConfigureAwait(false);
            return true;
        }

        /// <summary>
        /// Whether a caller may read an object of a content target provider type (true for types without a provider,
        /// which other rules cover). Moderators of the object's scope see hidden objects when the provider says so.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="targetType">Target type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when readable or not a provider type.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        public async Task<bool> IsTargetVisibleAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ContentTargetProvider? provider = Targets?.Find(targetType);
            if (provider == null) return true;
            if (String.IsNullOrEmpty(targetId)) return false;
            return await provider.IsVisibleAsync(actor, targetType, targetId, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static ApiErrorException Denied(int status, string error, string message, string required, string postId)
        {
            AccessDeniedResponse body = new AccessDeniedResponse
            {
                Error = error,
                Message = message,
                Required = required,
                Feature = AccessPolicy.FeatureKey(FeatureKeyEnum.Feed),
                TargetType = TargetTypeEnum.FeedPost.ToString(),
                TargetId = postId
            };
            return new ApiErrorException(status, error, message, null, body);
        }

        #endregion
    }
}
