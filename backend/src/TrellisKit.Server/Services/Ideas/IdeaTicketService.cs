namespace TrellisKit.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Ideas from support tickets (docs/PHASE5_CONTRACT.md sections 6.2 and 7.2): publish an Enhancement ticket as an idea,
    /// or link a ticket to an existing idea. Either way the requester votes for (and follows) the idea and the ticket's
    /// ideaid is set. Ideas staff only. Reads and writes only the tickets columns it needs (id, typekey, title, body,
    /// requesteruserid, ideaid).
    /// </summary>
    public class IdeaTicketService
    {
        #region Public-Members

        /// <summary>
        /// The ticket type key that may be published as an idea.
        /// </summary>
        public const string EnhancementType = "Enhancement";

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
        public IdeaTicketService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// POST /api/v1/admin/tickets/{id}/publish-idea: create an idea from an Enhancement ticket (title and body default
        /// to the ticket's; the requester is shown as the author only when showRequester), link it, and record the
        /// requester's vote.
        /// </summary>
        /// <param name="actor">Ideas staff.</param>
        /// <param name="info">Request information (audit).</param>
        /// <param name="ticketId">Ticket identifier.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The idea.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or NotAnEnhancement, 403 Forbidden, 404 NotFound, 409 TicketHasIdea.</exception>
        public async Task<IdeaView> PublishAsync(ContentActor actor, RequestInfo info, string ticketId, TicketPublishIdeaRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            _Services.Ideas.Ideas.RequireEnabled();
            IdeaService.RequireStaff(actor);
            IdeaTicket ticket = await RequireTicketAsync(ticketId, token).ConfigureAwait(false);
            if (!String.Equals(ticket.TypeKey, EnhancementType, StringComparison.OrdinalIgnoreCase))
                throw new ApiErrorException(400, "NotAnEnhancement", "Only Enhancement tickets can be published as ideas.");
            if (!String.IsNullOrEmpty(ticket.IdeaId)) throw new ApiErrorException(409, "TicketHasIdea", "This ticket is already linked to idea " + ticket.IdeaId + ".");
            IdeaRequest idea = new IdeaRequest
            {
                Title = String.IsNullOrWhiteSpace(request.Title) ? ticket.Title : request.Title,
                Body = request.Body ?? ticket.Body,
                Audience = request.Audience
            };
            IdeaView view = await _Services.Ideas.Ideas.CreateFromTicketAsync(actor, info, idea, ticket.RequesterUserId, request.ShowRequester ?? false, ticket.Id, token).ConfigureAwait(false);
            await _Services.Database.Ideas.LinkTicketAsync(ticket.Id, view.Id, DateTime.UtcNow, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "idea.create", TargetTypeEnum.Ticket.ToString(), ticket.Id, "published as " + view.Id, token).ConfigureAwait(false);
            return view;
        }

        /// <summary>
        /// POST /api/v1/admin/tickets/{id}/link-idea: link a ticket to an existing idea and record the requester's vote
        /// (a merged idea links to the idea it was merged into).
        /// </summary>
        /// <param name="actor">Ideas staff.</param>
        /// <param name="info">Request information (audit).</param>
        /// <param name="ticketId">Ticket identifier.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>{ ticketId, ideaId }.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound.</exception>
        public async Task<TicketIdeaLinkView> LinkAsync(ContentActor actor, RequestInfo info, string ticketId, TicketLinkIdeaRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            _Services.Ideas.Ideas.RequireEnabled();
            IdeaService.RequireStaff(actor);
            string ideaId = (request.IdeaId ?? String.Empty).Trim();
            if (ideaId.Length == 0) throw ApiErrorException.Field("ideaId", "Required.");
            IdeaTicket ticket = await RequireTicketAsync(ticketId, token).ConfigureAwait(false);
            Idea idea = await _Services.Ideas.Ideas.RequireLiveAsync(ideaId, token).ConfigureAwait(false);
            if (!String.IsNullOrEmpty(idea.MergedIntoId)) idea = await _Services.Ideas.Ideas.RequireLiveAsync(idea.MergedIntoId, token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            await _Services.Database.Ideas.LinkTicketAsync(ticket.Id, idea.Id, now, token).ConfigureAwait(false);
            if (!String.IsNullOrEmpty(ticket.RequesterUserId))
            {
                User? requester = await _Services.Database.Users.ReadAsync(ticket.RequesterUserId, token).ConfigureAwait(false);
                if (requester != null && !requester.IsDeleted && await _Services.Database.Ideas.VoteAsync(idea.Id, requester.Id, now, token).ConfigureAwait(false)) IdeaTelemetry.Count("vote");
            }
            await _Services.Audit.WriteAsync(info, "idea.update", TargetTypeEnum.Ticket.ToString(), ticket.Id, "linked to " + idea.Id, token).ConfigureAwait(false);
            return new TicketIdeaLinkView { TicketId = ticket.Id, IdeaId = idea.Id };
        }

        #endregion

        #region Private-Methods

        private async Task<IdeaTicket> RequireTicketAsync(string ticketId, CancellationToken token)
        {
            IdeaTicket? ticket = String.IsNullOrWhiteSpace(ticketId) ? null : await _Services.Database.Ideas.ReadTicketAsync(ticketId.Trim(), token).ConfigureAwait(false);
            return ticket ?? throw ApiErrorException.NotFound("Ticket");
        }

        #endregion
    }
}
