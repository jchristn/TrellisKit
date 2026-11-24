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
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Conversation data access (aivisitors, aiconversations, aimessages, aimessagecitations, aimessagestages,
    /// aifeedback, aidailyusage). Implementations are bound to one property and safe to call concurrently.
    /// </summary>
    public interface IAiConversationMethods
    {
        /// <summary>
        /// Create a visitor.
        /// </summary>
        /// <param name="visitor">Visitor.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task CreateVisitorAsync(AssistantVisitor visitor, CancellationToken token = default);

        /// <summary>
        /// Read a visitor.
        /// </summary>
        /// <param name="id">Visitor id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Visitor, or null.</returns>
        Task<AssistantVisitor?> ReadVisitorAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Record that a visitor was seen.
        /// </summary>
        /// <param name="id">Visitor id.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task TouchVisitorAsync(string id, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Create a conversation.
        /// </summary>
        /// <param name="conversation">Conversation.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The conversation.</returns>
        /// <exception cref="ArgumentNullException">The conversation is null.</exception>
        Task<AssistantConversation> CreateConversationAsync(AssistantConversation conversation, CancellationToken token = default);

        /// <summary>
        /// Read a conversation.
        /// </summary>
        /// <param name="id">Conversation id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Conversation, or null.</returns>
        Task<AssistantConversation?> ReadConversationAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Update the title, title source, status, and archive state.
        /// </summary>
        /// <param name="conversation">Conversation.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task UpdateConversationAsync(AssistantConversation conversation, CancellationToken token = default);

        /// <summary>
        /// Set the title unless the owner renamed the conversation.
        /// </summary>
        /// <param name="id">Conversation id.</param>
        /// <param name="title">Title.</param>
        /// <param name="source">Generated or Fallback.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when set.</returns>
        Task<bool> SetGeneratedTitleAsync(string id, string title, AssistantTitleSourceEnum source, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Hard-delete a conversation with its messages, citations, and stages; feedback rows keep their copies.
        /// </summary>
        /// <param name="id">Conversation id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when deleted.</returns>
        Task<bool> DeleteConversationAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate conversations (AiCollections.Conversations or AiCollections.AdminConversations).
        /// </summary>
        /// <param name="request">Prepared request.</param>
        /// <param name="filter">Filters (the service sets the owner for member lists).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<AssistantConversation>> EnumerateConversationsAsync(EnumerationRequest request, AiEnumerationFilter filter, CancellationToken token = default);

        /// <summary>
        /// Store a member message and its pending reply, and update the conversation counters, in one transaction.
        /// </summary>
        /// <param name="userMessage">Member message.</param>
        /// <param name="reply">Pending reply.</param>
        /// <param name="maxMessages">Conversation cap (status becomes Full when reached).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="DuplicateRecordException">The client message id was used.</exception>
        Task AddExchangeAsync(AssistantMessage userMessage, AssistantMessage reply, int maxMessages, CancellationToken token = default);

        /// <summary>
        /// Supersede the previous reply and store a new pending reply to the same member message.
        /// </summary>
        /// <param name="supersededId">Reply being replaced.</param>
        /// <param name="reply">New pending reply.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task AddRegeneratedReplyAsync(string supersededId, AssistantMessage reply, CancellationToken token = default);

        /// <summary>
        /// Read a message with its citations and stages.
        /// </summary>
        /// <param name="id">Message id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Message, or null.</returns>
        Task<AssistantMessage?> ReadMessageAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read the member message with a client message id in a conversation.
        /// </summary>
        /// <param name="conversationId">Conversation id.</param>
        /// <param name="clientMessageId">Client message id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Message, or null.</returns>
        Task<AssistantMessage?> ReadByClientMessageIdAsync(string conversationId, string clientMessageId, CancellationToken token = default);

        /// <summary>
        /// The reply (newest, not superseded) to a member message.
        /// </summary>
        /// <param name="userMessageId">Member message id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Reply, or null.</returns>
        Task<AssistantMessage?> ReadReplyToAsync(string userMessageId, CancellationToken token = default);

        /// <summary>
        /// Whether a reply is Pending or Streaming in a conversation.
        /// </summary>
        /// <param name="conversationId">Conversation id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a reply is in flight.</returns>
        Task<bool> HasInFlightAsync(string conversationId, CancellationToken token = default);

        /// <summary>
        /// Move a reply to Streaming (only from Pending).
        /// </summary>
        /// <param name="id">Reply id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task MarkStreamingAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Store the final state of a reply (content, status, telemetry, citations, stages) and update the conversation's
        /// last message. Only a Pending or Streaming reply is updated.
        /// </summary>
        /// <param name="reply">Reply.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when stored (false when the reply had already finished).</returns>
        Task<bool> CompleteReplyAsync(AssistantMessage reply, CancellationToken token = default);

        /// <summary>
        /// Enumerate a conversation's messages (AiCollections.Messages or AiCollections.AdminMessages), with citations
        /// and stages.
        /// </summary>
        /// <param name="request">Prepared request.</param>
        /// <param name="conversationId">Conversation id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<AssistantMessage>> EnumerateMessagesAsync(EnumerationRequest request, string conversationId, CancellationToken token = default);

        /// <summary>
        /// The newest messages of a conversation in chronological order (at most limit), with citations.
        /// </summary>
        /// <param name="conversationId">Conversation id.</param>
        /// <param name="limit">Most messages. Minimum 1, maximum 1000.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Messages, oldest first.</returns>
        Task<List<AssistantMessage>> RecentMessagesAsync(string conversationId, int limit, CancellationToken token = default);

        /// <summary>
        /// Insert or replace the feedback on a reply.
        /// </summary>
        /// <param name="feedback">Feedback.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task UpsertFeedbackAsync(AssistantFeedback feedback, CancellationToken token = default);

        /// <summary>
        /// Read the feedback on a reply.
        /// </summary>
        /// <param name="messageId">Reply id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Feedback, or null.</returns>
        Task<AssistantFeedback?> ReadFeedbackByMessageAsync(string messageId, CancellationToken token = default);

        /// <summary>
        /// Read feedback by id.
        /// </summary>
        /// <param name="id">Feedback id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Feedback, or null.</returns>
        Task<AssistantFeedback?> ReadFeedbackAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Delete the feedback on a reply.
        /// </summary>
        /// <param name="messageId">Reply id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when deleted.</returns>
        Task<bool> DeleteFeedbackByMessageAsync(string messageId, CancellationToken token = default);

        /// <summary>
        /// Enumerate feedback (AiCollections.Feedback).
        /// </summary>
        /// <param name="request">Prepared request.</param>
        /// <param name="filter">Filters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<AssistantFeedback>> EnumerateFeedbackAsync(EnumerationRequest request, AiEnumerationFilter filter, CancellationToken token = default);

        /// <summary>
        /// Mark feedback reviewed.
        /// </summary>
        /// <param name="id">Feedback id.</param>
        /// <param name="reviewerUserId">Reviewer, or null for the admin token.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when updated.</returns>
        Task<bool> ReviewFeedbackAsync(string id, string? reviewerUserId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Read the daily usage of one subject with one assistant.
        /// </summary>
        /// <param name="assistantId">Assistant id.</param>
        /// <param name="subjectType">User or Visitor.</param>
        /// <param name="subjectId">Subject id.</param>
        /// <param name="date">UTC date, yyyy-MM-dd.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Usage, or null.</returns>
        Task<AiDailyUsage?> ReadDailyUsageAsync(string assistantId, string subjectType, string subjectId, string date, CancellationToken token = default);

        /// <summary>
        /// Add messages, tokens, and cost to a subject's daily usage.
        /// </summary>
        /// <param name="usage">Amounts to add.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task AddDailyUsageAsync(AiDailyUsage usage, CancellationToken token = default);

        /// <summary>
        /// Delete up to limit conversations of an assistant idle since a time (or every conversation when the cutoff is
        /// in the future).
        /// </summary>
        /// <param name="assistantId">Assistant id.</param>
        /// <param name="idleBeforeUtc">Last message cutoff.</param>
        /// <param name="limit">Most conversations.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Conversations deleted.</returns>
        Task<int> DeleteIdleConversationsAsync(string assistantId, DateTime idleBeforeUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// Delete up to limit visitor conversations idle since a time.
        /// </summary>
        /// <param name="idleBeforeUtc">Last message cutoff.</param>
        /// <param name="limit">Most conversations.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Conversations deleted.</returns>
        Task<int> DeleteVisitorConversationsAsync(DateTime idleBeforeUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// Delete every conversation, feedback row, and daily usage row of a member (account deletion).
        /// </summary>
        /// <param name="userId">Member id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task DeleteForUserAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// Delete feedback older than a time.
        /// </summary>
        /// <param name="olderThanUtc">Cutoff.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        Task<int> PruneFeedbackAsync(DateTime olderThanUtc, CancellationToken token = default);

        /// <summary>
        /// Mark replies left Pending or Streaming before a time as Failed (a task that stopped mid-reply).
        /// </summary>
        /// <param name="beforeUtc">Created before this time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows updated.</returns>
        Task<int> FailStaleRepliesAsync(DateTime beforeUtc, CancellationToken token = default);

        /// <summary>
        /// A member's conversations in identifier order after a key (account export).
        /// </summary>
        /// <param name="userId">Member id.</param>
        /// <param name="afterId">Exclusive key, or null for the first page.</param>
        /// <param name="limit">Most conversations (1 to 500).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Conversations.</returns>
        Task<List<AssistantConversation>> ListByUserAsync(string userId, string? afterId, int limit, CancellationToken token = default);

        /// <summary>
        /// A conversation's messages in (created, identifier) order after a message, with citations (account export).
        /// </summary>
        /// <param name="conversationId">Conversation identifier.</param>
        /// <param name="afterMessageId">Exclusive key message, or null for the first page.</param>
        /// <param name="limit">Most messages (1 to 500).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Messages.</returns>
        Task<List<AssistantMessage>> ListMessagesAfterAsync(string conversationId, string? afterMessageId, int limit, CancellationToken token = default);

        /// <summary>
        /// Feedback rows of several messages, keyed by message identifier.
        /// </summary>
        /// <param name="messageIds">Message identifiers (bounded by the caller's page).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Feedback by message.</returns>
        Task<Dictionary<string, AssistantFeedback>> ReadFeedbackByMessagesAsync(IEnumerable<string> messageIds, CancellationToken token = default);
    }
}
