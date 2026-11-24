namespace TrellisKit.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;

    /// <summary>
    /// The Phase 5 ideas board services of one property (docs/PHASE5_CONTRACT.md sections 7 and 18) and their wiring into
    /// the Phase 2 services: idea comments (comment host, visibility, notifications), reactions, reports and moderation
    /// (scope Ideas), notification fan-out (IdeaStatusChanged), account export, and account deletion.
    /// </summary>
    public class IdeaModule
    {
        #region Public-Members

        /// <summary>
        /// Ideas (member, staff, and public routes).
        /// </summary>
        public IdeaService Ideas { get; }

        /// <summary>
        /// Idea statuses.
        /// </summary>
        public IdeaStatusService Statuses { get; }

        /// <summary>
        /// Payload builder.
        /// </summary>
        public IdeaViewBuilder Views { get; }

        /// <summary>
        /// Notifications and email.
        /// </summary>
        public IdeaNotificationService Notifications { get; }

        /// <summary>
        /// Moderation extension (scope Ideas).
        /// </summary>
        public IdeaModerationTarget Moderation { get; }

        /// <summary>
        /// Ideas from support tickets.
        /// </summary>
        public IdeaTicketService Tickets { get; }

        /// <summary>
        /// Clock of the trending recompute (tests set a fixed time). Default DateTime.UtcNow. Never null.
        /// </summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public Func<DateTime> Clock
        {
            get { return _Clock; }
            set { _Clock = value ?? throw new ArgumentNullException(nameof(Clock)); }
        }

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;
        private Func<DateTime> _Clock = () => DateTime.UtcNow;
        private int _Attached = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the module (call <see cref="Attach"/> once the Phase 2 and forum services exist).
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public IdeaModule(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            Ideas = new IdeaService(services);
            Statuses = new IdeaStatusService(services);
            Views = new IdeaViewBuilder(services);
            Notifications = new IdeaNotificationService(services);
            Moderation = new IdeaModerationTarget(services);
            Tickets = new IdeaTicketService(services);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Wire the ideas module into the shared services (idempotent). Attach after the forum module: the notification
        /// fan-out hook chains the forum hook.
        /// </summary>
        public void Attach()
        {
            if (Interlocked.Exchange(ref _Attached, 1) != 0) return;
            _Services.ContentAccess.IdeaVisible = Ideas.VisibleAsync;
            _Services.Comments.IdeaHostResolver = HostAsync;
            _Services.Comments.IdeaAfterCreate = Notifications.CommentCreatedAsync;
            _Services.Reactions.ExtraTargets.Add(ReactionTargetAsync);
            _Services.Reports.ExtraTargets.Add(ReportTargetAsync);
            _Services.Reports.ExtraTargetTypes.Add(TargetTypeEnum.Idea);
            _Services.Moderation.Extensions.Add(Moderation);
            Func<NotificationFanout, string, TimeSpan, CancellationToken, Task<int>>? fanout = _Services.Notifications.ExtraFanout;
            _Services.Notifications.ExtraFanout = async (job, holder, claim, token) =>
            {
                if (job.Type == NotificationTypeEnum.IdeaStatusChanged) return await Notifications.ProcessFanoutAsync(job, holder, claim, token).ConfigureAwait(false);
                return fanout == null ? 0 : await fanout(job, holder, claim, token).ConfigureAwait(false);
            };
            IdeaExportSections.Register(_Services);
            Func<string, CancellationToken, Task>? previous = _Services.Accounts.AccountClosed;
            _Services.Accounts.AccountClosed = async (userId, token) =>
            {
                await EraseForAccountAsync(userId, token).ConfigureAwait(false);
                if (previous != null) await previous(userId, token).ConfigureAwait(false);
            };
        }

        /// <summary>
        /// Account deletion (section 12): votes deleted with counts recomputed, follows deleted, authored ideas kept with
        /// "Deleted member" unless still in the initial status (then deleted).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Ideas deleted.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        public async Task<int> EraseForAccountAsync(string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            IdeaStatus initial = await Statuses.InitialAsync(token).ConfigureAwait(false);
            return await _Services.Database.Ideas.EraseForUserAsync(userId, initial.Key, DateTime.UtcNow, token).ConfigureAwait(false);
        }

        /// <summary>
        /// The trending worker's pass: recompute recentvotecount from the votes of the last ideas.trendingDays.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Ideas whose count changed.</returns>
        public Task<int> RecomputeTrendingAsync(CancellationToken token = default)
        {
            if (!_Services.Property.Property.Features.Ideas) return Task.FromResult(0);
            return _Services.Database.Ideas.RecomputeTrendingAsync(_Clock().AddDays(-_Services.Settings.Ideas.TrendingDays), token);
        }

        #endregion

        #region Private-Methods

        private async Task<CommentHost> HostAsync(ContentActor actor, string ideaId, bool forWrite, CancellationToken token)
        {
            Idea idea = await Ideas.RequireReadableAsync(actor, ideaId, token).ConfigureAwait(false);
            if (forWrite)
            {
                Ideas.RequireParticipation(idea, actor);
                await Ideas.RequireNotMergedAsync(idea, actor, token).ConfigureAwait(false);
            }
            return new CommentHost
            {
                TargetType = TargetTypeEnum.Idea,
                TargetId = idea.Id,
                AuthorUserId = idea.AuthorUserId ?? String.Empty,
                CommentCount = idea.CommentCount,
                AllowMedia = true,
                AllowVideo = true,
                ModeratorInScope = IdeaService.IsStaff(actor) || (actor.AllForumsModerator && _Services.Settings.Ideas.ForumModeratorsModerate),
                Idea = idea
            };
        }

        private async Task<ReactionTarget?> ReactionTargetAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, bool forWrite, CancellationToken token)
        {
            if (targetType == TargetTypeEnum.Idea)
            {
                Idea idea = await Ideas.RequireReadableAsync(actor, targetId, token).ConfigureAwait(false);
                if (forWrite) Ideas.RequireParticipation(idea, actor);
                return new ReactionTarget
                {
                    AuthorUserId = idea.AuthorUserId ?? actor.UserId ?? String.Empty,
                    Excerpt = idea.Title,
                    Path = IdeaNotificationService.IdeaPath(idea.Id, null)
                };
            }
            if (targetType != TargetTypeEnum.Comment) return null;
            Comment? comment = String.IsNullOrWhiteSpace(targetId) ? null : await _Services.Database.Comments.ReadAsync(targetId.Trim(), token).ConfigureAwait(false);
            if (comment == null || comment.TargetType != TargetTypeEnum.Idea) return null;
            Comment visible = await _Services.ContentAccess.RequireCommentAsync(actor, comment.Id, token).ConfigureAwait(false);
            if (!ContentAccessService.CanSeeCommentContent(visible, actor)) throw ApiErrorException.NotFound("Comment");
            if (forWrite)
            {
                if (visible.ModerationState != ModerationStateEnum.None || visible.DeletedUtc != null) throw ApiErrorException.NotFound("Comment");
                Idea idea = await Ideas.RequireReadableAsync(actor, visible.TargetId, token).ConfigureAwait(false);
                Ideas.RequireParticipation(idea, actor);
            }
            return new ReactionTarget { AuthorUserId = visible.AuthorUserId, Excerpt = visible.Excerpt, Path = IdeaNotificationService.IdeaPath(visible.TargetId, visible.Id) };
        }

        private async Task<ModerationTarget?> ReportTargetAsync(ContentActor actor, TargetTypeEnum targetType, string targetId, CancellationToken token)
        {
            string id = (targetId ?? String.Empty).Trim();
            if (targetType == TargetTypeEnum.Idea)
            {
                Idea idea = await Ideas.RequireReadableAsync(actor, id, token).ConfigureAwait(false);
                return IdeaModerationTarget.TargetOf(idea);
            }
            if (targetType != TargetTypeEnum.Comment || id.Length == 0) return null;
            Comment? comment = await _Services.Database.Comments.ReadAsync(id, token).ConfigureAwait(false);
            if (comment == null || comment.TargetType != TargetTypeEnum.Idea) return null;
            Comment visible = await _Services.ContentAccess.RequireCommentAsync(actor, id, token).ConfigureAwait(false);
            if (!ContentAccessService.CanSeeCommentContent(visible, actor)) throw ApiErrorException.NotFound("Comment");
            ModerationTarget target = new ModerationTarget { TargetType = TargetTypeEnum.Comment, TargetId = visible.Id, AuthorUserId = visible.AuthorUserId, Comment = visible };
            IdeaModerationTarget.Claim(target, visible);
            return target;
        }

        #endregion
    }
}
