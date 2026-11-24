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
    /// Builds FeedPost, PublicFeedPost, and Comment payloads for a page in a fixed number of queries (authors, media,
    /// link previews, mentions, reactions, labels and tags, block relations, SystemAdmin authors), never one query per
    /// item. Viewer capability objects come from the caller's <see cref="ContentActor"/>. Comment placeholders follow
    /// the state table of contract section 7.2. Thread safety: safe to call concurrently.
    /// </summary>
    public class ContentViewBuilder
    {
        #region Public-Members

        /// <summary>
        /// Called after feed post views are built, to decorate promoted cards and discussion links (set by the forum
        /// module, docs/PHASE3_CONTRACT.md section 8). Null leaves the views unchanged.
        /// </summary>
        public Func<IReadOnlyList<FeedPostView>, IReadOnlyList<FeedPost>, ContentActor, CancellationToken, Task>? PostDecorator { get; set; } = null;

        /// <summary>
        /// Called after comment views are built, to apply thread rules (locked threads, accepted answers) (set by the
        /// forum module). Null leaves the views unchanged.
        /// </summary>
        public Func<Dictionary<string, CommentView>, IReadOnlyList<Comment>, ContentActor, CancellationToken, Task>? CommentDecorator { get; set; } = null;

        /// <summary>
        /// Further feed post decorators, applied after <see cref="PostDecorator"/> (the Phase 4 modules decorate
        /// PromotedLesson and PromotedEvent cards). Register during module construction or Attach.
        /// </summary>
        public List<Func<IReadOnlyList<FeedPostView>, IReadOnlyList<FeedPost>, ContentActor, CancellationToken, Task>> PostDecorators { get; } =
            new List<Func<IReadOnlyList<FeedPostView>, IReadOnlyList<FeedPost>, ContentActor, CancellationToken, Task>>();

        /// <summary>
        /// Further comment decorators, applied after <see cref="CommentDecorator"/> (the Phase 4 modules apply event and
        /// lesson rules: canceled events, comments turned off). Register during module construction or Attach.
        /// </summary>
        public List<Func<Dictionary<string, CommentView>, IReadOnlyList<Comment>, ContentActor, CancellationToken, Task>> CommentDecorators { get; } =
            new List<Func<Dictionary<string, CommentView>, IReadOnlyList<Comment>, ContentActor, CancellationToken, Task>>();

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the builder.
        /// </summary>
        /// <param name="services">Application services (read on each call).</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public ContentViewBuilder(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// FeedPost payloads for a page, in the given order.
        /// </summary>
        /// <param name="posts">Posts.</param>
        /// <param name="actor">Caller.</param>
        /// <param name="moderatorView">Moderator view: the moderation object is always present.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Views.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public Task<List<FeedPostView>> FeedPostsAsync(IReadOnlyList<FeedPost> posts, ContentActor actor, bool moderatorView, CancellationToken token = default)
        {
            return BuildPostsAsync<FeedPostView>(posts, actor, moderatorView, token);
        }

        /// <summary>
        /// FeedPost payloads of a payload type derived from FeedPostView (for example the admin detail).
        /// </summary>
        /// <typeparam name="T">Payload type.</typeparam>
        /// <param name="posts">Posts.</param>
        /// <param name="actor">Caller.</param>
        /// <param name="moderatorView">Moderator view.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Views.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<List<T>> BuildPostsAsync<T>(IReadOnlyList<FeedPost> posts, ContentActor actor, bool moderatorView, CancellationToken token = default) where T : FeedPostView, new()
        {
            ArgumentNullException.ThrowIfNull(posts);
            ArgumentNullException.ThrowIfNull(actor);
            List<T> views = new List<T>();
            if (posts.Count == 0) return views;
            List<string> ids = posts.Select(p => p.Id).ToList();
            List<string> authors = posts.Select(p => p.AuthorUserId).Distinct(StringComparer.Ordinal).ToList();
            bool staff = actor.IsStaffFor(TargetTypeEnum.FeedPost);
            // Signed-out callers (anonymous browsing) see the anonymous profile cards and the public media copies.
            Dictionary<string, UserSummary> users = actor.IsAnonymous
                ? await _Services.Summaries.BuildAsync(authors.Select(a => (string?)a), ProfileViewerEnum.Anonymous, token).ConfigureAwait(false)
                : await _Services.Summaries.BuildAsync(authors.Select(a => (string?)a), token).ConfigureAwait(false);
            Dictionary<string, List<MediaView>> media = await _Services.Media.ListViewsForTargetsAsync(TargetTypeEnum.FeedPost, ids, actor.IsAnonymous, actor.IsStaffFor(TargetTypeEnum.Media), token).ConfigureAwait(false);
            Dictionary<string, LinkPreviewView> previews = await _Services.LinkPreviews.LoadViewsAsync(posts.Where(p => p.ShowLinkPreview).Select(p => p.LinkPreviewId), token).ConfigureAwait(false);
            Dictionary<string, List<MentionView>> mentions = await _Services.Mentions.LoadViewsAsync(TargetTypeEnum.FeedPost, ids, token).ConfigureAwait(false);
            Dictionary<string, ReactionState> reactions = await _Services.Reactions.LoadAsync(TargetTypeEnum.FeedPost, ids, actor.UserId, token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.FeedPost, ids, staff, token).ConfigureAwait(false);
            HashSet<string> admins = await _Services.Summaries.SystemAdminsAsync(authors, token).ConfigureAwait(false);
            HashSet<string> blockers = String.IsNullOrEmpty(actor.UserId) ? new HashSet<string>(StringComparer.Ordinal) : await _Services.Blocks.BlockersOfAsync(actor.UserId, token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            foreach (FeedPost post in posts)
            {
                T view = new T();
                Fill(view, post, actor, moderatorView, now, users, media, previews, mentions, reactions, metadata, admins, blockers);
                views.Add(view);
            }
            if (PostDecorator != null) await PostDecorator(views, posts, actor, token).ConfigureAwait(false);
            foreach (Func<IReadOnlyList<FeedPostView>, IReadOnlyList<FeedPost>, ContentActor, CancellationToken, Task> decorator in PostDecorators) await decorator(views, posts, actor, token).ConfigureAwait(false);
            return views;
        }

        /// <summary>
        /// PublicFeedPost payloads (anonymous public feed): media from the public paths, public labels and tags, and
        /// reaction counts without the caller's keys.
        /// </summary>
        /// <param name="posts">Public, Published posts.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Views.</returns>
        /// <exception cref="ArgumentNullException">The posts are null.</exception>
        public async Task<List<PublicFeedPost>> PublicPostsAsync(IReadOnlyList<FeedPost> posts, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(posts);
            List<PublicFeedPost> views = new List<PublicFeedPost>();
            if (posts.Count == 0) return views;
            List<string> ids = posts.Select(p => p.Id).ToList();
            Dictionary<string, UserSummary> users = await _Services.Summaries.BuildAsync(posts.Select(p => (string?)p.AuthorUserId), ProfileViewerEnum.Anonymous, token).ConfigureAwait(false);
            Dictionary<string, List<MediaView>> media = await _Services.Media.ListViewsForTargetsAsync(TargetTypeEnum.FeedPost, ids, true, false, token).ConfigureAwait(false);
            Dictionary<string, LinkPreviewView> previews = await _Services.LinkPreviews.LoadViewsAsync(posts.Where(p => p.ShowLinkPreview).Select(p => p.LinkPreviewId), token).ConfigureAwait(false);
            Dictionary<string, ReactionState> reactions = await _Services.Reactions.LoadAsync(TargetTypeEnum.FeedPost, ids, null, token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.FeedPost, ids, false, token).ConfigureAwait(false);
            foreach (FeedPost post in posts)
            {
                ReactionState state = reactions.TryGetValue(post.Id, out ReactionState? r) ? r : new ReactionState();
                ObjectMetadata meta = metadata.TryGetValue(post.Id, out ObjectMetadata? m) ? m : new ObjectMetadata();
                views.Add(new PublicFeedPost
                {
                    Id = post.Id,
                    Kind = post.Kind,
                    Author = users.TryGetValue(post.AuthorUserId, out UserSummary? u) ? u : UserSummary.Deleted(),
                    Title = post.Title,
                    Body = post.Body,
                    Excerpt = post.Excerpt,
                    Attachments = media.TryGetValue(post.Id, out List<MediaView>? list) ? list : new List<MediaView>(),
                    LinkPreview = post.ShowLinkPreview && post.LinkPreviewId != null && previews.TryGetValue(post.LinkPreviewId, out LinkPreviewView? p) ? p : null,
                    CommentsEnabled = post.CommentsEnabled,
                    IsPinned = post.IsPinned,
                    PublishedUtc = post.PublishedUtc,
                    CreatedUtc = post.CreatedUtc,
                    EditedUtc = post.EditedUtc,
                    RevisionCount = post.RevisionCount,
                    CommentCount = post.CommentCount,
                    ReactionCounts = state.ReactionCounts,
                    ReactionTotal = state.ReactionTotal,
                    CommentTarget = new ContentTarget { Type = TargetTypeEnum.FeedPost.ToString(), Id = post.Id },
                    Labels = meta.Labels,
                    Tags = meta.Tags
                });
            }
            return views;
        }

        /// <summary>
        /// Comment payloads (without children) keyed by comment id, with placeholders per contract section 7.2:
        /// Deleted for deleted comments; Removed and Hidden for moderated comments unless the caller is their author or a
        /// feed moderator (who see the content and the moderation object); BlockedAuthor when the caller blocked the
        /// author (and the author is not staff).
        /// </summary>
        /// <param name="comments">Comments.</param>
        /// <param name="actor">Caller.</param>
        /// <param name="moderatorView">Moderator view: full content in every state and the moderation object.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Views by comment id.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<Dictionary<string, CommentView>> CommentsAsync(IReadOnlyList<Comment> comments, ContentActor actor, bool moderatorView, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(comments);
            ArgumentNullException.ThrowIfNull(actor);
            Dictionary<string, CommentView> result = new Dictionary<string, CommentView>(StringComparer.Ordinal);
            if (comments.Count == 0) return result;
            List<string> ids = comments.Select(c => c.Id).ToList();
            List<string> replyTo = comments.Where(c => c.ReplyToCommentId != null).Select(c => c.ReplyToCommentId!).Distinct(StringComparer.Ordinal).ToList();
            Dictionary<string, string> replyAuthors = (await _Services.Database.Comments.ReadManyAsync(replyTo, token).ConfigureAwait(false)).ToDictionary(c => c.Id, c => c.AuthorUserId, StringComparer.Ordinal);
            List<string> authors = comments.Select(c => c.AuthorUserId).Concat(replyAuthors.Values).Distinct(StringComparer.Ordinal).ToList();
            bool staff = actor.IsStaffFor(TargetTypeEnum.Comment);
            Dictionary<string, UserSummary> users = actor.IsAnonymous
                ? await _Services.Summaries.BuildAsync(authors.Select(a => (string?)a), ProfileViewerEnum.Anonymous, token).ConfigureAwait(false)
                : await _Services.Summaries.BuildAsync(authors.Select(a => (string?)a), token).ConfigureAwait(false);
            // Comment media has no public copy, so signed-out callers (anonymous browsing) get comments without attachments.
            Dictionary<string, List<MediaView>> media = actor.IsAnonymous
                ? new Dictionary<string, List<MediaView>>(StringComparer.Ordinal)
                : await _Services.Media.ListViewsForTargetsAsync(TargetTypeEnum.Comment, ids, false, actor.IsStaffFor(TargetTypeEnum.Media), token).ConfigureAwait(false);
            Dictionary<string, LinkPreviewView> previews = await _Services.LinkPreviews.LoadViewsAsync(comments.Where(c => c.ShowLinkPreview).Select(c => c.LinkPreviewId), token).ConfigureAwait(false);
            Dictionary<string, List<MentionView>> mentions = await _Services.Mentions.LoadViewsAsync(TargetTypeEnum.Comment, ids, token).ConfigureAwait(false);
            Dictionary<string, ReactionState> reactions = await _Services.Reactions.LoadAsync(TargetTypeEnum.Comment, ids, actor.UserId, token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.Comment, ids, staff, token).ConfigureAwait(false);
            HashSet<string> admins = await _Services.Summaries.SystemAdminsAsync(comments.Select(c => c.AuthorUserId).Distinct(StringComparer.Ordinal), token).ConfigureAwait(false);
            HashSet<string> blockers = String.IsNullOrEmpty(actor.UserId) ? new HashSet<string>(StringComparer.Ordinal) : await _Services.Blocks.BlockersOfAsync(actor.UserId, token).ConfigureAwait(false);
            HashSet<string> hidden = String.IsNullOrEmpty(actor.UserId) ? new HashSet<string>(StringComparer.Ordinal) : await _Services.Blocks.HiddenAuthorsForAsync(actor.UserId, token).ConfigureAwait(false);
            List<string> postIds = comments.Where(c => c.TargetType == TargetTypeEnum.FeedPost).Select(c => c.TargetId).Distinct(StringComparer.Ordinal).ToList();
            Dictionary<string, FeedPost> posts = (await _Services.Database.FeedPosts.ReadManyAsync(postIds, token).ConfigureAwait(false)).ToDictionary(p => p.Id, StringComparer.Ordinal);
            DateTime now = DateTime.UtcNow;
            foreach (Comment comment in comments)
            {
                CommentView view = new CommentView
                {
                    Id = comment.Id,
                    TargetType = comment.TargetType.ToString(),
                    TargetId = comment.TargetId,
                    ParentCommentId = comment.ParentCommentId,
                    ReplyToCommentId = comment.ReplyToCommentId,
                    ReplyToUser = comment.ReplyToCommentId != null && replyAuthors.TryGetValue(comment.ReplyToCommentId, out string? ra) && users.TryGetValue(ra, out UserSummary? ru) ? ru : null,
                    Depth = comment.Depth,
                    CreatedUtc = comment.CreatedUtc,
                    ChildCount = comment.ChildCount,
                    DescendantCount = comment.DescendantCount,
                    IsAcceptedAnswer = comment.IsAcceptedAnswer,
                    IsFeedbackReview = comment.IsFeedbackReview
                };
                bool isAuthor = actor.UserId != null && String.Equals(comment.AuthorUserId, actor.UserId, StringComparison.Ordinal);
                bool full;
                if (comment.DeletedUtc != null)
                {
                    view.State = CommentStateEnum.Deleted;
                    full = false;
                }
                else if (comment.ModerationState != ModerationStateEnum.None)
                {
                    view.State = comment.ModerationState == ModerationStateEnum.Removed ? CommentStateEnum.Removed : CommentStateEnum.Hidden;
                    full = moderatorView || ContentAccessService.CanSeeCommentContent(comment, actor);
                }
                else if (!moderatorView && hidden.Contains(comment.AuthorUserId))
                {
                    view.State = CommentStateEnum.BlockedAuthor;
                    full = false;
                }
                else
                {
                    view.State = CommentStateEnum.Visible;
                    full = true;
                }
                if (moderatorView || (full && comment.ModerationState != ModerationStateEnum.None))
                    view.Moderation = new ModerationInfo { State = comment.ModerationState, Reason = comment.ModerationReason, ActedUtc = comment.ModeratedUtc };
                if (full || (moderatorView && comment.Body != null))
                {
                    ReactionState state = reactions.TryGetValue(comment.Id, out ReactionState? r) ? r : new ReactionState();
                    ObjectMetadata meta = metadata.TryGetValue(comment.Id, out ObjectMetadata? m) ? m : new ObjectMetadata();
                    view.Author = users.TryGetValue(comment.AuthorUserId, out UserSummary? u) ? u : UserSummary.Deleted();
                    view.Body = comment.Body;
                    view.Mentions = mentions.TryGetValue(comment.Id, out List<MentionView>? mv) ? mv : new List<MentionView>();
                    view.Attachments = media.TryGetValue(comment.Id, out List<MediaView>? list) ? list : new List<MediaView>();
                    view.LinkPreview = comment.ShowLinkPreview && comment.LinkPreviewId != null && previews.TryGetValue(comment.LinkPreviewId, out LinkPreviewView? p) ? p : null;
                    view.EditedUtc = comment.EditedUtc;
                    view.RevisionCount = comment.RevisionCount;
                    view.ReactionCounts = state.ReactionCounts;
                    view.ReactionTotal = state.ReactionTotal;
                    view.MyReactions = state.MyReactions;
                    view.Labels = meta.Labels;
                    view.Tags = meta.Tags;
                    if (full) view.Viewer = CommentViewer(comment, actor, isAuthor, posts, admins, blockers, now);
                }
                result[comment.Id] = view;
            }
            if (CommentDecorator != null) await CommentDecorator(result, comments, actor, token).ConfigureAwait(false);
            foreach (Func<Dictionary<string, CommentView>, IReadOnlyList<Comment>, ContentActor, CancellationToken, Task> decorator in CommentDecorators) await decorator(result, comments, actor, token).ConfigureAwait(false);
            return result;
        }

        #endregion

        #region Private-Methods

        private void Fill(FeedPostView view, FeedPost post, ContentActor actor, bool moderatorView, DateTime now, Dictionary<string, UserSummary> users, Dictionary<string, List<MediaView>> media,
            Dictionary<string, LinkPreviewView> previews, Dictionary<string, List<MentionView>> mentions, Dictionary<string, ReactionState> reactions, Dictionary<string, ObjectMetadata> metadata,
            HashSet<string> admins, HashSet<string> blockers)
        {
            FeedPostStateEnum state = post.StateAt(now);
            bool isAuthor = actor.UserId != null && String.Equals(post.AuthorUserId, actor.UserId, StringComparison.Ordinal);
            bool published = state == FeedPostStateEnum.Published;
            bool blockedByAuthor = !isAuthor && blockers.Contains(post.AuthorUserId);
            ReactionState reaction = reactions.TryGetValue(post.Id, out ReactionState? r) ? r : new ReactionState();
            ObjectMetadata meta = metadata.TryGetValue(post.Id, out ObjectMetadata? m) ? m : new ObjectMetadata();
            view.Id = post.Id;
            view.Kind = post.Kind;
            view.Author = users.TryGetValue(post.AuthorUserId, out UserSummary? u) ? u : UserSummary.Deleted();
            view.Title = post.Title;
            view.Body = post.Body;
            view.Excerpt = post.Excerpt;
            view.Mentions = mentions.TryGetValue(post.Id, out List<MentionView>? mv) ? mv : new List<MentionView>();
            view.Attachments = media.TryGetValue(post.Id, out List<MediaView>? list) ? list : new List<MediaView>();
            view.LinkPreview = post.ShowLinkPreview && post.LinkPreviewId != null && previews.TryGetValue(post.LinkPreviewId, out LinkPreviewView? p) ? p : null;
            view.Visibility = post.Visibility;
            view.Audience = post.Audience;
            view.State = state;
            view.CommentsEnabled = post.CommentsEnabled;
            view.ShowLinkPreview = post.ShowLinkPreview;
            view.IsPinned = post.IsPinned;
            view.PinnedUtc = post.PinnedUtc;
            view.PublishAtUtc = post.PublishAtUtc;
            view.PublishedUtc = post.PublishedUtc;
            view.CreatedUtc = post.CreatedUtc;
            view.UpdatedUtc = post.UpdatedUtc;
            view.EditedUtc = post.EditedUtc;
            view.RevisionCount = post.RevisionCount;
            view.CommentCount = post.CommentCount;
            view.ReactionCounts = reaction.ReactionCounts;
            view.ReactionTotal = reaction.ReactionTotal;
            view.MyReactions = reaction.MyReactions;
            view.CommentTarget = new ContentTarget { Type = TargetTypeEnum.FeedPost.ToString(), Id = post.Id };
            view.Source = post.SourceType == null || post.SourceId == null ? null : new FeedPostSourceView { Type = post.SourceType, Id = post.SourceId };
            bool showModeration = moderatorView || (post.ModerationState != ModerationStateEnum.None && (isAuthor || actor.CanModerateFeed));
            view.Moderation = showModeration ? new ModerationInfo { State = post.ModerationState, Reason = post.ModerationReason, ActedUtc = post.ModeratedUtc } : null;
            bool canAct = actor.UserId != null;
            view.Viewer = new FeedPostViewer
            {
                CanEdit = actor.IsSystemAdmin || (isAuthor && actor.CanWrite),
                CanDelete = actor.IsSystemAdmin || isAuthor,
                CanPin = actor.IsSystemAdmin && published,
                CanComment = published && post.CommentsEnabled && canAct && actor.CanWrite && !blockedByAuthor,
                CanReact = published && canAct && (actor.IsSystemAdmin || !actor.IsMuted) && !blockedByAuthor,
                CanReport = canAct && !isAuthor,
                CanModerate = ContentAccessService.CanModerate(actor, admins.Contains(post.AuthorUserId)),
                CanAttachMedia = actor.CanAttachMedia,
                CanAttachVideo = actor.CanAttachVideo
            };
            view.Labels = meta.Labels;
            view.Tags = meta.Tags;
            AccessTierEnum tier = AccessDecider.TierOf(actor);
            if (!AccessDecider.Meets(tier, AccessDecider.ParticipateTier(actor.Access, FeatureKeyEnum.Feed, post.Audience)) || actor.IsAnonymous)
            {
                view.Viewer.CanComment = false;
                view.Viewer.CanReact = false;
            }
            if (!ContentAccessService.CanSeePost(post, actor, now) && AccessDecider.ShowsLocked(actor.Access, tier, post.Audience)) Lock(view);
        }

        private static void Lock(FeedPostView view)
        {
            view.Locked = true;
            view.Body = String.Empty;
            view.Excerpt = String.Empty;
            view.Mentions = new List<MentionView>();
            view.Attachments = new List<MediaView>();
            view.LinkPreview = null;
            view.ReactionCounts = new Dictionary<string, long>();
            view.ReactionTotal = 0;
            view.MyReactions = new List<string>();
            view.Moderation = null;
            view.Viewer = new FeedPostViewer { CanOpen = false, Requires = "Membership" };
        }

        private static CommentViewer CommentViewer(Comment comment, ContentActor actor, bool isAuthor, Dictionary<string, FeedPost> posts, HashSet<string> admins, HashSet<string> blockers, DateTime now)
        {
            bool canAct = actor.UserId != null;
            bool open = comment.TargetType != TargetTypeEnum.FeedPost;
            bool commentsEnabled = true;
            if (comment.TargetType == TargetTypeEnum.FeedPost && posts.TryGetValue(comment.TargetId, out FeedPost? post))
            {
                open = post.StateAt(now) == FeedPostStateEnum.Published;
                commentsEnabled = post.CommentsEnabled;
            }
            bool live = comment.ModerationState == ModerationStateEnum.None;
            bool blockedByAuthor = !isAuthor && blockers.Contains(comment.AuthorUserId);
            // The access model: taking part needs the higher of the post's read tier and the feed's write tier.
            ContentAudienceEnum audience = comment.TargetType == TargetTypeEnum.FeedPost && posts.TryGetValue(comment.TargetId, out FeedPost? target) ? target.Audience : ContentAudienceEnum.SignedIn;
            bool participate = !actor.IsAnonymous && AccessDecider.Meets(AccessDecider.TierOf(actor), AccessDecider.ParticipateTier(actor.Access, FeatureKeyEnum.Feed, audience));
            return new CommentViewer
            {
                CanEdit = isAuthor && actor.CanWrite,
                CanDelete = isAuthor || actor.IsSystemAdmin,
                CanReply = open && live && commentsEnabled && canAct && participate && actor.CanWrite && !blockedByAuthor,
                CanReact = open && live && canAct && participate && (actor.IsSystemAdmin || !actor.IsMuted) && !blockedByAuthor,
                CanReport = canAct && !isAuthor,
                CanModerate = ContentAccessService.CanModerate(actor, admins.Contains(comment.AuthorUserId)),
                CanAttachMedia = actor.CanAttachMedia,
                CanAttachVideo = actor.CanAttachVideo
            };
        }

        #endregion
    }
}
