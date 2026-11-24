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
    /// Builds Idea payloads (docs/PHASE5_CONTRACT.md sections 1.4 and 2.4) in batches: status blocks, authors (hidden when
    /// showAuthor is false, except to staff), reactions, the caller's vote and follow, merged targets, labels and tags,
    /// locked stubs, staff voter counts, and the viewer block. Voters are never listed.
    /// </summary>
    public class IdeaViewBuilder
    {
        #region Private-Members

        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the builder.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public IdeaViewBuilder(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build payloads in the order given.
        /// </summary>
        /// <param name="ideas">Ideas the caller may receive (readable or locked).</param>
        /// <param name="actor">Caller.</param>
        /// <param name="statuses">Every status.</param>
        /// <param name="list">True for list items (IdeaSummary: body null, excerpt set).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Payloads.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task<List<IdeaView>> BuildAsync(List<Idea> ideas, ContentActor actor, List<IdeaStatus> statuses, bool list, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ideas);
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(statuses);
            List<IdeaView> views = new List<IdeaView>();
            if (ideas.Count == 0) return views;
            bool staff = IdeaService.IsStaff(actor);
            string? userId = actor.IsAnonymous ? null : actor.UserId;
            List<string> ids = ideas.Select(i => i.Id).ToList();
            List<string> open = ideas.Where(i => !IdeaService.IsLocked(i, actor)).Select(i => i.Id).ToList();
            Dictionary<string, UserSummary> authors = await _Services.Summaries.BuildAsync(ideas.Where(i => i.AuthorUserId != null && (i.ShowAuthor || staff)).Select(i => i.AuthorUserId), token).ConfigureAwait(false);
            Dictionary<string, ReactionState> reactions = await _Services.Reactions.LoadAsync(TargetTypeEnum.Idea, open, userId, token).ConfigureAwait(false);
            HashSet<string> voted = userId == null ? new HashSet<string>(StringComparer.Ordinal) : await _Services.Database.Ideas.VotedAmongAsync(userId, open, token).ConfigureAwait(false);
            HashSet<string> followed = userId == null ? new HashSet<string>(StringComparer.Ordinal) : await _Services.Database.Ideas.FollowedAmongAsync(userId, open, token).ConfigureAwait(false);
            Dictionary<string, Idea> targets = (await _Services.Database.Ideas.ReadManyAsync(ideas.Where(i => i.MergedIntoId != null).Select(i => i.MergedIntoId!), token).ConfigureAwait(false))
                .ToDictionary(i => i.Id, StringComparer.Ordinal);
            IdeaStatus? initial = statuses.FirstOrDefault(s => s.IsInitial && !s.IsRetired) ?? statuses.FirstOrDefault(s => !s.IsRetired);
            bool voteOnClosed = _Services.Settings.Ideas.VoteOnClosed;
            DateTime now = DateTime.UtcNow;
            foreach (Idea idea in ideas)
            {
                bool locked = IdeaService.IsLocked(idea, actor);
                IdeaStatus? status = IdeaStatusService.Find(statuses, idea.StatusKey);
                bool author = userId != null && idea.AuthorUserId != null && String.Equals(idea.AuthorUserId, userId, StringComparison.Ordinal);
                IdeaView view = new IdeaView
                {
                    Id = idea.Id,
                    Title = idea.Title,
                    Body = locked ? String.Empty : (list ? null : idea.Body),
                    Excerpt = locked ? String.Empty : MarkdownNormalizer.Excerpt(idea.Body),
                    Status = IdeaStatusService.Ref(statuses, idea.StatusKey),
                    StatusNote = idea.StatusNote,
                    StatusUtc = idea.StatusUtc,
                    VoteCount = idea.VoteCount,
                    HasVoted = voted.Contains(idea.Id),
                    IsFollowed = followed.Contains(idea.Id),
                    CommentTarget = new ContentTarget { Type = TargetTypeEnum.Idea.ToString(), Id = idea.Id },
                    CommentCount = idea.CommentCount,
                    Audience = idea.Audience,
                    Locked = locked,
                    EditedUtc = idea.EditedUtc,
                    CreatedUtc = idea.CreatedUtc,
                    UpdatedUtc = idea.UpdatedUtc
                };
                if (idea.ShowAuthor || staff)
                    view.Author = idea.AuthorUserId == null ? (idea.ShowAuthor ? UserSummary.Deleted() : null)
                        : (authors.TryGetValue(idea.AuthorUserId, out UserSummary? summary) ? summary : UserSummary.Deleted());
                if (idea.MergedIntoId != null)
                {
                    targets.TryGetValue(idea.MergedIntoId, out Idea? target);
                    view.MergedInto = new IdeaMergedRefView { Id = idea.MergedIntoId, Title = target != null && IdeaService.CanRead(target, actor) ? target.Title : String.Empty };
                }
                if (!locked && reactions.TryGetValue(idea.Id, out ReactionState? state))
                {
                    view.ReactionCounts = state.ReactionCounts;
                    view.ReactionTotal = state.ReactionTotal;
                    view.MyReactions = state.MyReactions;
                }
                if (staff || author)
                {
                    if (idea.ModerationState != ModerationStateEnum.None) view.Moderation = new ModerationInfo { State = idea.ModerationState, Reason = staff ? idea.ModerationReason : null, ActedUtc = idea.ModeratedUtc };
                }
                if (staff)
                {
                    view.SourceTicketId = idea.SourceTicketId;
                    if (!list) view.VoterCounts = await _Services.Database.Ideas.VoterCountsAsync(idea.Id, now, token).ConfigureAwait(false);
                }
                view.Viewer = Viewer(idea, actor, status, initial, locked, author, staff, voteOnClosed);
                views.Add(view);
            }
            await _Services.Metadata.AttachAsync(TargetTypeEnum.Idea, views, v => v.Id, actor.IsStaffFor(TargetTypeEnum.Idea), token).ConfigureAwait(false);
            return views;
        }

        #endregion

        #region Private-Methods

        private static IdeaViewerView Viewer(Idea idea, ContentActor actor, IdeaStatus? status, IdeaStatus? initial, bool locked, bool author, bool staff, bool voteOnClosed)
        {
            if (locked) return new IdeaViewerView { CanOpen = false, Requires = "Membership" };
            bool participate = IdeaService.ModelAllowsParticipation(idea, actor) && (staff || !actor.IsMuted) && idea.ModerationState == ModerationStateEnum.None;
            bool merged = idea.MergedIntoId != null;
            bool closed = status != null && status.IsClosed;
            bool initialStatus = initial != null && String.Equals(idea.StatusKey, initial.Key, StringComparison.OrdinalIgnoreCase);
            return new IdeaViewerView
            {
                CanVote = participate && !merged && (!closed || voteOnClosed),
                CanComment = participate && !merged && actor.CanPost,
                CanReact = participate,
                CanReport = !actor.IsAnonymous && !String.IsNullOrEmpty(actor.UserId) && !author,
                CanEdit = staff || (author && initialStatus && !merged && !actor.IsAnonymous),
                CanSetStatus = staff,
                CanMerge = staff,
                CanOpen = true,
                Requires = null
            };
        }

        #endregion
    }
}
