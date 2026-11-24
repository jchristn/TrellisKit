namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Support ticket data methods (docs/PHASE5_CONTRACT.md sections 6 and 16): ticket types and their staff recipients,
    /// tickets with their per-property numbers, and ticket messages. Every statement is bound to the driver's property.
    /// Tickets are never read by global search or the assistant.
    /// </summary>
    public interface ISupportMethods
    {
        #region Ticket-Types

        /// <summary>
        /// Insert ticket types when the property has none yet (concurrent seeders produce one set; existing keys are kept).
        /// </summary>
        /// <param name="types">Types with their recipients.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Types inserted.</returns>
        /// <exception cref="ArgumentNullException">The types are null.</exception>
        Task<int> SeedTypesAsync(List<TicketType> types, CancellationToken token = default);

        /// <summary>
        /// Number of ticket types (retired included).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountTypesAsync(CancellationToken token = default);

        /// <summary>
        /// Every ticket type in sort order with recipients, bounded by limit.
        /// </summary>
        /// <param name="includeRetired">Include retired types.</param>
        /// <param name="limit">Most rows (1 to 1000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Types.</returns>
        Task<List<TicketType>> ListTypesAsync(bool includeRetired, int limit, CancellationToken token = default);

        /// <summary>
        /// Read a ticket type by identifier.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Type, or null.</returns>
        Task<TicketType?> ReadTypeAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read a ticket type by key (case-insensitive).
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Type, or null.</returns>
        Task<TicketType?> ReadTypeByKeyAsync(string key, CancellationToken token = default);

        /// <summary>
        /// Insert a ticket type with its recipients.
        /// </summary>
        /// <param name="type">Type.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The type.</returns>
        /// <exception cref="ArgumentNullException">The type is null.</exception>
        /// <exception cref="DuplicateRecordException">The key is in use.</exception>
        Task<TicketType> CreateTypeAsync(TicketType type, CancellationToken token = default);

        /// <summary>
        /// Update a ticket type and replace its recipients.
        /// </summary>
        /// <param name="type">Type.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a row changed.</returns>
        /// <exception cref="ArgumentNullException">The type is null.</exception>
        /// <exception cref="DuplicateRecordException">The key is in use.</exception>
        Task<bool> UpdateTypeAsync(TicketType type, CancellationToken token = default);

        /// <summary>
        /// Set sort orders 10, 20, 30, ... in the given order.
        /// </summary>
        /// <param name="ids">Type identifiers in order.</param>
        /// <param name="nowUtc">Update time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Types updated.</returns>
        /// <exception cref="ArgumentNullException">The identifiers are null.</exception>
        Task<int> OrderTypesAsync(List<string> ids, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Enumerate ticket types (collection SupportCollections.TicketTypes or AdminTicketTypes) with recipients.
        /// </summary>
        /// <param name="request">Normalized request.</param>
        /// <param name="includeRetired">Include retired types.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The request is null.</exception>
        Task<EnumerationResult<TicketType>> EnumerateTypesAsync(EnumerationRequest request, bool includeRetired, CancellationToken token = default);

        #endregion

        #region Tickets

        /// <summary>
        /// Insert a ticket and its first message in one transaction, assigning the next per-property number with one
        /// counter UPDATE (safe under concurrency). Sets ticket.Number.
        /// </summary>
        /// <param name="ticket">Ticket (Number is assigned).</param>
        /// <param name="first">First message.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The ticket.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<Ticket> CreateTicketAsync(Ticket ticket, TicketMessage first, CancellationToken token = default);

        /// <summary>
        /// Read a ticket.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Ticket, or null.</returns>
        Task<Ticket?> ReadTicketAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Tickets of a requester that are not Closed.
        /// </summary>
        /// <param name="requesterUserId">Requester.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountOpenForRequesterAsync(string requesterUserId, CancellationToken token = default);

        /// <summary>
        /// Enumerate tickets (member collection SupportCollections.MemberTickets or staff SupportCollections.Inbox; the
        /// inbox joins the requester as ru for search).
        /// </summary>
        /// <param name="request">Normalized request.</param>
        /// <param name="filter">Filters (the member list sets RequesterUserId).</param>
        /// <param name="inbox">Whether this is the staff inbox.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<EnumerationResult<Ticket>> EnumerateTicketsAsync(EnumerationRequest request, TicketFilter filter, bool inbox, CancellationToken token = default);

        /// <summary>
        /// Write a ticket's mutable fields and append timeline lines in one transaction, optionally only when the ticket
        /// still has an expected status.
        /// </summary>
        /// <param name="ticket">Ticket after the change.</param>
        /// <param name="messages">Messages to insert (replies, notes, or system lines), or empty.</param>
        /// <param name="expectedStatus">Required current status, or null for any.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the ticket row changed.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<bool> UpdateTicketAsync(Ticket ticket, List<TicketMessage> messages, TicketStatusEnum? expectedStatus, CancellationToken token = default);

        /// <summary>
        /// Delete a Closed ticket with its messages and the labels and tags of both (attachments are purged by the caller).
        /// </summary>
        /// <param name="id">Ticket identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when deleted.</returns>
        Task<bool> DeleteTicketAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Tickets of a requester in identifier order (account export), bounded by limit.
        /// </summary>
        /// <param name="requesterUserId">Requester.</param>
        /// <param name="afterId">Keyset position, or null.</param>
        /// <param name="limit">Most rows.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Tickets.</returns>
        Task<List<Ticket>> ListByRequesterAsync(string requesterUserId, string? afterId, int limit, CancellationToken token = default);

        /// <summary>
        /// Mark tickets overdue once: first response past due, unanswered, not Closed, not yet notified. Returns the
        /// tickets this call marked (each ticket is returned by exactly one caller).
        /// </summary>
        /// <param name="nowUtc">Evaluation time.</param>
        /// <param name="limit">Most rows.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Tickets marked.</returns>
        Task<List<Ticket>> ClaimOverdueAsync(DateTime nowUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// Resolved tickets resolved before the cutoff with no member reply since, oldest first.
        /// </summary>
        /// <param name="cutoffUtc">Resolved before this time.</param>
        /// <param name="limit">Most rows.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Tickets.</returns>
        Task<List<Ticket>> ListAutoCloseAsync(DateTime cutoffUtc, int limit, CancellationToken token = default);

        #endregion

        #region Messages

        /// <summary>
        /// Enumerate a ticket's messages (collection SupportCollections.Messages). Members see replies only.
        /// </summary>
        /// <param name="request">Normalized request.</param>
        /// <param name="ticketId">Ticket.</param>
        /// <param name="staff">Include notes and system lines.</param>
        /// <param name="kind">Only this kind (staff filter), or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<EnumerationResult<TicketMessage>> EnumerateMessagesAsync(EnumerationRequest request, string ticketId, bool staff, TicketMessageKindEnum? kind, CancellationToken token = default);

        /// <summary>
        /// The newest messages of a ticket in ascending order (payload preview).
        /// </summary>
        /// <param name="ticketId">Ticket.</param>
        /// <param name="staff">Include notes and system lines.</param>
        /// <param name="limit">Most rows.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Messages, oldest first.</returns>
        Task<List<TicketMessage>> LatestMessagesAsync(string ticketId, bool staff, int limit, CancellationToken token = default);

        /// <summary>
        /// Number of messages of a ticket.
        /// </summary>
        /// <param name="ticketId">Ticket.</param>
        /// <param name="staff">Include notes and system lines.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountMessagesAsync(string ticketId, bool staff, CancellationToken token = default);

        /// <summary>
        /// Replies of several tickets (account export: never notes or system lines), oldest first, bounded by limit.
        /// </summary>
        /// <param name="ticketIds">Tickets.</param>
        /// <param name="limit">Most rows.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Replies.</returns>
        Task<List<TicketMessage>> RepliesForTicketsAsync(IEnumerable<string> ticketIds, int limit, CancellationToken token = default);

        /// <summary>
        /// Identifiers of every message of a ticket (attachment purge), bounded by limit.
        /// </summary>
        /// <param name="ticketId">Ticket.</param>
        /// <param name="limit">Most rows.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Identifiers.</returns>
        Task<List<string>> MessageIdsAsync(string ticketId, int limit, CancellationToken token = default);

        #endregion

        #region Metrics

        /// <summary>
        /// Tickets created per UTC day (yyyy-MM-dd keys) since a time.
        /// </summary>
        /// <param name="sinceUtc">Window start.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Counts per day with tickets.</returns>
        Task<List<TicketCount>> OpenedByDayAsync(DateTime sinceUtc, CancellationToken token = default);

        /// <summary>
        /// Tickets resolved per UTC day since a time.
        /// </summary>
        /// <param name="sinceUtc">Window start.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Counts per day with resolutions.</returns>
        Task<List<TicketCount>> ResolvedByDayAsync(DateTime sinceUtc, CancellationToken token = default);

        /// <summary>
        /// Median hours from creation to first staff reply of tickets created since a time.
        /// </summary>
        /// <param name="sinceUtc">Window start.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Hours, or null when no ticket was answered.</returns>
        Task<double?> FirstResponseMedianHoursAsync(DateTime sinceUtc, CancellationToken token = default);

        /// <summary>
        /// Tickets created per type since a time, most first, bounded by limit.
        /// </summary>
        /// <param name="sinceUtc">Window start.</param>
        /// <param name="limit">Most rows.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Counts keyed by type key.</returns>
        Task<List<TicketCount>> ByTypeAsync(DateTime sinceUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// Tickets that are not Closed.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountOpenAsync(CancellationToken token = default);

        /// <summary>
        /// Tickets overdue now (first response past due, unanswered, not Closed).
        /// </summary>
        /// <param name="nowUtc">Evaluation time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountOverdueAsync(DateTime nowUtc, CancellationToken token = default);

        #endregion
    }
}
