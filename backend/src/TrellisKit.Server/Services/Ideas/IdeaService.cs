namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// The ideas board (docs/PHASE5_CONTRACT.md section 7): member routes (list, read, submit, edit, vote, follow), staff
    /// routes (list, create, edit, status, merge, unmerge, delete), and the public board. Audience follows
    /// docs/ACCESS_MODELS_CONTRACT.md section 3.3; ideas staff (SystemAdmin, SupportAgent) see every idea.
    /// Thread safety: safe to call concurrently; counters are recomputed from source rows.
    /// </summary>
    public class IdeaService
    {
        #region Public-Members

        /// <summary>
        /// Shortest title.
        /// </summary>
        public const int MinTitleLength = 3;

        /// <summary>
        /// Longest title.
        /// </summary>
        public const int MaxTitleLength = 140;

        /// <summary>
        /// Longest body.
        /// </summary>
        public const int MaxBodyLength = 10000;

        /// <summary>
        /// Longest status note.
        /// </summary>
        public const int MaxStatusNoteLength = 1000;

        /// <summary>
        /// Most public board results per page.
        /// </summary>
        public const int MaxPublicResults = 50;

        /// <summary>
        /// Rate limit bucket of idea submissions (ideas.ideasPerDay).
        /// </summary>
        public const string CreateBucket = "ideas.create";

        /// <summary>
        /// Rate limit bucket of vote changes (ideas.votesPerMinute).
        /// </summary>
        public const string VoteBucket = "ideas.votes";

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public IdeaService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a caller is ideas staff: SystemAdmin (or the break-glass token) or SupportAgent.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <returns>True when staff.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        public static bool IsStaff(ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(actor);
            return actor.IsSystemAdmin || actor.IsSupportAgent;
        }

        /// <summary>
        /// Fail with 404 NotAvailable when the property turned the ideas board off (features.ideas false).
        /// </summary>
        /// <exception cref="ApiErrorException">404 NotAvailable.</exception>
        public void RequireEnabled()
        {
            if (!_Services.Property.Property.Features.Ideas) throw new ApiErrorException(404, "NotAvailable", "The ideas board is not available in this community.");
        }

        /// <summary>
        /// Whether a caller may read an idea: never deleted; staff always; the author always; otherwise unmoderated and
        /// the audience is met.
        /// </summary>
        /// <param name="idea">Idea.</param>
        /// <param name="actor">Caller.</param>
        /// <returns>True when readable.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static bool CanRead(Idea idea, ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(idea);
            ArgumentNullException.ThrowIfNull(actor);
            if (idea.DeletedUtc != null) return false;
            if (IsStaff(actor)) return true;
            if (actor.UserId != null && idea.AuthorUserId != null && String.Equals(idea.AuthorUserId, actor.UserId, StringComparison.Ordinal)) return true;
            if (idea.ModerationState != ModerationStateEnum.None) return false;
            return ContentAudienceRule.Meets(idea.Audience, actor);
        }

        /// <summary>
        /// Whether a caller sees an idea as a locked stub (PartiallyGated, signed in without a membership, Members audience).
        /// </summary>
        /// <param name="idea">Idea.</param>
        /// <param name="actor">Caller.</param>
        /// <returns>True when locked.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static bool IsLocked(Idea idea, ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(idea);
            ArgumentNullException.ThrowIfNull(actor);
            if (CanRead(idea, actor) || idea.DeletedUtc != null || idea.ModerationState != ModerationStateEnum.None) return false;
            return AccessDecider.ShowsLocked(actor.Access, AccessDecider.TierOf(actor), idea.Audience);
        }

        /// <summary>
        /// Whether the access model lets a caller take part on an idea (vote, comment, react): the higher of the idea's
        /// read tier and the ideas write tier, never signed out.
        /// </summary>
        /// <param name="idea">Idea.</param>
        /// <param name="actor">Caller.</param>
        /// <returns>True when allowed.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public static bool ModelAllowsParticipation(Idea idea, ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(idea);
            ArgumentNullException.ThrowIfNull(actor);
            if (actor.IsAnonymous || String.IsNullOrEmpty(actor.UserId)) return false;
            if (IsStaff(actor)) return true;
            return AccessDecider.Meets(AccessDecider.TierOf(actor), AccessDecider.ParticipateTier(actor.Access, FeatureKeyEnum.Ideas, idea.Audience));
        }

        /// <summary>
        /// Whether a caller may read an idea by identifier (comment and media visibility).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="ideaId">Idea identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when readable.</returns>
        public async Task<bool> VisibleAsync(ContentActor actor, string ideaId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (!_Services.Property.Property.Features.Ideas) return false;
            Idea? idea = String.IsNullOrWhiteSpace(ideaId) ? null : await _Services.Database.Ideas.ReadAsync(ideaId.Trim(), token).ConfigureAwait(false);
            return idea != null && CanRead(idea, actor);
        }

        /// <summary>
        /// Load an idea the caller may read; a locked one answers 401 (signed out) or 402 (locked stub).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="ideaId">Idea identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Idea.</returns>
        /// <exception cref="ApiErrorException">401 Unauthorized, 402 MembershipRequired, 404 NotFound or NotAvailable.</exception>
        public async Task<Idea> RequireReadableAsync(ContentActor actor, string ideaId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            RequireEnabled();
            Idea? idea = String.IsNullOrWhiteSpace(ideaId) ? null : await _Services.Database.Ideas.ReadAsync(ideaId.Trim(), token).ConfigureAwait(false);
            if (idea == null || idea.DeletedUtc != null) throw ApiErrorException.NotFound("Idea");
            if (CanRead(idea, actor)) return idea;
            if (idea.ModerationState == ModerationStateEnum.None)
            {
                AccessTierEnum required = AccessDecider.ReadTier(actor.Access, idea.Audience);
                if (!AccessDecider.Meets(AccessDecider.TierOf(actor), required))
                {
                    if (actor.IsAnonymous) throw Denied(401, "Unauthorized", "Sign in to see this.", "SignIn", idea.Id);
                    if (AccessDecider.ShowsLocked(actor.Access, AccessDecider.TierOf(actor), idea.Audience)) throw Denied(402, "MembershipRequired", "This is for members.", "Membership", idea.Id);
                }
            }
            throw ApiErrorException.NotFound("Idea");
        }

        /// <summary>
        /// Throw 409 IdeaMerged naming the target when an idea is merged.
        /// </summary>
        /// <param name="idea">Idea.</param>
        /// <param name="actor">Caller (the target title is shown only when readable).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">409 IdeaMerged.</exception>
        public async Task RequireNotMergedAsync(Idea idea, ContentActor actor, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(idea);
            if (String.IsNullOrEmpty(idea.MergedIntoId)) return;
            Idea? target = await _Services.Database.Ideas.ReadAsync(idea.MergedIntoId, token).ConfigureAwait(false);
            string message = "This idea was merged into another idea; vote or comment there.";
            IdeaMergedErrorResponse body = new IdeaMergedErrorResponse
            {
                Error = "IdeaMerged",
                Message = message,
                MergedInto = new IdeaMergedRefView { Id = idea.MergedIntoId, Title = target != null && CanRead(target, actor) ? target.Title : String.Empty }
            };
            throw new ApiErrorException(409, "IdeaMerged", message, null, body);
        }

        /// <summary>
        /// Check that a caller may take part on an idea (vote, comment, react): access model, mute.
        /// </summary>
        /// <param name="idea">Idea.</param>
        /// <param name="actor">Caller.</param>
        /// <exception cref="ApiErrorException">401 Unauthorized, 402 MembershipRequired, 403 Muted, 404 NotFound.</exception>
        public void RequireParticipation(Idea idea, ContentActor actor)
        {
            ArgumentNullException.ThrowIfNull(idea);
            ArgumentNullException.ThrowIfNull(actor);
            if (actor.IsAnonymous || String.IsNullOrEmpty(actor.UserId))
                throw new ApiErrorException(401, "Unauthorized", "Sign in to take part.");
            if (idea.ModerationState != ModerationStateEnum.None && !IsStaff(actor)) throw ApiErrorException.NotFound("Idea");
            if (!ModelAllowsParticipation(idea, actor))
                throw Denied(402, "MembershipRequired", "Taking part is for members.", "Membership", idea.Id);
            actor.RequireNotMuted();
        }

        /// <summary>
        /// POST /api/v1/ideas/enumerate: the board (IdeaCollections.Ideas). Members ideas appear as locked stubs to
        /// signed-in non-members in PartiallyGated.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of IdeaSummary.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 404 NotAvailable.</exception>
        public async Task<EnumerationResult<IdeaView>> ListAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            RequireEnabled();
            EnumerationQuery q = query ?? new EnumerationQuery();
            List<IdeaStatus> statuses = await _Services.Ideas.Statuses.AllAsync(token).ConfigureAwait(false);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            IdeaFilter filter = BuildFilter(actor, q, statuses, false, errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            bool staff = actor.IsStaffFor(TargetTypeEnum.Idea);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, IdeaCollections.Ideas, staff, "ideas|" + (actor.UserId ?? "-") + "|" + String.Join(",", filter.Audiences));
            filter.RelevanceText = request.Query.Search;
            EnumerationResult<Idea> page = await _Services.Database.Ideas.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            return page.With(await _Services.Ideas.Views.BuildAsync(page.Objects, actor, statuses, true, token).ConfigureAwait(false));
        }

        /// <summary>
        /// POST /api/v1/admin/ideas/enumerate: every idea in any state (IdeaCollections.AdminIdeas; filters add hidden and merged).
        /// </summary>
        /// <param name="actor">Ideas staff.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotAvailable.</exception>
        public async Task<EnumerationResult<IdeaView>> AdminListAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            RequireEnabled();
            RequireStaff(actor);
            EnumerationQuery q = query ?? new EnumerationQuery();
            List<IdeaStatus> statuses = await _Services.Ideas.Statuses.AllAsync(token).ConfigureAwait(false);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            IdeaFilter filter = BuildFilter(actor, q, statuses, true, errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, IdeaCollections.AdminIdeas, true, "admin-ideas");
            filter.RelevanceText = request.Query.Search;
            EnumerationResult<Idea> page = await _Services.Database.Ideas.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            return page.With(await _Services.Ideas.Views.BuildAsync(page.Objects, actor, statuses, true, token).ConfigureAwait(false));
        }

        /// <summary>
        /// GET /api/v1/ideas/{id}: the idea (a merged idea returns its own payload with mergedInto).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="ideaId">Idea identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Idea.</returns>
        /// <exception cref="ApiErrorException">401, 402, 404.</exception>
        public async Task<IdeaView> GetAsync(ContentActor actor, string ideaId, CancellationToken token = default)
        {
            Idea idea = await RequireReadableAsync(actor, ideaId, token).ConfigureAwait(false);
            return await ViewAsync(idea, actor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// GET /api/v1/admin/ideas/{id}: the idea for staff, with voter counts by membership state.
        /// </summary>
        /// <param name="actor">Ideas staff.</param>
        /// <param name="ideaId">Idea identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Idea.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task<IdeaView> AdminGetAsync(ContentActor actor, string ideaId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            RequireEnabled();
            RequireStaff(actor);
            Idea idea = await RequireLiveAsync(ideaId, token).ConfigureAwait(false);
            return await ViewAsync(idea, actor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/ideas: submit an idea (only when ideas.memberSubmissions is Direct). The author votes and follows.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information (audit).</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The idea.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or ContentFiltered, 403 Forbidden, PostingNotAllowed, or Muted, 429 RateLimited.</exception>
        public async Task<IdeaView> CreateAsync(ContentActor actor, RequestInfo info, IdeaRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            RequireEnabled();
            string userId = actor.RequireUserId();
            if (_Services.Settings.Ideas.MemberSubmissions != IdeaSubmissionModeEnum.Direct && !IsStaff(actor))
                throw ApiErrorException.Forbidden("Ideas are published by staff in this community.");
            actor.RequireCanPost();
            return await CreateCoreAsync(actor, info, request, userId, true, null, true, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/ideas: staff create an idea (author the caller, or none for the break-glass token).
        /// </summary>
        /// <param name="actor">Ideas staff.</param>
        /// <param name="info">Request information (audit).</param>
        /// <param name="request">Body (adds status and showAuthor).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The idea.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden.</exception>
        public async Task<IdeaView> AdminCreateAsync(ContentActor actor, RequestInfo info, IdeaRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            RequireEnabled();
            RequireStaff(actor);
            return await CreateCoreAsync(actor, info, request, actor.UserId, request.ShowAuthor ?? true, null, false, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Create an idea published from a support ticket (IdeaTicketService): author the requester (shown when
        /// showRequester), the requester votes and follows.
        /// </summary>
        /// <param name="actor">Ideas staff.</param>
        /// <param name="info">Request information (audit).</param>
        /// <param name="request">Title, body, audience.</param>
        /// <param name="requesterUserId">Requester, or null.</param>
        /// <param name="showRequester">Whether the requester is shown as the author.</param>
        /// <param name="ticketId">Source ticket.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The idea.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed.</exception>
        public Task<IdeaView> CreateFromTicketAsync(ContentActor actor, RequestInfo info, IdeaRequest request, string? requesterUserId, bool showRequester, string ticketId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            return CreateCoreAsync(actor, info, request, requesterUserId, showRequester, ticketId, false, token);
        }

        /// <summary>
        /// PUT /api/v1/ideas/{id} (the author while the status is the initial status; staff any time) and PUT
        /// /api/v1/admin/ideas/{id} (staff): title, body, labels, tags; staff also audience and showAuthor.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request information (audit).</param>
        /// <param name="ideaId">Idea identifier.</param>
        /// <param name="request">Body.</param>
        /// <param name="present">Top-level fields present in the body.</param>
        /// <param name="staffRoute">Whether this is the staff route.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The idea.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or ContentFiltered, 403 Forbidden, 404 NotFound, 409 IdeaNotEditable.</exception>
        public async Task<IdeaView> UpdateAsync(ContentActor actor, RequestInfo info, string ideaId, IdeaRequest request, HashSet<string> present, bool staffRoute, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(present);
            RequireEnabled();
            bool staff = IsStaff(actor);
            if (staffRoute) RequireStaff(actor);
            Idea idea = staffRoute ? await RequireLiveAsync(ideaId, token).ConfigureAwait(false) : await RequireReadableAsync(actor, ideaId, token).ConfigureAwait(false);
            if (!staff)
            {
                bool author = actor.UserId != null && String.Equals(idea.AuthorUserId, actor.UserId, StringComparison.Ordinal);
                if (!author) throw ApiErrorException.Forbidden("Only the author or ideas staff can edit this idea.");
                IdeaStatus initial = await _Services.Ideas.Statuses.InitialAsync(token).ConfigureAwait(false);
                if (!String.Equals(idea.StatusKey, initial.Key, StringComparison.OrdinalIgnoreCase) || idea.MergedIntoId != null)
                    throw new ApiErrorException(409, "IdeaNotEditable", "Ideas can be edited by their author only while they are " + initial.Label + ".");
                actor.RequireCanPost();
            }
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string title = present.Contains("title") ? NormalizeTitle(request.Title, errors) : idea.Title;
            string body = present.Contains("body") ? FeedService.NormalizeBody(request.Body, MaxBodyLength, errors) : idea.Body;
            ContentAudienceEnum audience = idea.Audience;
            if (present.Contains("audience") && request.Audience != null) audience = ResolveAudience(actor, request.Audience, errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            WordFilterResult filter = await _Services.WordFilter.CheckAsync(actor, TargetTypeEnum.Idea, FeedService.FilterFields(title, body, new List<MediaAttachment>()), token).ConfigureAwait(false);
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, actor.IsStaffFor(TargetTypeEnum.Idea), token, TargetTypeEnum.Idea).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            bool changed = !String.Equals(title, idea.Title, StringComparison.Ordinal) || !String.Equals(body, idea.Body, StringComparison.Ordinal);
            idea.Title = title;
            idea.Body = body;
            idea.Audience = audience;
            if (staff && present.Contains("showAuthor") && request.ShowAuthor != null) idea.ShowAuthor = request.ShowAuthor.Value;
            if (changed) idea.EditedUtc = now;
            idea.UpdatedUtc = now;
            Idea stored = await _Services.Database.Ideas.UpdateAsync(idea, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Idea");
            await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.Idea, stored.Id, present.Contains("labels") ? request.Labels : null, present.Contains("tags") ? request.Tags : null, token).ConfigureAwait(false);
            await _Services.WordFilter.RecordFlagsAsync(filter, TargetTypeEnum.Idea, stored.Id, stored.AuthorUserId, ModerationService.IdeasScopeType, IdeaModerationTarget.IdeaScopeId, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "idea.update", TargetTypeEnum.Idea.ToString(), stored.Id, null, token).ConfigureAwait(false);
            await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Idea, stored.Id, token).ConfigureAwait(false);
            IdeaTelemetry.Count("update");
            return await ViewAsync(stored, actor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// PUT and DELETE /api/v1/ideas/{id}/vote: idempotent; voting follows the idea.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="ideaId">Idea identifier.</param>
        /// <param name="vote">True to vote, false to remove the vote.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The idea.</returns>
        /// <exception cref="ApiErrorException">401, 402, 403 Muted, 404, 409 IdeaMerged or IdeaClosed, 429 RateLimited.</exception>
        public async Task<IdeaView> VoteAsync(ContentActor actor, string ideaId, bool vote, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            Idea idea = await RequireReadableAsync(actor, ideaId, token).ConfigureAwait(false);
            string userId = actor.RequireUserId();
            RequireParticipation(idea, actor);
            await RequireNotMergedAsync(idea, actor, token).ConfigureAwait(false);
            if (vote && !_Services.Settings.Ideas.VoteOnClosed)
            {
                IdeaStatus? status = IdeaStatusService.Find(await _Services.Ideas.Statuses.AllAsync(token).ConfigureAwait(false), idea.StatusKey);
                if (status != null && status.IsClosed) throw new ApiErrorException(409, "IdeaClosed", "Voting is closed for ideas that are " + status.Label + ".");
            }
            _Services.Limits.EnforceMember(actor, VoteBucket, _Services.Settings.Ideas.VotesPerMinute, RateLimitService.Minute);
            DateTime now = DateTime.UtcNow;
            bool changed = vote
                ? await _Services.Database.Ideas.VoteAsync(idea.Id, userId, now, token).ConfigureAwait(false)
                : await _Services.Database.Ideas.UnvoteAsync(idea.Id, userId, now, token).ConfigureAwait(false);
            if (changed) IdeaTelemetry.Count(vote ? "vote" : "unvote");
            Idea stored = await _Services.Database.Ideas.ReadAsync(idea.Id, token).ConfigureAwait(false) ?? idea;
            return await ViewAsync(stored, actor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// PUT and DELETE /api/v1/ideas/{id}/follow: idempotent.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="ideaId">Idea identifier.</param>
        /// <param name="follow">True to follow, false to unfollow.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>{ isFollowed }.</returns>
        /// <exception cref="ApiErrorException">401, 402, 404.</exception>
        public async Task<IdeaFollowResult> FollowAsync(ContentActor actor, string ideaId, bool follow, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            Idea idea = await RequireReadableAsync(actor, ideaId, token).ConfigureAwait(false);
            string userId = actor.RequireUserId();
            if (actor.IsAnonymous) throw new ApiErrorException(401, "Unauthorized", "Sign in to follow ideas.");
            if (follow)
            {
                if (!ModelAllowsParticipation(idea, actor)) throw Denied(402, "MembershipRequired", "Following is for members.", "Membership", idea.Id);
                await _Services.Database.Ideas.FollowAsync(idea.Id, userId, IdeaFollowSourceEnum.Manual, DateTime.UtcNow, token).ConfigureAwait(false);
            }
            else
            {
                await _Services.Database.Ideas.UnfollowAsync(idea.Id, userId, token).ConfigureAwait(false);
            }
            IdeaTelemetry.Count(follow ? "follow" : "unfollow");
            return new IdeaFollowResult { IsFollowed = follow };
        }

        /// <summary>
        /// PUT /api/v1/admin/ideas/{id}/status: set the status and note; notifies voters and followers (IdeaStatusChanged)
        /// unless notifyVoters is false, and emails voters idea_shipped when the status is the shipped status. Audited
        /// idea.status.
        /// </summary>
        /// <param name="actor">Ideas staff.</param>
        /// <param name="info">Request information (audit).</param>
        /// <param name="ideaId">Idea identifier.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The idea.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound.</exception>
        public async Task<IdeaView> SetStatusAsync(ContentActor actor, RequestInfo info, string ideaId, IdeaStatusChangeRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            RequireEnabled();
            RequireStaff(actor);
            Idea idea = await RequireLiveAsync(ideaId, token).ConfigureAwait(false);
            List<IdeaStatus> statuses = await _Services.Ideas.Statuses.AllAsync(token).ConfigureAwait(false);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            IdeaStatus? status = IdeaStatusService.Find(statuses, request.Status);
            if (status == null || status.IsRetired) errors["status"] = "Use one of " + String.Join(", ", statuses.Where(s => !s.IsRetired).Select(s => s.Key)) + ".";
            string? note = String.IsNullOrWhiteSpace(request.StatusNote) ? null : request.StatusNote.Trim();
            if (note != null && note.Length > MaxStatusNoteLength) errors["statusNote"] = "At most " + MaxStatusNoteLength + " characters.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            DateTime now = DateTime.UtcNow;
            Idea stored = await _Services.Database.Ideas.SetStatusAsync(idea.Id, status!.Key, note, now, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Idea");
            await _Services.Audit.WriteAsync(info, "idea.status", TargetTypeEnum.Idea.ToString(), stored.Id, idea.StatusKey + " -> " + status.Key, token).ConfigureAwait(false);
            IdeaTelemetry.Count("status");
            if (request.NotifyVoters ?? true) await _Services.Ideas.Notifications.StatusChangedAsync(stored, now, token).ConfigureAwait(false);
            return await ViewAsync(stored, actor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/ideas/{id}/merge: votes move to the target (de-duplicated), followers follow the target,
        /// comments stay on the merged idea (read-only). Audited idea.merge.
        /// </summary>
        /// <param name="actor">Ideas staff.</param>
        /// <param name="info">Request information (audit).</param>
        /// <param name="ideaId">Idea to merge.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The merged idea.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound, 409 IdeaMerged or AlreadyMerged.</exception>
        public async Task<IdeaView> MergeAsync(ContentActor actor, RequestInfo info, string ideaId, IdeaMergeRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            RequireEnabled();
            RequireStaff(actor);
            Idea idea = await RequireLiveAsync(ideaId, token).ConfigureAwait(false);
            string targetId = (request.IntoIdeaId ?? String.Empty).Trim();
            if (targetId.Length == 0) throw ApiErrorException.Field("intoIdeaId", "Required.");
            if (String.Equals(targetId, idea.Id, StringComparison.Ordinal)) throw ApiErrorException.Field("intoIdeaId", "An idea cannot be merged into itself.");
            Idea target = await RequireLiveAsync(targetId, token).ConfigureAwait(false);
            if (idea.MergedIntoId != null) throw new ApiErrorException(409, "AlreadyMerged", "This idea is already merged; unmerge it first.");
            if (target.MergedIntoId != null) await RequireNotMergedAsync(target, actor, token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            if (!await _Services.Database.Ideas.MergeAsync(idea.Id, target.Id, now, token).ConfigureAwait(false))
                throw new ApiErrorException(409, "AlreadyMerged", "This idea is already merged; unmerge it first.");
            await _Services.Audit.WriteAsync(info, "idea.merge", TargetTypeEnum.Idea.ToString(), idea.Id, "into " + target.Id, token).ConfigureAwait(false);
            await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Idea, idea.Id, token).ConfigureAwait(false);
            IdeaTelemetry.Count("merge");
            Idea stored = await _Services.Database.Ideas.ReadAsync(idea.Id, token).ConfigureAwait(false) ?? idea;
            return await ViewAsync(stored, actor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/ideas/{id}/unmerge: the idea is listed again; votes copied to the target stay there.
        /// Audited idea.merge (summary unmerge).
        /// </summary>
        /// <param name="actor">Ideas staff.</param>
        /// <param name="info">Request information (audit).</param>
        /// <param name="ideaId">Merged idea.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The idea.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound, 409 NotMerged.</exception>
        public async Task<IdeaView> UnmergeAsync(ContentActor actor, RequestInfo info, string ideaId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            RequireEnabled();
            RequireStaff(actor);
            Idea idea = await RequireLiveAsync(ideaId, token).ConfigureAwait(false);
            if (!await _Services.Database.Ideas.UnmergeAsync(idea.Id, DateTime.UtcNow, token).ConfigureAwait(false))
                throw new ApiErrorException(409, "NotMerged", "This idea is not merged.");
            await _Services.Audit.WriteAsync(info, "idea.merge", TargetTypeEnum.Idea.ToString(), idea.Id, "unmerge from " + idea.MergedIntoId, token).ConfigureAwait(false);
            await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Idea, idea.Id, token).ConfigureAwait(false);
            IdeaTelemetry.Count("unmerge");
            Idea stored = await _Services.Database.Ideas.ReadAsync(idea.Id, token).ConfigureAwait(false) ?? idea;
            return await ViewAsync(stored, actor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// DELETE /api/v1/admin/ideas/{id}: soft delete. Audited idea.delete.
        /// </summary>
        /// <param name="actor">Ideas staff.</param>
        /// <param name="info">Request information (audit).</param>
        /// <param name="ideaId">Idea identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task DeleteAsync(ContentActor actor, RequestInfo info, string ideaId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            RequireEnabled();
            RequireStaff(actor);
            Idea idea = await RequireLiveAsync(ideaId, token).ConfigureAwait(false);
            await _Services.Database.Ideas.SoftDeleteAsync(idea.Id, DateTime.UtcNow, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "idea.delete", TargetTypeEnum.Idea.ToString(), idea.Id, null, token).ConfigureAwait(false);
            await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Idea, idea.Id, token).ConfigureAwait(false);
            IdeaTelemetry.Count("delete");
        }

        /// <summary>
        /// POST /api/v1/public/ideas/enumerate: Public, unmoderated, unmerged ideas when ideas.publicBoardEnabled (anonymous;
        /// no viewer votes, no comments).
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="sourceIp">Caller address (rate limit).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotAvailable, 429 RateLimited.</exception>
        public async Task<EnumerationResult<IdeaView>> PublicListAsync(EnumerationQuery? query, string? sourceIp, CancellationToken token = default)
        {
            RequirePublic(sourceIp);
            EnumerationQuery q = query ?? new EnumerationQuery();
            List<IdeaStatus> statuses = await _Services.Ideas.Statuses.AllAsync(token).ConfigureAwait(false);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            ContentActor anonymous = Anonymous(sourceIp);
            IdeaFilter filter = BuildFilter(anonymous, q, statuses, false, errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            filter.Audiences = new List<ContentAudienceEnum> { ContentAudienceEnum.Public };
            EnumerationRequest request = _Services.Enumeration.Prepare(q, IdeaCollections.PublicIdeas, false, "public-ideas", MaxPublicResults);
            filter.RelevanceText = request.Query.Search;
            EnumerationResult<Idea> page = await _Services.Database.Ideas.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            return page.With(await _Services.Ideas.Views.BuildAsync(page.Objects, anonymous, statuses, true, token).ConfigureAwait(false));
        }

        /// <summary>
        /// GET /api/v1/public/ideas/{id}: one Public idea (anonymous).
        /// </summary>
        /// <param name="ideaId">Idea identifier.</param>
        /// <param name="sourceIp">Caller address (rate limit).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Idea.</returns>
        /// <exception cref="ApiErrorException">404 NotFound or NotAvailable, 429 RateLimited.</exception>
        public async Task<IdeaView> PublicGetAsync(string ideaId, string? sourceIp, CancellationToken token = default)
        {
            RequirePublic(sourceIp);
            Idea? idea = String.IsNullOrWhiteSpace(ideaId) ? null : await _Services.Database.Ideas.ReadAsync(ideaId.Trim(), token).ConfigureAwait(false);
            if (idea == null || idea.DeletedUtc != null || idea.ModerationState != ModerationStateEnum.None || idea.Audience != ContentAudienceEnum.Public)
                throw ApiErrorException.NotFound("Idea");
            List<IdeaStatus> statuses = await _Services.Ideas.Statuses.AllAsync(token).ConfigureAwait(false);
            return (await _Services.Ideas.Views.BuildAsync(new List<Idea> { idea }, Anonymous(sourceIp), statuses, false, token).ConfigureAwait(false))[0];
        }

        /// <summary>
        /// The Idea payload for one idea.
        /// </summary>
        /// <param name="idea">Idea.</param>
        /// <param name="actor">Caller.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Payload.</returns>
        public async Task<IdeaView> ViewAsync(Idea idea, ContentActor actor, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(idea);
            ArgumentNullException.ThrowIfNull(actor);
            List<IdeaStatus> statuses = await _Services.Ideas.Statuses.AllAsync(token).ConfigureAwait(false);
            return (await _Services.Ideas.Views.BuildAsync(new List<Idea> { idea }, actor, statuses, false, token).ConfigureAwait(false))[0];
        }

        /// <summary>
        /// Load a live (not deleted) idea in any state, or fail 404.
        /// </summary>
        /// <param name="ideaId">Idea identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Idea.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<Idea> RequireLiveAsync(string ideaId, CancellationToken token = default)
        {
            Idea? idea = String.IsNullOrWhiteSpace(ideaId) ? null : await _Services.Database.Ideas.ReadAsync(ideaId.Trim(), token).ConfigureAwait(false);
            if (idea == null || idea.DeletedUtc != null) throw ApiErrorException.NotFound("Idea");
            return idea;
        }

        /// <summary>
        /// Fail with 403 Forbidden unless the caller is ideas staff.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <exception cref="ApiErrorException">403 Forbidden.</exception>
        public static void RequireStaff(ContentActor actor)
        {
            if (!IsStaff(actor)) throw ApiErrorException.Forbidden("Ideas staff (SystemAdmin or SupportAgent) only.");
        }

        #endregion

        #region Private-Methods

        private async Task<IdeaView> CreateCoreAsync(ContentActor actor, RequestInfo info, IdeaRequest request, string? authorUserId, bool showAuthor, string? ticketId, bool memberRoute, CancellationToken token)
        {
            List<IdeaStatus> statuses = await _Services.Ideas.Statuses.AllAsync(token).ConfigureAwait(false);
            IdeaStatus initial = await _Services.Ideas.Statuses.InitialAsync(token).ConfigureAwait(false);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string title = NormalizeTitle(request.Title, errors);
            string body = FeedService.NormalizeBody(request.Body, MaxBodyLength, errors);
            ContentAudienceEnum audience = ResolveAudience(actor, request.Audience, errors);
            IdeaStatus status = initial;
            if (!memberRoute && !String.IsNullOrWhiteSpace(request.Status))
            {
                IdeaStatus? chosen = IdeaStatusService.Find(statuses, request.Status);
                if (chosen == null || chosen.IsRetired) errors["status"] = "Use one of " + String.Join(", ", statuses.Where(s => !s.IsRetired).Select(s => s.Key)) + ".";
                else status = chosen;
            }
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            WordFilterResult filter = await _Services.WordFilter.CheckAsync(actor, TargetTypeEnum.Idea, FeedService.FilterFields(title, body, new List<MediaAttachment>()), token).ConfigureAwait(false);
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, actor.IsStaffFor(TargetTypeEnum.Idea), token, TargetTypeEnum.Idea).ConfigureAwait(false);
            if (memberRoute) _Services.Limits.EnforceMember(actor, CreateBucket, _Services.Settings.Ideas.IdeasPerDay, RateLimitService.Day);
            DateTime now = DateTime.UtcNow;
            Idea idea = new Idea
            {
                Id = IdGenerator.Generate(IdeaIdPrefixes.Idea),
                Title = title,
                Body = body,
                AuthorUserId = String.IsNullOrEmpty(authorUserId) ? null : authorUserId,
                ShowAuthor = showAuthor,
                StatusKey = status.Key,
                StatusUtc = now,
                SourceTicketId = ticketId,
                Audience = audience,
                CreatedByUserId = memberRoute ? null : actor.UserId,
                CreatedUtc = now,
                UpdatedUtc = now
            };
            string? voter = memberRoute || ticketId != null ? idea.AuthorUserId : null;
            idea = await _Services.Database.Ideas.CreateAsync(idea, voter, ticketId != null ? IdeaFollowSourceEnum.Vote : IdeaFollowSourceEnum.Author, token).ConfigureAwait(false);
            await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.Idea, idea.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            await _Services.WordFilter.RecordFlagsAsync(filter, TargetTypeEnum.Idea, idea.Id, idea.AuthorUserId, ModerationService.IdeasScopeType, IdeaModerationTarget.IdeaScopeId, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "idea.create", TargetTypeEnum.Idea.ToString(), idea.Id, ticketId == null ? null : "from " + ticketId, token).ConfigureAwait(false);
            await _Services.Forums.Search.EnqueueAsync(TargetTypeEnum.Idea, idea.Id, token).ConfigureAwait(false);
            IdeaTelemetry.Count("create");
            return await ViewAsync(idea, actor, token).ConfigureAwait(false);
        }

        private IdeaFilter BuildFilter(ContentActor actor, EnumerationQuery q, List<IdeaStatus> statuses, bool admin, Dictionary<string, string> errors)
        {
            bool staff = IsStaff(actor);
            IdeaFilter filter = new IdeaFilter { IsStaff = staff, ViewerUserId = actor.IsAnonymous ? null : actor.UserId };
            filter.Audiences = ContentAudienceRule.ListedAudiences(actor);
            List<string>? keys = ForumFilters.List(q, "status");
            if (keys != null)
            {
                foreach (string key in keys)
                {
                    IdeaStatus? status = IdeaStatusService.Find(statuses, key);
                    if (status == null) errors["filters.status"] = "Unknown status " + key + ".";
                    else filter.StatusKeys.Add(status.Key);
                }
            }
            bool? open = q.FilterBool("open", errors);
            if (open == true) filter.ExcludedStatusKeys.AddRange(statuses.Where(s => s.IsClosed).Select(s => s.Key));
            else if (open == false) filter.StatusKeys.AddRange(statuses.Where(s => s.IsClosed && !filter.StatusKeys.Contains(s.Key)).Select(s => s.Key));
            bool? votedByMe = q.FilterBool("votedByMe", errors);
            if (votedByMe == true)
            {
                if (String.IsNullOrEmpty(filter.ViewerUserId)) errors["filters.votedByMe"] = "Sign in to filter by your votes.";
                else filter.VotedByUserId = filter.ViewerUserId;
            }
            string? author = q.FilterString("authorUserId");
            if (!String.IsNullOrWhiteSpace(author)) filter.AuthorUserId = author.Trim();
            ContentAudienceEnum? audience = q.FilterEnum<ContentAudienceEnum>("audience", errors);
            filter.AudienceFilter = audience;
            filter.IncludeMerged = q.FilterBool("includeMerged", errors) ?? false;
            if (admin)
            {
                filter.Hidden = q.FilterBool("hidden", errors);
                filter.Merged = q.FilterBool("merged", errors);
                if (filter.Merged == true) filter.IncludeMerged = true;
            }
            return filter;
        }

        private ContentAudienceEnum ResolveAudience(ContentActor actor, ContentAudienceEnum? requested, Dictionary<string, string> errors)
        {
            AccessPolicy policy = _Services.Access.Current;
            ContentAudienceEnum audience = requested ?? policy.DefaultAudienceFor(FeatureKeyEnum.Ideas);
            if (audience == ContentAudienceEnum.Public && !IsStaff(actor))
            {
                errors["audience"] = "Only ideas staff can publish a Public idea.";
                return policy.DefaultAudienceFor(FeatureKeyEnum.Ideas);
            }
            if (requested == ContentAudienceEnum.Members && policy.Model == AccessModelEnum.Open)
            {
                errors["audience"] = "Members is not available: this property has no memberships. Use SignedIn.";
                return ContentAudienceEnum.SignedIn;
            }
            return audience;
        }

        private static string NormalizeTitle(string? raw, Dictionary<string, string> errors)
        {
            string title = MarkdownNormalizer.NormalizeSingleLine(raw ?? String.Empty);
            int length = MarkdownNormalizer.Length(title);
            if (length < MinTitleLength || length > MaxTitleLength) errors["title"] = "Use " + MinTitleLength + " to " + MaxTitleLength + " characters.";
            return title;
        }

        private void RequirePublic(string? sourceIp)
        {
            RequireEnabled();
            if (!_Services.Settings.Ideas.PublicBoardEnabled) throw new ApiErrorException(404, "NotAvailable", "The public ideas board is not available in this community.");
            _Services.Limits.EnforceIp(sourceIp, "public.ideas", _Services.Settings.RateLimits.PublicFeedPerMinute, RateLimitService.Minute);
        }

        private ContentActor Anonymous(string? sourceIp)
        {
            return new ContentActor { IsAnonymous = true, SourceIp = sourceIp, Access = _Services.Access.Current };
        }

        private static ApiErrorException Denied(int status, string error, string message, string required, string ideaId)
        {
            AccessDeniedResponse body = new AccessDeniedResponse
            {
                Error = error,
                Message = message,
                Required = required,
                Feature = AccessPolicy.FeatureKey(FeatureKeyEnum.Ideas),
                TargetType = TargetTypeEnum.Idea.ToString(),
                TargetId = ideaId
            };
            return new ApiErrorException(status, error, message, null, body);
        }

        #endregion
    }
}
