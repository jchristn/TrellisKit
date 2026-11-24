namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Direct message, attachment reference, history, search, report snapshot, retention, and export methods
    /// (docs/PHASE5_CONTRACT.md sections 3.3 to 3.9 and 16, as-built note B). Every statement is bound to one property.
    /// </summary>
    public interface IMessageMethods
    {
        /// <summary>
        /// Read a message with its attachment ids, or null.
        /// </summary>
        /// <param name="id">Message identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Message, or null.</returns>
        Task<Message?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read several messages with their attachment ids.
        /// </summary>
        /// <param name="ids">Message identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Messages keyed by id.</returns>
        Task<Dictionary<string, Message>> ReadManyAsync(IEnumerable<string> ids, CancellationToken token = default);

        /// <summary>
        /// Read a message by its idempotency key, or null.
        /// </summary>
        /// <param name="conversationId">Conversation identifier.</param>
        /// <param name="authorUserId">Author.</param>
        /// <param name="clientMessageId">Client message id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Message, or null.</returns>
        Task<Message?> ReadByClientIdAsync(string conversationId, string authorUserId, string clientMessageId, CancellationToken token = default);

        /// <summary>
        /// Send a message in one transaction: lock and increment the conversation's last seq, insert the message with
        /// that seq and its attachment references, move the sender's read and delivery positions, accept a pending
        /// request implicitly (a reply) or count a message awaiting acceptance (refused past the limit), bring the
        /// conversation back for members who deleted it (not for a member who declined), record the sender's known
        /// contacts, and run the realtime notify statement. A repeated client message id returns the stored message
        /// with Created false (also under concurrent retries).
        /// </summary>
        /// <param name="draft">Message.</param>
        /// <param name="notifyStatement">Realtime notify statement, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Outcome.</returns>
        /// <exception cref="ArgumentNullException">The draft is null.</exception>
        Task<MessageSendOutcome> SendAsync(MessageDraft draft, string? notifyStatement, CancellationToken token = default);

        /// <summary>
        /// Edit a message once: the author's Visible, unedited message created at or after editableSinceUtc keeps its
        /// previous text as the original body (conditional).
        /// </summary>
        /// <param name="id">Message identifier.</param>
        /// <param name="authorUserId">Author.</param>
        /// <param name="body">New text.</param>
        /// <param name="excerpt">New excerpt.</param>
        /// <param name="linkPreviewId">Link preview of the new body, or null.</param>
        /// <param name="editableSinceUtc">Edit window start.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The edited message, or null when the edit was not allowed.</returns>
        Task<Message?> EditAsync(string id, string authorUserId, string body, string excerpt, string? linkPreviewId, DateTime editableSinceUtc, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Delete the author's message for everyone (state Deleted; the text and attachment references are kept
        /// internally for moderation).
        /// </summary>
        /// <param name="id">Message identifier.</param>
        /// <param name="authorUserId">Author.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The message afterwards, or null when it is not the author's or already deleted.</returns>
        Task<Message?> DeleteForEveryoneAsync(string id, string authorUserId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Moderator Remove (Visible to Removed) or Restore (Removed to Visible), conditional.
        /// </summary>
        /// <param name="id">Message identifier.</param>
        /// <param name="removed">True to remove, false to restore.</param>
        /// <param name="moderatorUserId">Moderator, or null.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the state changed.</returns>
        Task<bool> SetRemovedAsync(string id, bool removed, string? moderatorUserId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Recompute a message's reaction total from the reactions table.
        /// </summary>
        /// <param name="id">Message identifier.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task RecomputeReactionTotalAsync(string id, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// One page of a conversation's history above the member's hidden position (MessagingCollections.History):
        /// tombstones and removed messages are included unless searching.
        /// </summary>
        /// <param name="conversationId">Conversation identifier.</param>
        /// <param name="hiddenBeforeSeq">Hidden position of the caller.</param>
        /// <param name="request">Normalized request.</param>
        /// <param name="beforeSeq">Exclusive upper bound, or null.</param>
        /// <param name="afterSeq">Exclusive lower bound, or null.</param>
        /// <param name="search">Full text query, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<Message>> EnumerateHistoryAsync(string conversationId, long hiddenBeforeSeq, EnumerationRequest request, long? beforeSeq, long? afterSeq, string? search, CancellationToken token = default);

        /// <summary>
        /// Enumerate a bounded window around a seq (half before including the seq, half after), above the hidden position.
        /// </summary>
        /// <param name="conversationId">Conversation identifier.</param>
        /// <param name="hiddenBeforeSeq">Hidden position of the caller.</param>
        /// <param name="seq">Center.</param>
        /// <param name="half">Messages per side.</param>
        /// <param name="request">Normalized request (labels, tags, created window).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Standard page within the bounded window.</returns>
        Task<EnumerationResult<Message>> AroundAsync(string conversationId, long hiddenBeforeSeq, long seq, int half, EnumerationRequest request, CancellationToken token = default);

        /// <summary>
        /// One page of full text search over a member's conversations (MessagingCollections.Search): Visible messages
        /// above the member's hidden position in conversations the member belongs to and has not deleted or declined,
        /// excluding conversations with members the caller blocked.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="request">Normalized request (search required).</param>
        /// <param name="conversationId">Only this conversation, or null.</param>
        /// <param name="withUserId">Only conversations with this member, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<MessageSearchRow>> SearchAsync(string userId, EnumerationRequest request, string? conversationId, string? withUserId, CancellationToken token = default);

        /// <summary>
        /// Messages of a conversation with seq at or below atSeq and above minSeqExclusive, newest first.
        /// </summary>
        /// <param name="conversationId">Conversation identifier.</param>
        /// <param name="atSeq">Highest seq.</param>
        /// <param name="minSeqExclusive">Lowest seq (exclusive).</param>
        /// <param name="limit">Maximum messages.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Messages descending by seq.</returns>
        Task<List<Message>> LatestUpToAsync(string conversationId, long atSeq, long minSeqExclusive, int limit, CancellationToken token = default);

        /// <summary>
        /// Store a report snapshot with its items (one transaction; one snapshot per report).
        /// </summary>
        /// <param name="snapshot">Snapshot.</param>
        /// <param name="items">Items.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when stored; false when the report already had one.</returns>
        Task<bool> CreateSnapshotAsync(MessageReportSnapshot snapshot, List<MessageReportSnapshotItem> items, CancellationToken token = default);

        /// <summary>
        /// The snapshot of a report, or null.
        /// </summary>
        /// <param name="reportId">Report identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Snapshot, or null.</returns>
        Task<MessageReportSnapshot?> ReadSnapshotByReportAsync(string reportId, CancellationToken token = default);

        /// <summary>
        /// Snapshots of several reports keyed by report id.
        /// </summary>
        /// <param name="reportIds">Report identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Snapshots.</returns>
        Task<Dictionary<string, MessageReportSnapshot>> ReadSnapshotsByReportsAsync(IEnumerable<string> reportIds, CancellationToken token = default);

        /// <summary>
        /// One page of direct message reports (scope Messaging; MessagingCollections.Reports).
        /// </summary>
        /// <param name="request">Normalized request.</param>
        /// <param name="status">Report status.</param>
        /// <param name="targetType">Message or Conversation, or null for both.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<Report>> EnumerateReportsAsync(EnumerationRequest request, ReportStatusEnum status, TargetTypeEnum? targetType, CancellationToken token = default);

        /// <summary>
        /// The items of a snapshot in seq order (bounded).
        /// </summary>
        /// <param name="snapshotId">Snapshot identifier.</param>
        /// <param name="limit">Maximum items.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Items.</returns>
        Task<List<MessageReportSnapshotItem>> SnapshotItemsAsync(string snapshotId, int limit, CancellationToken token = default);

        /// <summary>
        /// The most recent snapshot item of a message, or null (moderation loads targets from snapshots).
        /// </summary>
        /// <param name="messageId">Message identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Item, or null.</returns>
        Task<MessageReportSnapshotItem?> LatestSnapshotItemAsync(string messageId, CancellationToken token = default);

        /// <summary>
        /// Schedule the purge of the snapshots of resolved reports and delete the snapshots whose purge time passed.
        /// </summary>
        /// <param name="retainDays">Days to keep after resolution.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="limit">Maximum snapshots deleted.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Snapshots deleted.</returns>
        Task<int> PruneSnapshotsAsync(int retainDays, DateTime nowUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// Message ids whose content is due for retention (created before the cutoff and still holding a body).
        /// </summary>
        /// <param name="createdBeforeUtc">Cutoff.</param>
        /// <param name="limit">Maximum ids.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Messages.</returns>
        Task<List<Message>> ListRetentionDueAsync(DateTime createdBeforeUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// Turn messages into tombstones: state Deleted, body, original body, excerpt, and attachment references purged.
        /// </summary>
        /// <param name="ids">Message identifiers.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Messages changed.</returns>
        Task<int> PurgeContentAsync(IEnumerable<string> ids, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Message ids with attachments of a conversation (purge) or of an author (account deletion), keyset by id.
        /// </summary>
        /// <param name="conversationId">Conversation, or null.</param>
        /// <param name="authorUserId">Author, or null.</param>
        /// <param name="afterId">Keyset position, or null.</param>
        /// <param name="limit">Maximum ids.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Message ids.</returns>
        Task<List<string>> WithAttachmentsAsync(string? conversationId, string? authorUserId, string? afterId, int limit, CancellationToken token = default);

        /// <summary>
        /// One export page: messages of the member's conversations the member can still see, ordered by id.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="afterId">Keyset position, or null.</param>
        /// <param name="limit">Maximum rows.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows.</returns>
        Task<List<MessageExportRow>> ExportPageAsync(string userId, string? afterId, int limit, CancellationToken token = default);
    }
}
