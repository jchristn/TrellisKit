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
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Member event proposals (resolved decision 10): members propose single events through a review queue
    /// (events.proposalsEnabled and property.json events.proposals, at most events.maxPendingProposalsPerMember pending,
    /// events.proposalsPerDay per day, word filtered); event staff approve (the proposer becomes the host; published
    /// by default) or reject with a note; the proposer is told either way (EventProposalDecision and the
    /// event_proposal_decision email) and event staff get staff_event_proposal. Thread safety: stateless.
    /// </summary>
    public class EventProposalService
    {
        #region Private-Members

        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public EventProposalService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether proposals are on (setting and property.json).
        /// </summary>
        public bool Enabled
        {
            get { return _Services.Settings.Events.ProposalsEnabled && (_Services.Property.Property.EventsSection ?? new PropertyEventsSection()).Proposals; }
        }

        /// <summary>
        /// POST /api/v1/events/proposals.
        /// </summary>
        /// <param name="actor">Member.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="request">Event fields (single event; hosts, Public audience, and recurrence are refused).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The proposal.</returns>
        /// <exception cref="ApiErrorException">400, 403 ProposalsDisabled, PostingNotAllowed, or Muted, 409 ProposalLimitReached, 429.</exception>
        public async Task<EventProposalView> ProposeAsync(ContentActor actor, RequestInfo info, EventRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            string userId = actor.RequireUserId();
            if (!Enabled) throw new ApiErrorException(403, "ProposalsDisabled", "Event proposals are turned off for this property.");
            actor.RequireCanPost();
            long pending = await _Services.Database.Events.Records.CountPendingProposalsAsync(userId, token).ConfigureAwait(false);
            if (pending >= _Services.Settings.Events.MaxPendingProposalsPerMember && !actor.IsSystemAdmin)
                throw new ApiErrorException(409, "ProposalLimitReached", "You have " + pending + " proposals waiting for review.");
            _Services.Limits.CheckMember(actor, "event-proposals-day", _Services.Settings.Events.ProposalsPerDay, RateLimitService.Day);
            if (request.Recurrence != null) throw ApiErrorException.Field("recurrence", "Proposals are single events.");
            EventRecord e = await _Services.Events.Catalog.CreateCoreAsync(actor, request, true, token).ConfigureAwait(false);
            _Services.Limits.EnforceMember(actor, "event-proposals-day", _Services.Settings.Events.ProposalsPerDay, RateLimitService.Day);
            await _Services.Audit.WriteAsync(info, "event.propose", "Event", e.Id, e.Title, token).ConfigureAwait(false);
            EventTelemetry.Count(EventTelemetry.Events, 1, "action", "propose");
            UserSummary me = await _Services.Summaries.BuildOneAsync(userId, token).ConfigureAwait(false);
            await _Services.Events.Notifier.StaffProposalAsync(e, me.DisplayName, token, actor.Request ?? info).ConfigureAwait(false);
            return (await ViewsAsync(actor, new List<EventRecord> { e }, token).ConfigureAwait(false))[0];
        }

        /// <summary>
        /// POST /api/v1/me/events/proposals/enumerate: the caller's proposals (filter status).
        /// </summary>
        /// <param name="actor">Member.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        public async Task<EnumerationResult<EventProposalView>> MineAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            string userId = actor.RequireUserId();
            EnumerationQuery q = query ?? new EnumerationQuery();
            EventListFilter filter = BaseFilter(q);
            filter.ProposedByUserId = userId;
            EnumerationRequest request = _Services.Enumeration.Prepare(q, EventCollections.MyProposals, false);
            EnumerationResult<EventRecord> page = await _Services.Database.Events.Records.EnumerateAsync(filter, request, token).ConfigureAwait(false);
            return page.With(await ViewsAsync(actor, page.Objects, token).ConfigureAwait(false));
        }

        /// <summary>
        /// POST /api/v1/admin/events/proposals/enumerate: the review queue (oldest first; filter status, default
        /// Proposed; proposedByUserId).
        /// </summary>
        /// <param name="actor">Event staff.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of AdminEvent.</returns>
        /// <exception cref="ApiErrorException">403.</exception>
        public async Task<EnumerationResult<AdminEventView>> QueueAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            EventService.RequireStaff(actor);
            EnumerationQuery q = query ?? new EnumerationQuery();
            EventListFilter filter = BaseFilter(q);
            if (!q.HasFilter("status")) filter.Statuses = new List<EventStatusEnum> { EventStatusEnum.Proposed };
            filter.ProposedByUserId = q.FilterString("proposedByUserId");
            filter.OnlyProposals = true;
            EnumerationRequest request = _Services.Enumeration.Prepare(q, EventCollections.AdminProposals, actor.IsStaffFor(TargetTypeEnum.Event));
            EnumerationResult<EventRecord> page = await _Services.Database.Events.Records.EnumerateAsync(filter, request, token).ConfigureAwait(false);
            return page.With(await _Services.Events.Views.AdminPageAsync(actor, page.Objects, token).ConfigureAwait(false));
        }

        /// <summary>
        /// POST /api/v1/events/proposals/{id}/withdraw (the proposer, while Proposed).
        /// </summary>
        /// <param name="actor">Member.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="id">Proposal.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The proposal.</returns>
        /// <exception cref="ApiErrorException">404, 409 NotPending.</exception>
        public async Task<EventProposalView> WithdrawAsync(ContentActor actor, RequestInfo info, string id, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            string userId = actor.RequireUserId();
            EventRecord? e = await _Services.Database.Events.Records.ReadAsync(id, token).ConfigureAwait(false);
            if (e == null || !String.Equals(e.ProposedByUserId, userId, StringComparison.Ordinal)) throw ApiErrorException.NotFound("Proposal");
            if (e.Status != EventStatusEnum.Proposed) throw new ApiErrorException(409, "NotPending", "This proposal was already reviewed or withdrawn.");
            e.Status = EventStatusEnum.Withdrawn;
            await _Services.Database.Events.Records.UpdateAsync(e, false, DateTime.UtcNow, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "event.withdraw", "Event", e.Id, e.Title, token).ConfigureAwait(false);
            EventTelemetry.Count(EventTelemetry.Events, 1, "action", "withdraw");
            return (await ViewsAsync(actor, new List<EventRecord> { e }, token).ConfigureAwait(false))[0];
        }

        /// <summary>
        /// POST /api/v1/admin/events/{id}/approve: the proposer becomes the host; published unless publish is false.
        /// </summary>
        /// <param name="actor">Event staff.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="id">Proposal.</param>
        /// <param name="request">Body (reviewNote optional, publish default true, notifyMembers default false).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>AdminEvent.</returns>
        /// <exception cref="ApiErrorException">400, 403, 404, 409 NotPending.</exception>
        public async Task<AdminEventView> ApproveAsync(ContentActor actor, RequestInfo info, string id, EventReviewRequest request, CancellationToken token = default)
        {
            EventService.RequireStaff(actor);
            ArgumentNullException.ThrowIfNull(request);
            EventRecord e = await RequirePendingAsync(id, token).ConfigureAwait(false);
            string note = MarkdownNormalizer.NormalizeSingleLine(request.ReviewNote);
            if (note.Length > 1000) throw ApiErrorException.Field("reviewNote", "Use at most 1000 characters.");
            DateTime now = DateTime.UtcNow;
            e.ReviewNote = note.Length == 0 ? null : note;
            e.ReviewerUserId = actor.UserId;
            e.ReviewedUtc = now;
            e.Status = EventStatusEnum.Draft;
            if (!e.HostUserIds.Contains(e.ProposedByUserId!, StringComparer.Ordinal)) e.HostUserIds.Insert(0, e.ProposedByUserId!);
            await _Services.Database.Events.Records.UpdateAsync(e, true, now, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "event.approve", "Event", e.Id, e.Title, token).ConfigureAwait(false);
            EventTelemetry.Count(EventTelemetry.Events, 1, "action", "approve");
            EventRecord after = await _Services.Database.Events.Records.ReadAsync(e.Id, token).ConfigureAwait(false) ?? e;
            if (request.Publish ?? true) after = await _Services.Events.Catalog.PublishCoreAsync(actor, info, after, request.NotifyMembers ?? false, token).ConfigureAwait(false);
            await _Services.Events.Notifier.ProposalDecisionAsync(after, true, token).ConfigureAwait(false);
            return await _Services.Events.Views.AdminAsync(actor, after, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/events/{id}/reject (reviewNote 3 to 1000 required).
        /// </summary>
        /// <param name="actor">Event staff.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="id">Proposal.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>AdminEvent.</returns>
        /// <exception cref="ApiErrorException">400, 403, 404, 409 NotPending.</exception>
        public async Task<AdminEventView> RejectAsync(ContentActor actor, RequestInfo info, string id, EventReviewRequest request, CancellationToken token = default)
        {
            EventService.RequireStaff(actor);
            ArgumentNullException.ThrowIfNull(request);
            EventRecord e = await RequirePendingAsync(id, token).ConfigureAwait(false);
            string note = MarkdownNormalizer.NormalizeSingleLine(request.ReviewNote);
            if (note.Length < 3 || note.Length > 1000) throw ApiErrorException.Field("reviewNote", "Use 3 to 1000 characters.");
            DateTime now = DateTime.UtcNow;
            e.ReviewNote = note;
            e.ReviewerUserId = actor.UserId;
            e.ReviewedUtc = now;
            e.Status = EventStatusEnum.Rejected;
            await _Services.Database.Events.Records.UpdateAsync(e, false, now, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "event.reject", "Event", e.Id, note, token).ConfigureAwait(false);
            EventTelemetry.Count(EventTelemetry.Events, 1, "action", "reject");
            await _Services.Events.Notifier.ProposalDecisionAsync(e, false, token).ConfigureAwait(false);
            return await _Services.Events.Views.AdminAsync(actor, e, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static EventListFilter BaseFilter(EnumerationQuery q)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            List<EventStatusEnum> statuses = ForumFilters.Enums<EventStatusEnum>(q, "status", errors).Where(s => s != EventStatusEnum.Template).ToList();
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            if (statuses.Count == 0) statuses = new List<EventStatusEnum> { EventStatusEnum.Proposed, EventStatusEnum.Draft, EventStatusEnum.Published, EventStatusEnum.Canceled, EventStatusEnum.Rejected, EventStatusEnum.Withdrawn };
            return new EventListFilter { NowUtc = DateTime.UtcNow, When = EventWhenEnum.All, Statuses = statuses, OnlyUnmoderated = false };
        }

        private async Task<EventRecord> RequirePendingAsync(string id, CancellationToken token)
        {
            EventRecord? e = await _Services.Database.Events.Records.ReadAsync(id, token).ConfigureAwait(false);
            if (e == null || e.ProposedByUserId == null) throw ApiErrorException.NotFound("Proposal");
            if (e.Status != EventStatusEnum.Proposed) throw new ApiErrorException(409, "NotPending", "This proposal was already reviewed or withdrawn.");
            return e;
        }

        private async Task<List<EventProposalView>> ViewsAsync(ContentActor actor, List<EventRecord> rows, CancellationToken token)
        {
            List<EventProposalView> views = rows.Select(e => new EventProposalView
            {
                Id = e.Id,
                Title = e.Title,
                Status = e.Status,
                StartUtc = e.StartUtc,
                TimeZone = e.TimeZone,
                ProposalNote = e.ProposalNote,
                ReviewNote = e.ReviewNote,
                ReviewedUtc = e.ReviewedUtc,
                CreatedUtc = e.CreatedUtc
            }).ToList();
            await _Services.Metadata.AttachAsync(TargetTypeEnum.Event, views, v => v.Id, actor.IsStaffFor(TargetTypeEnum.Event), token).ConfigureAwait(false);
            return views;
        }

        #endregion
    }
}
