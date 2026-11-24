namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Direct conversation, member state, messaging settings, known contacts, push and email state, and lapse methods
    /// (docs/PHASE5_CONTRACT.md sections 3 and 16, as-built note B). Every statement is bound to one property.
    /// </summary>
    public interface IConversationMethods
    {
        /// <summary>
        /// Read a conversation, or null.
        /// </summary>
        /// <param name="id">Conversation identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Conversation, or null.</returns>
        Task<Conversation?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Read the direct conversation of a pair, or null.
        /// </summary>
        /// <param name="directKey">Sorted member ids joined with a colon.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Conversation, or null.</returns>
        Task<Conversation?> ReadByDirectKeyAsync(string directKey, CancellationToken token = default);

        /// <summary>
        /// Create a direct conversation with its members unless the pair already has one (unique direct key).
        /// </summary>
        /// <param name="conversation">Conversation.</param>
        /// <param name="members">Members.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when created; false when the pair already had a conversation.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<bool> CreateDirectAsync(Conversation conversation, List<ConversationMember> members, CancellationToken token = default);

        /// <summary>
        /// The conversation with the member row of a user and the other members, or null when the user is not a member.
        /// </summary>
        /// <param name="conversationId">Conversation identifier.</param>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entry, or null.</returns>
        Task<ConversationEntry?> ReadEntryAsync(string conversationId, string userId, CancellationToken token = default);

        /// <summary>
        /// Entries of several conversations for one member in one batched load (unknown ids and conversations the user does
        /// not belong to are absent).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="conversationIds">Conversation identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entries keyed by conversation id.</returns>
        Task<Dictionary<string, ConversationEntry>> ReadEntriesAsync(string userId, IEnumerable<string> conversationIds, CancellationToken token = default);

        /// <summary>
        /// The members of a conversation (bounded by the conversation size).
        /// </summary>
        /// <param name="conversationId">Conversation identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Members.</returns>
        Task<List<ConversationMember>> ReadMembersAsync(string conversationId, CancellationToken token = default);

        /// <summary>
        /// One page of a member's conversation rows (MessagingCollections.Conversations, Declined, or Changes).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="request">Normalized request.</param>
        /// <param name="filter">Typed conditions.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of the caller's member rows.</returns>
        Task<EnumerationResult<ConversationMember>> EnumerateAsync(string userId, EnumerationRequest request, ConversationListFilter filter, CancellationToken token = default);

        /// <summary>
        /// Unread messages by others per conversation for a member (above the read and hidden positions, capped at 999).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="conversationIds">Conversation identifiers (a page).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Counts keyed by conversation id (absent means 0).</returns>
        Task<Dictionary<string, int>> UnreadCountsAsync(string userId, IEnumerable<string> conversationIds, CancellationToken token = default);

        /// <summary>
        /// A member's unread totals over visible conversations (not declined, not deleted for them, the other member not
        /// blocked by them).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Counts.</returns>
        Task<MessagingUnreadCounts> UnreadSummaryAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// Move a member's read position forward (monotonic, at most the conversation's last seq; delivery follows) and
        /// touch the other members' rows so the receipt surfaces in their sync.
        /// </summary>
        /// <param name="conversationId">Conversation identifier.</param>
        /// <param name="userId">Member.</param>
        /// <param name="seq">Requested position.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The read position afterwards, or null when the member row does not exist.</returns>
        Task<long?> MarkReadAsync(string conversationId, string userId, long seq, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Move a member's delivery position forward (monotonic, at most the conversation's last seq).
        /// </summary>
        /// <param name="conversationId">Conversation identifier.</param>
        /// <param name="userId">Member.</param>
        /// <param name="seq">Requested position.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The delivery position when it moved, else null.</returns>
        Task<long?> MarkDeliveredAsync(string conversationId, string userId, long seq, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Set a member's folder (Inbox or Archived; never while a request is pending for the member).
        /// </summary>
        /// <param name="memberId">Member row identifier.</param>
        /// <param name="folder">Folder.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when changed or already set.</returns>
        Task<bool> SetFolderAsync(string memberId, ConversationFolderEnum folder, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Mute or unmute a conversation for a member.
        /// </summary>
        /// <param name="memberId">Member row identifier.</param>
        /// <param name="muted">Muted.</param>
        /// <param name="untilUtc">Mute end, or null.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the row exists.</returns>
        Task<bool> SetMuteAsync(string memberId, bool muted, DateTime? untilUtc, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Delete a conversation for one member: hide every message up to the current last seq and remove it from their
        /// lists. When no member is left with the conversation, it is scheduled for purge at purgeAfterUtc.
        /// </summary>
        /// <param name="conversationId">Conversation identifier.</param>
        /// <param name="userId">Member.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="purgeAfterUtc">Purge time when every member deleted it.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the member row exists.</returns>
        Task<bool> DeleteForMemberAsync(string conversationId, string userId, DateTime nowUtc, DateTime purgeAfterUtc, CancellationToken token = default);

        /// <summary>
        /// Bring a conversation back into a member's lists (keeps the hidden range) and cancel a scheduled purge.
        /// </summary>
        /// <param name="conversationId">Conversation identifier.</param>
        /// <param name="userId">Member.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the member row was deleted and is restored.</returns>
        Task<bool> RestoreForMemberAsync(string conversationId, string userId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Accept a pending or declined request: the recipient's state clears into the Inbox, the sender's awaiting state
        /// clears, and the recipient records the sender as an accepted contact (one transaction, conditional).
        /// </summary>
        /// <param name="conversationId">Conversation identifier.</param>
        /// <param name="recipientUserId">Recipient (the caller).</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a request was accepted.</returns>
        Task<bool> AcceptAsync(string conversationId, string recipientUserId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Decline a pending request (conditional).
        /// </summary>
        /// <param name="conversationId">Conversation identifier.</param>
        /// <param name="recipientUserId">Recipient (the caller).</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a pending request was declined.</returns>
        Task<bool> DeclineAsync(string conversationId, string recipientUserId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Requests a member sent that are still awaiting acceptance.
        /// </summary>
        /// <param name="userId">Sender.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountPendingSentAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// Of the candidates, the members the user knows (the user messaged or accepted them).
        /// </summary>
        /// <param name="userId">Member whose contacts are checked.</param>
        /// <param name="candidateUserIds">Candidates.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Known candidates.</returns>
        Task<HashSet<string>> ContactsAmongAsync(string userId, IEnumerable<string> candidateUserIds, CancellationToken token = default);

        /// <summary>
        /// Members who list any of the candidates as a contact of the user: for each candidate, whether the candidate
        /// knows the user (the candidate messaged or accepted the user).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="candidateUserIds">Candidates.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Candidates that know the user.</returns>
        Task<HashSet<string>> KnownByAmongAsync(string userId, IEnumerable<string> candidateUserIds, CancellationToken token = default);

        /// <summary>
        /// Messaging settings of several members (members without a row are absent).
        /// </summary>
        /// <param name="userIds">Members.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Settings keyed by user id.</returns>
        Task<Dictionary<string, MemberMessagingSettings>> ReadSettingsAsync(IEnumerable<string> userIds, CancellationToken token = default);

        /// <summary>
        /// Insert or replace a member's messaging settings.
        /// </summary>
        /// <param name="settings">Settings (UpdatedUtc is required).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The settings are null.</exception>
        Task WriteSettingsAsync(MemberMessagingSettings settings, CancellationToken token = default);

        /// <summary>
        /// Touch a member's rows in the conversations shared with another member (block changes surface in sync).
        /// </summary>
        /// <param name="userId">Member whose rows change.</param>
        /// <param name="otherUserId">The other member.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows touched.</returns>
        Task<int> TouchSharedAsync(string userId, string otherUserId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Claim the right to push a message of a conversation to a member: at most one push per conversation per
        /// window while the member has not read past the last pushed message.
        /// </summary>
        /// <param name="conversationId">Conversation identifier.</param>
        /// <param name="userId">Recipient.</param>
        /// <param name="seq">Message seq.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="windowSeconds">Coalescing window (0 disables coalescing).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the push may be sent.</returns>
        Task<bool> TryClaimPushAsync(string conversationId, string userId, long seq, DateTime nowUtc, int windowSeconds, CancellationToken token = default);

        /// <summary>
        /// Members with unread direct messages older than unreadBeforeUtc whose last unread email was before
        /// sentBeforeUtc (or never), excluding requests, muted conversations, and blocked members.
        /// </summary>
        /// <param name="unreadBeforeUtc">Messages must be older than this.</param>
        /// <param name="sentBeforeUtc">The last email must be older than this.</param>
        /// <param name="afterUserId">Keyset position (user id), or null.</param>
        /// <param name="limit">Maximum members. Minimum 1.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Candidates ordered by user id.</returns>
        Task<List<MessageEmailCandidate>> ListEmailCandidatesAsync(DateTime unreadBeforeUtc, DateTime sentBeforeUtc, string? afterUserId, int limit, CancellationToken token = default);

        /// <summary>
        /// Distinct authors of a member's unread messages older than a time, most recent first.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="unreadBeforeUtc">Messages must be older than this.</param>
        /// <param name="limit">Maximum authors.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Author ids.</returns>
        Task<List<string>> UnreadSendersAsync(string userId, DateTime unreadBeforeUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// Distinct authors of a member's unread messages older than a time.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="unreadBeforeUtc">Messages must be older than this.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountUnreadSendersAsync(string userId, DateTime unreadBeforeUtc, CancellationToken token = default);

        /// <summary>
        /// Record that the unread messages email was sent to a member.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task MarkEmailSentAsync(string userId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Members that belong to at least one conversation, in user id order (lapse scans).
        /// </summary>
        /// <param name="afterUserId">Keyset position, or null.</param>
        /// <param name="limit">Maximum members.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>User ids.</returns>
        Task<List<string>> MemberUserIdsAsync(string? afterUserId, int limit, CancellationToken token = default);

        /// <summary>
        /// Record that a member lost messaging access (keeps the first lapse time).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a new lapse was recorded.</returns>
        Task<bool> RecordLapseAsync(string userId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Clear a member's lapse (access returned).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a lapse was cleared.</returns>
        Task<bool> ClearLapseAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// Read a member's lapse, or null.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Lapse, or null.</returns>
        Task<MessagingLapse?> ReadLapseAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// Lapses older than a time that were not purged yet.
        /// </summary>
        /// <param name="lapsedBeforeUtc">Cutoff.</param>
        /// <param name="limit">Maximum rows.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Lapses.</returns>
        Task<List<MessagingLapse>> ListLapsesDueAsync(DateTime lapsedBeforeUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// Remove every conversation for a lapsed member (as delete for me), clear their contacts, and mark the lapse
        /// purged (one transaction).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="purgeAfterUtc">Purge time for conversations no member keeps.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Conversations removed for the member.</returns>
        Task<int> PurgeForLapsedAsync(string userId, DateTime nowUtc, DateTime purgeAfterUtc, CancellationToken token = default);

        /// <summary>
        /// Conversations whose purge time passed and that no member keeps.
        /// </summary>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="limit">Maximum rows.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Conversation ids.</returns>
        Task<List<string>> ListPurgeDueAsync(DateTime nowUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// Purge a conversation with its messages (tombstones included), attachments references, reactions, member rows,
        /// labels and tags of messages and member rows, and push state, when it is still due (one transaction).
        /// </summary>
        /// <param name="conversationId">Conversation identifier.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when purged.</returns>
        Task<bool> PurgeConversationAsync(string conversationId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Account deletion: the member's messages become tombstones without body and author, their memberships,
        /// settings, contacts (both directions), email and push state, and lapse are deleted, and conversations no member
        /// keeps are scheduled for purge (one transaction).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="purgeAfterUtc">Purge time for conversations no member keeps.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Messages turned into tombstones.</returns>
        Task<int> EraseForAccountAsync(string userId, DateTime nowUtc, DateTime purgeAfterUtc, CancellationToken token = default);
    }
}
