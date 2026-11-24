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
    /// Builds Forum, Thread, ThreadSummary, PublicThread, and Poll payloads for a caller in batches
    /// (docs/PHASE3_CONTRACT.md sections 1.4, 2, and 6.2). Counts, reactions, and comments come from each thread's
    /// conversation target.
    /// </summary>
    public class ForumViewBuilder
    {
        #region Public-Members

        /// <summary>
        /// Most attachments in a thread list item.
        /// </summary>
        public const int AttachmentPreviewMax = 4;

        /// <summary>
        /// Cap of a forum's unread thread count.
        /// </summary>
        public const int UnreadCap = 99;

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public ForumViewBuilder(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build forum payloads.
        /// </summary>
        /// <typeparam name="T">ForumView or a subclass.</typeparam>
        /// <param name="forums">Forums.</param>
        /// <param name="actor">Caller.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Views in order.</returns>
        public async Task<List<T>> ForumsAsync<T>(IReadOnlyList<Forum> forums, ContentActor actor, CancellationToken token = default) where T : ForumView, new()
        {
            ArgumentNullException.ThrowIfNull(forums);
            ArgumentNullException.ThrowIfNull(actor);
            List<T> views = new List<T>();
            if (forums.Count == 0) return views;
            List<string> ids = forums.Select(f => f.Id).ToList();
            bool staff = actor.IsStaffFor(TargetTypeEnum.Forum);
            Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.Forum, ids, staff, token).ConfigureAwait(false);
            List<string> lastIds = forums.Where(f => f.LastThreadId != null).Select(f => f.LastThreadId!).Distinct(StringComparer.Ordinal).ToList();
            Dictionary<string, ForumThread> lastThreads = (await _Services.Database.Threads.ReadManyAsync(lastIds, token).ConfigureAwait(false)).ToDictionary(t => t.Id, StringComparer.Ordinal);
            Dictionary<string, UserSummary> authors = await _Services.Summaries.BuildAsync(lastThreads.Values.Select(t => (string?)t.AuthorUserId), token).ConfigureAwait(false);
            HashSet<string> followed = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, int> unread = new Dictionary<string, int>(StringComparer.Ordinal);
            HashSet<string> muted = new HashSet<string>(StringComparer.Ordinal);
            if (!String.IsNullOrEmpty(actor.UserId))
            {
                followed = await _Services.Database.Forums.FollowedAmongAsync(actor.UserId, ids, token).ConfigureAwait(false);
                List<string> hidden = (await _Services.Blocks.HiddenAuthorsForAsync(actor.UserId, token).ConfigureAwait(false)).ToList();
                DateTime window = DateTime.UtcNow.AddDays(-_Services.Settings.Forums.UnreadWindowDays);
                unread = await _Services.Database.Forums.UnreadCountsAsync(actor.UserId, ids, ContentAudienceRule.VisibleAudiences(actor), hidden, window, UnreadCap, token).ConfigureAwait(false);
                muted = await _Services.Forums.Access.MutedForumsAsync(actor, ids, token).ConfigureAwait(false);
            }
            Dictionary<string, string?> icons = await IconUrlsAsync(forums, actor, token).ConfigureAwait(false);
            foreach (Forum forum in forums)
            {
                ObjectMetadata meta = metadata.TryGetValue(forum.Id, out ObjectMetadata? m) ? m : new ObjectMetadata();
                bool canAct = !String.IsNullOrEmpty(actor.UserId);
                bool canWrite = canAct && ForumAccessService.ModelAllowsWrite(actor);
                T view = new T
                {
                    Id = forum.Id,
                    Slug = forum.Slug,
                    Name = forum.Name,
                    Description = forum.Description,
                    Guidelines = forum.Guidelines,
                    Icon = new ForumIconView { Emoji = forum.IconEmoji, MediaId = forum.IconMediaId, ImageUrl = forum.IconMediaId != null && icons.TryGetValue(forum.Id, out string? url) ? url : null },
                    Color = forum.Color,
                    Section = forum.Section,
                    SortOrder = forum.SortOrder,
                    Audience = forum.Audience,
                    ThreadCreation = forum.ThreadCreation,
                    AllowedThreadTypes = forum.AllowedThreadTypes.ToList(),
                    DefaultOrdering = forum.DefaultOrdering,
                    AllowMedia = forum.AllowMedia,
                    AllowVideo = forum.AllowVideo,
                    MaxOpenFeedbackPerMember = forum.MaxOpenFeedbackPerMember,
                    IsArchived = forum.IsArchived,
                    ThreadCount = forum.ThreadCount,
                    CommentCount = forum.CommentCount,
                    LastActivityUtc = forum.LastActivityUtc,
                    IsFollowed = followed.Contains(forum.Id),
                    UnreadThreadCount = canAct ? (unread.TryGetValue(forum.Id, out int u) ? u : 0) : null,
                    Labels = meta.Labels,
                    Tags = meta.Tags,
                    Viewer = new ForumViewer
                    {
                        CanCreateThread = canWrite && actor.CanWrite && !forum.IsArchived && !muted.Contains(forum.Id) && ForumAccessService.CreationAllows(forum, actor),
                        CanFollow = canAct,
                        CanModerate = actor.CanModerateForum(forum.Id),
                        AllowedThreadTypes = ForumAccessService.AllowedTypesFor(forum, actor)
                    },
                    CreatedUtc = forum.CreatedUtc,
                    UpdatedUtc = forum.UpdatedUtc
                };
                if (forum.LastThreadId != null && lastThreads.TryGetValue(forum.LastThreadId, out ForumThread? last))
                {
                    view.LastThread = new ForumLastThreadView
                    {
                        Id = last.Id,
                        Title = last.Title,
                        Author = authors.TryGetValue(last.AuthorUserId, out UserSummary? a) ? a : UserSummary.Deleted(),
                        LastActivityUtc = last.LastActivityUtc
                    };
                }
                views.Add(view);
            }
            return views;
        }

        /// <summary>
        /// Build full thread payloads.
        /// </summary>
        /// <param name="contexts">Threads with their forums.</param>
        /// <param name="actor">Caller.</param>
        /// <param name="moderatorView">Whether to include moderation details for staff tools.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Views in order.</returns>
        public async Task<List<ThreadView>> ThreadsAsync(IReadOnlyList<ThreadContext> contexts, ContentActor actor, bool moderatorView, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(contexts);
            ArgumentNullException.ThrowIfNull(actor);
            List<ThreadView> views = new List<ThreadView>();
            if (contexts.Count == 0) return views;
            ThreadBundle bundle = await LoadAsync(contexts, actor, true, token).ConfigureAwait(false);
            Dictionary<string, CommentView> accepted = await AcceptedAnswersAsync(contexts, actor, token).ConfigureAwait(false);
            foreach (ThreadContext c in contexts)
            {
                ThreadView view = new ThreadView();
                FillCore(view, c, actor, moderatorView, bundle);
                ForumThread t = c.Thread;
                bool linked = t.Kind == ThreadKindEnum.LinkedFeedPost;
                string mediaKey = linked && c.Source != null ? c.Source.Id : t.Id;
                view.Body = linked ? c.Source?.Body : t.Body;
                view.Mentions = (linked ? bundle.PostMentions : bundle.Mentions).TryGetValue(mediaKey, out List<MentionView>? mv) ? mv : new List<MentionView>();
                view.Attachments = (linked ? bundle.PostMedia : bundle.Media).TryGetValue(mediaKey, out List<MediaView>? media) ? media : new List<MediaView>();
                string? previewId = linked ? (c.Source != null && c.Source.ShowLinkPreview ? c.Source.LinkPreviewId : null) : (t.ShowLinkPreview ? t.LinkPreviewId : null);
                view.LinkPreview = previewId != null && bundle.Previews.TryGetValue(previewId, out LinkPreviewView? p) ? p : null;
                view.AcceptedAnswer = t.HasAcceptedAnswer && t.AcceptedCommentId != null && accepted.TryGetValue(t.AcceptedCommentId, out CommentView? answer) ? answer : null;
                view.Poll = bundle.Polls.TryGetValue(t.Id, out Poll? poll) ? PollView(poll, t, actor, bundle.MyVotes.TryGetValue(poll.Id, out List<string>? mine) ? mine : new List<string>(), bundle.PollMeta) : null;
                if (t.Type == ThreadTypeEnum.Feedback)
                {
                    view.Feedback = new FeedbackView
                    {
                        Status = t.FeedbackStatus ?? FeedbackStatusEnum.Open,
                        Focus = t.FeedbackFocus,
                        Reviewer = t.FeedbackReviewerId != null && bundle.Users.TryGetValue(t.FeedbackReviewerId, out UserSummary? r) ? r : null,
                        ReviewCommentId = t.FeedbackReviewCommentId,
                        StatusUtc = t.FeedbackStatusUtc
                    };
                }
                views.Add(view);
            }
            return views;
        }

        /// <summary>
        /// Build thread list items.
        /// </summary>
        /// <param name="contexts">Threads with their forums.</param>
        /// <param name="actor">Caller.</param>
        /// <param name="moderatorView">Whether to include moderation details for staff tools.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Views in order.</returns>
        public async Task<List<ThreadSummaryView>> SummariesAsync(IReadOnlyList<ThreadContext> contexts, ContentActor actor, bool moderatorView, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(contexts);
            ArgumentNullException.ThrowIfNull(actor);
            List<ThreadSummaryView> views = new List<ThreadSummaryView>();
            if (contexts.Count == 0) return views;
            ThreadBundle bundle = await LoadAsync(contexts, actor, false, token).ConfigureAwait(false);
            foreach (ThreadContext c in contexts)
            {
                ThreadSummaryView view = new ThreadSummaryView();
                FillCore(view, c, actor, moderatorView, bundle);
                ForumThread t = c.Thread;
                bool linked = t.Kind == ThreadKindEnum.LinkedFeedPost;
                string mediaKey = linked && c.Source != null ? c.Source.Id : t.Id;
                List<MediaView> media = (linked ? bundle.PostMedia : bundle.Media).TryGetValue(mediaKey, out List<MediaView>? list) ? list : new List<MediaView>();
                view.HasAcceptedAnswer = t.HasAcceptedAnswer;
                view.AttachmentPreview = media.Take(AttachmentPreviewMax).ToList();
                view.AttachmentCount = media.Count;
                if (bundle.Polls.TryGetValue(t.Id, out Poll? poll))
                {
                    view.Poll = new PollSummaryView
                    {
                        Id = poll.Id,
                        IsClosed = IsClosed(poll, DateTime.UtcNow),
                        ClosesUtc = poll.ClosesUtc,
                        TotalVoters = poll.TotalVoters,
                        HasVoted = bundle.MyVotes.TryGetValue(poll.Id, out List<string>? mine) && mine.Count > 0
                    };
                }
                if (t.Type == ThreadTypeEnum.Feedback)
                {
                    view.Feedback = new FeedbackSummaryView
                    {
                        Status = t.FeedbackStatus ?? FeedbackStatusEnum.Open,
                        Reviewer = t.FeedbackReviewerId != null && bundle.Users.TryGetValue(t.FeedbackReviewerId, out UserSummary? reviewer) ? reviewer : null
                    };
                }
                views.Add(view);
            }
            return views;
        }

        /// <summary>
        /// Build public sample thread payloads (no viewer state; mentions as plain text in the body).
        /// </summary>
        /// <param name="contexts">Threads.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Views in order.</returns>
        public async Task<List<PublicThreadView>> PublicAsync(IReadOnlyList<ThreadContext> contexts, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(contexts);
            List<PublicThreadView> views = new List<PublicThreadView>();
            if (contexts.Count == 0) return views;
            List<string> ids = contexts.Select(c => c.Thread.Id).ToList();
            Dictionary<string, UserSummary> users = await _Services.Summaries.BuildAsync(contexts.Select(c => (string?)c.Thread.AuthorUserId).Concat(contexts.Select(c => c.Thread.FeedbackReviewerId)), token).ConfigureAwait(false);
            Dictionary<string, List<MediaView>> media = await _Services.Media.ListViewsForTargetsAsync(TargetTypeEnum.Thread, ids, true, false, token).ConfigureAwait(false);
            Dictionary<string, ReactionState> reactions = await _Services.Reactions.LoadAsync(TargetTypeEnum.Thread, ids, null, token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.Thread, ids, false, token).ConfigureAwait(false);
            Dictionary<string, Poll> polls = await _Services.Database.Polls.ReadByThreadsAsync(ids, token).ConfigureAwait(false);
            foreach (ThreadContext c in contexts)
            {
                ForumThread t = c.Thread;
                ReactionState state = reactions.TryGetValue(t.Id, out ReactionState? r) ? r : new ReactionState();
                ObjectMetadata meta = metadata.TryGetValue(t.Id, out ObjectMetadata? m) ? m : new ObjectMetadata();
                List<MediaView> list = media.TryGetValue(t.Id, out List<MediaView>? found) ? found : new List<MediaView>();
                views.Add(new PublicThreadView
                {
                    Id = t.Id,
                    Forum = new ForumRefView { Id = c.Forum.Id, Slug = c.Forum.Slug, Name = c.Forum.Name },
                    Kind = t.Kind,
                    Type = t.Type,
                    Author = users.TryGetValue(t.AuthorUserId, out UserSummary? u) ? u : UserSummary.Deleted(),
                    Title = t.Title,
                    Body = t.Body,
                    Excerpt = t.Excerpt,
                    Audience = t.Audience,
                    State = ForumAccessService.StateOf(t),
                    IsPinned = t.IsPinned,
                    IsLocked = t.IsLocked,
                    HasAcceptedAnswer = t.HasAcceptedAnswer,
                    Poll = polls.TryGetValue(t.Id, out Poll? poll) ? new PublicPollSummaryView { Id = poll.Id, IsClosed = IsClosed(poll, DateTime.UtcNow), ClosesUtc = poll.ClosesUtc, TotalVoters = poll.TotalVoters } : null,
                    Feedback = t.Type == ThreadTypeEnum.Feedback
                        ? new FeedbackSummaryView { Status = t.FeedbackStatus ?? FeedbackStatusEnum.Open, Reviewer = t.FeedbackReviewerId != null && users.TryGetValue(t.FeedbackReviewerId, out UserSummary? reviewer) ? reviewer : null }
                        : null,
                    CommentTarget = new ContentTarget { Type = t.CommentTargetType.ToString(), Id = t.CommentTargetId },
                    CommentCount = t.CommentCount,
                    UniqueCommenterCount = t.UniqueCommenterCount,
                    ReactionCounts = state.ReactionCounts,
                    ReactionTotal = state.ReactionTotal,
                    TrendingScore = t.TrendingScore,
                    LastActivityUtc = t.LastActivityUtc,
                    AttachmentPreview = list.Take(AttachmentPreviewMax).ToList(),
                    AttachmentCount = list.Count,
                    Labels = meta.Labels,
                    Tags = meta.Tags,
                    PublishedUtc = t.PublishedUtc,
                    CreatedUtc = t.CreatedUtc,
                    UpdatedUtc = t.UpdatedUtc
                });
            }
            return views;
        }

        /// <summary>
        /// Build a poll payload for a caller.
        /// </summary>
        /// <param name="poll">Poll.</param>
        /// <param name="thread">The poll's thread.</param>
        /// <param name="actor">Caller.</param>
        /// <param name="myVote">The caller's options.</param>
        /// <param name="metadata">Labels and tags of polls and options, keyed by identifier.</param>
        /// <returns>View.</returns>
        public PollView PollView(Poll poll, ForumThread thread, ContentActor actor, List<string> myVote, Dictionary<string, ObjectMetadata> metadata)
        {
            ArgumentNullException.ThrowIfNull(poll);
            ArgumentNullException.ThrowIfNull(thread);
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(myVote);
            ArgumentNullException.ThrowIfNull(metadata);
            DateTime now = DateTime.UtcNow;
            bool closed = IsClosed(poll, now);
            bool isAuthor = actor.UserId != null && String.Equals(thread.AuthorUserId, actor.UserId, StringComparison.Ordinal);
            bool visible = isAuthor || actor.CanModerateForum(thread.ForumId);
            if (!visible)
            {
                switch (poll.ResultsVisibility)
                {
                    case PollResultsVisibilityEnum.Always:
                        visible = true;
                        break;
                    case PollResultsVisibilityEnum.AfterVote:
                        visible = closed || myVote.Count > 0;
                        break;
                    default:
                        visible = closed;
                        break;
                }
            }
            ObjectMetadata pollMeta = metadata.TryGetValue(poll.Id, out ObjectMetadata? pm) ? pm : new ObjectMetadata();
            return new PollView
            {
                Id = poll.Id,
                ThreadId = poll.ThreadId,
                MultipleChoice = poll.MultipleChoice,
                MaxChoices = poll.MaxChoices,
                ResultsVisibility = poll.ResultsVisibility,
                VoterVisibility = poll.VoterVisibility,
                AllowVoteChange = poll.AllowVoteChange,
                ClosesUtc = poll.ClosesUtc,
                ClosedUtc = poll.ClosedUtc ?? (closed ? poll.ClosesUtc : null),
                IsClosed = closed,
                TotalVoters = poll.TotalVoters,
                ResultsVisible = visible,
                MyVote = myVote.ToList(),
                Options = poll.Options.OrderBy(o => o.Position).Select(o =>
                {
                    ObjectMetadata om = metadata.TryGetValue(o.Id, out ObjectMetadata? found) ? found : new ObjectMetadata();
                    return new PollOptionView
                    {
                        Id = o.Id,
                        Label = o.Label,
                        Position = o.Position,
                        VoteCount = visible ? o.VoteCount : null,
                        Percent = visible ? (poll.TotalVoters == 0 ? 0 : Math.Round(o.VoteCount * 100.0 / poll.TotalVoters, 1, MidpointRounding.AwayFromZero)) : null,
                        Labels = om.Labels,
                        Tags = om.Tags
                    };
                }).ToList(),
                Labels = pollMeta.Labels,
                Tags = pollMeta.Tags
            };
        }

        /// <summary>
        /// Build a poll payload, loading the caller's vote and the metadata.
        /// </summary>
        /// <param name="poll">Poll.</param>
        /// <param name="thread">The poll's thread.</param>
        /// <param name="actor">Caller.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        public async Task<PollView> PollViewAsync(Poll poll, ForumThread thread, ContentActor actor, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(poll);
            ArgumentNullException.ThrowIfNull(actor);
            List<string> mine = new List<string>();
            if (!String.IsNullOrEmpty(actor.UserId))
            {
                Dictionary<string, List<string>> votes = await _Services.Database.Polls.VotesOfAsync(actor.UserId, new[] { poll.Id }, token).ConfigureAwait(false);
                if (votes.TryGetValue(poll.Id, out List<string>? found)) mine = found;
            }
            Dictionary<string, ObjectMetadata> metadata = await PollMetadataAsync(new[] { poll }, actor, token).ConfigureAwait(false);
            return PollView(poll, thread, actor, mine, metadata);
        }

        /// <summary>
        /// Whether a poll is closed at a time (closed explicitly or past its scheduled close).
        /// </summary>
        /// <param name="poll">Poll.</param>
        /// <param name="nowUtc">Evaluation time.</param>
        /// <returns>True when closed.</returns>
        public static bool IsClosed(Poll poll, DateTime nowUtc)
        {
            ArgumentNullException.ThrowIfNull(poll);
            return poll.ClosedUtc != null || (poll.ClosesUtc != null && poll.ClosesUtc.Value <= nowUtc);
        }

        #endregion

        #region Private-Methods

        private async Task<Dictionary<string, ObjectMetadata>> PollMetadataAsync(IEnumerable<Poll> polls, ContentActor actor, CancellationToken token)
        {
            List<Poll> list = polls.ToList();
            Dictionary<string, ObjectMetadata> result = await _Services.Metadata.LoadAsync(TargetTypeEnum.Poll, list.Select(p => p.Id), actor.IsStaffFor(TargetTypeEnum.Poll), token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> options = await _Services.Metadata.LoadAsync(TargetTypeEnum.PollOption, list.SelectMany(p => p.Options.Select(o => o.Id)), actor.IsStaffFor(TargetTypeEnum.PollOption), token).ConfigureAwait(false);
            foreach (KeyValuePair<string, ObjectMetadata> pair in options) result[pair.Key] = pair.Value;
            return result;
        }

        private async Task<Dictionary<string, string?>> IconUrlsAsync(IReadOnlyList<Forum> forums, ContentActor actor, CancellationToken token)
        {
            Dictionary<string, string?> result = new Dictionary<string, string?>(StringComparer.Ordinal);
            List<Forum> withIcons = forums.Where(f => f.IconMediaId != null).ToList();
            if (withIcons.Count == 0) return result;
            Dictionary<string, MediaAttachment> media = (await _Services.Database.Media.ReadManyAsync(withIcons.Select(f => f.IconMediaId!), token).ConfigureAwait(false)).ToDictionary(m => m.Id, StringComparer.Ordinal);
            foreach (Forum forum in withIcons)
            {
                if (!media.TryGetValue(forum.IconMediaId!, out MediaAttachment? attachment) || attachment.Status != MediaStatusEnum.Ready) continue;
                MediaView view = await _Services.Media.ViewAsync(attachment, actor, token).ConfigureAwait(false);
                MediaVariantView? thumb = view.Image != null && view.Image.Variants.TryGetValue("thumb", out MediaVariantView? v) ? v : null;
                result[forum.Id] = thumb?.WebpUrl ?? thumb?.JpegUrl;
            }
            return result;
        }

        private async Task<Dictionary<string, CommentView>> AcceptedAnswersAsync(IReadOnlyList<ThreadContext> contexts, ContentActor actor, CancellationToken token)
        {
            List<string> ids = contexts.Where(c => c.Thread.HasAcceptedAnswer && c.Thread.AcceptedCommentId != null).Select(c => c.Thread.AcceptedCommentId!).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) return new Dictionary<string, CommentView>(StringComparer.Ordinal);
            List<Comment> comments = (await _Services.Database.Comments.ReadManyAsync(ids, token).ConfigureAwait(false)).Where(c => c.DeletedUtc == null && c.ModerationState == ModerationStateEnum.None).ToList();
            return await _Services.ContentViews.CommentsAsync(comments, actor, false, token).ConfigureAwait(false);
        }

        private async Task<ThreadBundle> LoadAsync(IReadOnlyList<ThreadContext> contexts, ContentActor actor, bool full, CancellationToken token)
        {
            ThreadBundle bundle = new ThreadBundle();
            List<string> ids = contexts.Select(c => c.Thread.Id).ToList();
            List<string> postIds = contexts.Where(c => c.Source != null).Select(c => c.Source!.Id).Distinct(StringComparer.Ordinal).ToList();
            List<string> forumIds = contexts.Select(c => c.Forum.Id).Distinct(StringComparer.Ordinal).ToList();
            IEnumerable<string?> people = contexts.Select(c => (string?)c.Thread.AuthorUserId)
                .Concat(contexts.Select(c => c.Thread.PromotedByUserId))
                .Concat(contexts.Select(c => c.Thread.FeedbackReviewerId))
                .Concat(contexts.Where(c => c.Source != null).Select(c => (string?)c.Source!.AuthorUserId));
            bundle.Users = await _Services.Summaries.BuildAsync(people, token).ConfigureAwait(false);
            bool mediaStaff = actor.IsStaffFor(TargetTypeEnum.Media);
            // Signed-out visitors (anonymous browsing) get the public copies, which exist only for public sample content.
            bool publicPaths = actor.IsAnonymous || String.IsNullOrEmpty(actor.UserId);
            bundle.Media = await _Services.Media.ListViewsForTargetsAsync(TargetTypeEnum.Thread, ids, publicPaths, mediaStaff, token).ConfigureAwait(false);
            if (postIds.Count > 0) bundle.PostMedia = await _Services.Media.ListViewsForTargetsAsync(TargetTypeEnum.FeedPost, postIds, publicPaths, mediaStaff, token).ConfigureAwait(false);
            if (full)
            {
                bundle.Mentions = await _Services.Mentions.LoadViewsAsync(TargetTypeEnum.Thread, ids, token).ConfigureAwait(false);
                if (postIds.Count > 0) bundle.PostMentions = await _Services.Mentions.LoadViewsAsync(TargetTypeEnum.FeedPost, postIds, token).ConfigureAwait(false);
                IEnumerable<string?> previews = contexts.Select(c => c.Thread.ShowLinkPreview ? c.Thread.LinkPreviewId : null)
                    .Concat(contexts.Where(c => c.Source != null && c.Source.ShowLinkPreview).Select(c => c.Source!.LinkPreviewId));
                bundle.Previews = await _Services.LinkPreviews.LoadViewsAsync(previews, token).ConfigureAwait(false);
            }
            List<string> threadTargets = contexts.Where(c => c.Thread.CommentTargetType == TargetTypeEnum.Thread).Select(c => c.Thread.Id).ToList();
            List<string> postTargets = contexts.Where(c => c.Thread.CommentTargetType == TargetTypeEnum.FeedPost).Select(c => c.Thread.CommentTargetId).Distinct(StringComparer.Ordinal).ToList();
            bundle.Reactions = await _Services.Reactions.LoadAsync(TargetTypeEnum.Thread, threadTargets, actor.UserId, token).ConfigureAwait(false);
            if (postTargets.Count > 0) bundle.PostReactions = await _Services.Reactions.LoadAsync(TargetTypeEnum.FeedPost, postTargets, actor.UserId, token).ConfigureAwait(false);
            bundle.Metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.Thread, ids, actor.IsStaffFor(TargetTypeEnum.Thread), token).ConfigureAwait(false);
            bundle.Admins = await _Services.Summaries.SystemAdminsAsync(contexts.Select(c => c.Thread.AuthorUserId).Distinct(StringComparer.Ordinal), token).ConfigureAwait(false);
            bundle.Polls = await _Services.Database.Polls.ReadByThreadsAsync(contexts.Where(c => c.Thread.Type == ThreadTypeEnum.Poll).Select(c => c.Thread.Id), token).ConfigureAwait(false);
            if (full && bundle.Polls.Count > 0) bundle.PollMeta = await PollMetadataAsync(bundle.Polls.Values, actor, token).ConfigureAwait(false);
            if (!String.IsNullOrEmpty(actor.UserId))
            {
                bundle.Followed = await _Services.Database.Threads.FollowedAmongAsync(actor.UserId, ids, token).ConfigureAwait(false);
                bundle.ThreadReads = await _Services.Database.Threads.ReadTimesAsync(actor.UserId, ids, token).ConfigureAwait(false);
                bundle.ForumReads = await _Services.Database.Forums.ReadTimesAsync(actor.UserId, forumIds, token).ConfigureAwait(false);
                bundle.Blockers = await _Services.Blocks.BlockersOfAsync(actor.UserId, token).ConfigureAwait(false);
                bundle.Muted = await _Services.Forums.Access.MutedForumsAsync(actor, forumIds, token).ConfigureAwait(false);
                if (bundle.Polls.Count > 0) bundle.MyVotes = await _Services.Database.Polls.VotesOfAsync(actor.UserId, bundle.Polls.Values.Select(p => p.Id), token).ConfigureAwait(false);
            }
            bundle.UnreadWindowStart = DateTime.UtcNow.AddDays(-_Services.Settings.Forums.UnreadWindowDays);
            return bundle;
        }

        private void FillCore(ThreadCoreView view, ThreadContext c, ContentActor actor, bool moderatorView, ThreadBundle bundle)
        {
            ForumThread t = c.Thread;
            DateTime now = DateTime.UtcNow;
            ThreadStateEnum state = ForumAccessService.StateOf(t);
            bool isAuthor = actor.UserId != null && String.Equals(t.AuthorUserId, actor.UserId, StringComparison.Ordinal);
            bool canAct = !String.IsNullOrEmpty(actor.UserId);
            bool canTakePart = canAct && ForumAccessService.ModelAllowsParticipation(actor, ForumAccessService.Narrowest(t.Audience, c.Forum.Audience));
            bool published = state == ThreadStateEnum.Published;
            bool linked = t.Kind == ThreadKindEnum.LinkedFeedPost;
            bool authorIsAdmin = bundle.Admins.Contains(t.AuthorUserId);
            bool inScope = ForumAccessService.CanModerate(actor, t.ForumId, authorIsAdmin);
            bool muted = bundle.Muted.Contains(t.ForumId);
            bool blockedByAuthor = !isAuthor && bundle.Blockers.Contains(t.AuthorUserId);
            bool open = !t.IsLocked && !c.Forum.IsArchived;
            ReactionState reaction = t.CommentTargetType == TargetTypeEnum.FeedPost
                ? (bundle.PostReactions.TryGetValue(t.CommentTargetId, out ReactionState? pr) ? pr : new ReactionState())
                : (bundle.Reactions.TryGetValue(t.Id, out ReactionState? tr) ? tr : new ReactionState());
            ObjectMetadata meta = bundle.Metadata.TryGetValue(t.Id, out ObjectMetadata? m) ? m : new ObjectMetadata();
            Poll? poll = bundle.Polls.TryGetValue(t.Id, out Poll? found) ? found : null;
            bool pollOpen = poll != null && !IsClosed(poll, now);
            view.Id = t.Id;
            view.Forum = new ForumRefView { Id = c.Forum.Id, Slug = c.Forum.Slug, Name = c.Forum.Name };
            view.Kind = t.Kind;
            view.Type = t.Type;
            view.Author = bundle.Users.TryGetValue(t.AuthorUserId, out UserSummary? u) ? u : UserSummary.Deleted();
            view.Title = t.Title;
            view.Excerpt = linked && c.Source != null ? c.Source.Excerpt : t.Excerpt;
            view.Audience = t.Audience;
            view.State = state;
            view.IsPinned = t.IsPinned;
            view.PinnedUtc = t.PinnedUtc;
            view.IsLocked = t.IsLocked;
            view.LockedUtc = t.LockedUtc;
            view.LockReason = t.LockReason;
            view.CommentTarget = new ContentTarget { Type = t.CommentTargetType.ToString(), Id = t.CommentTargetId };
            if (linked && t.SourceId != null)
            {
                view.Source = new ThreadSourceView
                {
                    Type = t.SourceType ?? TargetTypeEnum.FeedPost.ToString(),
                    Id = t.SourceId,
                    Title = c.Source?.Title,
                    Author = c.Source != null && bundle.Users.TryGetValue(c.Source.AuthorUserId, out UserSummary? sa) ? sa : null
                };
            }
            if (t.PromotedFeedPostId != null && t.PromotedUtc != null)
            {
                view.Promotion = new ThreadPromotionView
                {
                    FeedPostId = t.PromotedFeedPostId,
                    PromotedBy = t.PromotedByUserId != null && bundle.Users.TryGetValue(t.PromotedByUserId, out UserSummary? pb) ? pb : null,
                    PromotedUtc = t.PromotedUtc.Value
                };
            }
            view.CommentCount = t.CommentCount;
            view.UniqueCommenterCount = t.UniqueCommenterCount;
            view.ReactionCounts = reaction.ReactionCounts;
            view.ReactionTotal = reaction.ReactionTotal;
            view.MyReactions = reaction.MyReactions;
            view.TrendingScore = t.TrendingScore;
            view.LastActivityUtc = t.LastActivityUtc;
            view.LastCommentUtc = t.LastCommentUtc;
            view.IsFollowed = bundle.Followed.Contains(t.Id);
            view.IsUnread = canAct && IsUnread(t, bundle);
            view.EditedUtc = t.EditedUtc;
            view.RevisionCount = t.RevisionCount;
            bool showModeration = moderatorView || (t.ModerationState != ModerationStateEnum.None && (isAuthor || inScope));
            view.Moderation = showModeration && t.ModerationState != ModerationStateEnum.None ? new ModerationInfo { State = t.ModerationState, Reason = t.ModerationReason, ActedUtc = t.ModeratedUtc } : null;
            view.Labels = meta.Labels;
            view.Tags = meta.Tags;
            view.PublishedUtc = t.PublishedUtc;
            view.CreatedUtc = t.CreatedUtc;
            view.UpdatedUtc = t.UpdatedUtc;
            view.Viewer = new ThreadViewer
            {
                CanEdit = actor.IsSystemAdmin || (isAuthor && actor.CanWrite && !linked) || inScope,
                CanDelete = actor.IsSystemAdmin || isAuthor || inScope,
                CanComment = published && canTakePart && actor.CanWrite && open && !muted && !blockedByAuthor,
                CanReact = published && canTakePart && (actor.IsSystemAdmin || !actor.IsMuted) && !t.IsLocked && !muted && !blockedByAuthor,
                CanReport = canAct && !isAuthor,
                CanModerate = inScope,
                CanPin = inScope && published,
                CanLock = inScope,
                CanMove = inScope,
                CanAcceptAnswer = t.Type == ThreadTypeEnum.Question && published && (isAuthor || inScope),
                CanVote = t.Type == ThreadTypeEnum.Poll && pollOpen && published && canTakePart && (actor.IsSystemAdmin || !actor.IsMuted) && open && !muted,
                CanClosePoll = t.Type == ThreadTypeEnum.Poll && pollOpen && (isAuthor || inScope),
                CanSetFeedbackStatus = t.Type == ThreadTypeEnum.Feedback && (inScope || isAuthor),
                CanPromote = published && !linked && t.PromotedFeedPostId == null && (actor.IsSystemAdmin || actor.CanPromoteToFeed),
                CanFollow = canAct,
                CanAttachMedia = actor.CanAttachMedia && c.Forum.AllowMedia,
                CanAttachVideo = actor.CanAttachVideo && c.Forum.AllowMedia && c.Forum.AllowVideo
            };
        }

        private static bool IsUnread(ForumThread thread, ThreadBundle bundle)
        {
            DateTime? threadRead = bundle.ThreadReads.TryGetValue(thread.Id, out DateTime tr) ? tr : null;
            DateTime? forumRead = bundle.ForumReads.TryGetValue(thread.ForumId, out DateTime fr) ? fr : null;
            DateTime? read = threadRead == null ? forumRead : (forumRead == null ? threadRead : (threadRead.Value > forumRead.Value ? threadRead : forumRead));
            return thread.LastActivityUtc > (read ?? bundle.UnreadWindowStart);
        }

        #endregion
    }
}
