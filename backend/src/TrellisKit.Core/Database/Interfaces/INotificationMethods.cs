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
    /// Notification center data access (notifications and notificationactors), plus the narrow reads notification
    /// creation needs from tables other slices own (blocks, feedposts, users). Implementations are safe to call
    /// concurrently; aggregation is race safe through the ux_notifications_group partial unique index.
    /// </summary>
    public interface INotificationMethods
    {
        /// <summary>
        /// Insert one notification and, when it has an actor, its notificationactors row, in one transaction.
        /// </summary>
        /// <param name="notification">Notification (Id set by the caller).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The notification.</returns>
        /// <exception cref="ArgumentNullException">The notification is null.</exception>
        Task<Notification> CreateAsync(Notification notification, CancellationToken token = default);

        /// <summary>
        /// Insert many non-aggregated notifications (and their actor rows) in one transaction.
        /// </summary>
        /// <param name="notifications">Notifications (Ids set by the caller; GroupKey must be null).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows inserted.</returns>
        /// <exception cref="ArgumentNullException">The list is null.</exception>
        Task<int> CreateManyAsync(List<Notification> notifications, CancellationToken token = default);

        /// <summary>
        /// Create or update the recipient's unread notification for the draft's group key, atomically: insert when none
        /// is unread, otherwise move it forward (actor, updatedUtc), add the actor, recompute actorCount from the actor
        /// rows, and render the title from the count. Concurrent calls for one recipient and group produce one row.
        /// </summary>
        /// <param name="draft">Draft row (Id, GroupKey, and ActorUserId required). Its Title is ignored.</param>
        /// <param name="titleOne">Title when there is one actor.</param>
        /// <param name="titleTwo">Title when there are two actors.</param>
        /// <param name="titleManyPrefix">Title before the number of other actors when there are three or more.</param>
        /// <param name="titleManySuffix">Title after the number of other actors when there are three or more.</param>
        /// <param name="queuePush">Whether to queue a push (pushstate Pending) for a new row, or for an updated row not pushed within pushWindow.</param>
        /// <param name="pushWindow">An updated row pushed within this window is not queued again.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The resulting row (its Id equals the draft Id when it was inserted), or null when the unread row was read concurrently.</returns>
        /// <exception cref="ArgumentNullException">The draft or a required field is null.</exception>
        Task<Notification?> AggregateAsync(Notification draft, string titleOne, string titleTwo, string titleManyPrefix, string titleManySuffix, bool queuePush, TimeSpan pushWindow, CancellationToken token = default);

        /// <summary>
        /// Insert a grouped notification, or replace the title, body, path, actor, and time of the recipient's unread row
        /// with the same group key (Phase 5 ticket notifications, one row per ticket while unread). Works without an actor.
        /// </summary>
        /// <param name="draft">Row (Id and GroupKey required; Title is stored as given).</param>
        /// <param name="queuePush">Whether to queue a push for a new row, or for an updated row not pushed within pushWindow.</param>
        /// <param name="pushWindow">An updated row pushed within this window is not queued again.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The resulting row (its Id equals the draft Id when it was inserted).</returns>
        /// <exception cref="ArgumentNullException">The draft or a required field is null.</exception>
        Task<Notification?> UpsertGroupedAsync(Notification draft, bool queuePush, TimeSpan pushWindow, CancellationToken token = default);

        /// <summary>
        /// Read a notification.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Notification with up to three recent actors, or null.</returns>
        /// <exception cref="ArgumentNullException">The identifier is null or empty.</exception>
        Task<Notification?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate a recipient's notifications (collection NotificationCollections.Notifications). Each item carries up
        /// to three recent actors.
        /// </summary>
        /// <param name="recipientUserId">Recipient.</param>
        /// <param name="request">Prepared enumeration request.</param>
        /// <param name="unreadOnly">Only unread notifications.</param>
        /// <param name="type">Only this type, or null.</param>
        /// <param name="category">Only this category, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The recipient or request is null.</exception>
        Task<EnumerationResult<Notification>> EnumerateForRecipientAsync(string recipientUserId, EnumerationRequest request, bool unreadOnly, NotificationTypeEnum? type, NotificationCategoryEnum? category, CancellationToken token = default);

        /// <summary>
        /// Unread notifications of a recipient.
        /// </summary>
        /// <param name="recipientUserId">Recipient.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        /// <exception cref="ArgumentNullException">The recipient is null or empty.</exception>
        Task<long> CountUnreadAsync(string recipientUserId, CancellationToken token = default);

        /// <summary>
        /// The latest updatedUtc among a recipient's notifications.
        /// </summary>
        /// <param name="recipientUserId">Recipient.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Time, or null when there are none.</returns>
        /// <exception cref="ArgumentNullException">The recipient is null or empty.</exception>
        Task<DateTime?> LatestUpdatedAsync(string recipientUserId, CancellationToken token = default);

        /// <summary>
        /// Mark the recipient's notifications read. Unknown and foreign identifiers are ignored.
        /// </summary>
        /// <param name="recipientUserId">Recipient.</param>
        /// <param name="ids">Identifiers.</param>
        /// <param name="nowUtc">Read time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows changed.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<int> MarkReadAsync(string recipientUserId, List<string> ids, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Mark the recipient's notifications unread. Unknown and foreign identifiers are ignored. An aggregated
        /// notification stays read when another unread notification already holds its group.
        /// </summary>
        /// <param name="recipientUserId">Recipient.</param>
        /// <param name="ids">Identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows changed.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<int> MarkUnreadAsync(string recipientUserId, List<string> ids, CancellationToken token = default);

        /// <summary>
        /// Mark every unread notification of the recipient last updated at or before a time read.
        /// </summary>
        /// <param name="recipientUserId">Recipient.</param>
        /// <param name="beforeUtc">Cutoff (inclusive).</param>
        /// <param name="nowUtc">Read time.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows changed.</returns>
        /// <exception cref="ArgumentNullException">The recipient is null or empty.</exception>
        Task<int> MarkAllReadAsync(string recipientUserId, DateTime beforeUtc, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Which recipients already have a notification of a type for a target (fan-out idempotency).
        /// </summary>
        /// <param name="type">Type.</param>
        /// <param name="targetId">Target identifier.</param>
        /// <param name="recipientUserIds">Candidate recipients.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Recipients that already have one.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<HashSet<string>> ExistingRecipientsAsync(NotificationTypeEnum type, string targetId, IEnumerable<string> recipientUserIds, CancellationToken token = default);

        /// <summary>
        /// Which of the other members have a block with the member in either direction (reads the blocks table).
        /// </summary>
        /// <param name="userId">Member, for example the actor.</param>
        /// <param name="otherUserIds">Other members, for example the recipients.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Other members on either side of a block with the member.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        Task<HashSet<string>> BlockedEitherWayAsync(string userId, IEnumerable<string> otherUserIds, CancellationToken token = default);

        /// <summary>
        /// Notifications waiting for the push worker, oldest first.
        /// </summary>
        /// <param name="limit">Maximum rows.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Notifications.</returns>
        Task<List<Notification>> EnumeratePendingPushAsync(int limit, CancellationToken token = default);

        /// <summary>
        /// Move notifications from Pending to a push state (a claim: only rows still Pending change).
        /// </summary>
        /// <param name="ids">Identifiers.</param>
        /// <param name="state">New state.</param>
        /// <param name="lastPushedUtc">Value for lastPushedUtc, or null to leave it unchanged.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Identifiers that changed.</returns>
        /// <exception cref="ArgumentNullException">The list is null.</exception>
        Task<List<string>> TransitionPushStateAsync(List<string> ids, PushStateEnum state, DateTime? lastPushedUtc, CancellationToken token = default);

        /// <summary>
        /// Set a notification's push state unconditionally.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="state">State.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">The identifier is null or empty.</exception>
        Task SetPushStateAsync(string id, PushStateEnum state, CancellationToken token = default);

        /// <summary>
        /// Notifications of a recipient pushed at or after a time.
        /// </summary>
        /// <param name="recipientUserId">Recipient.</param>
        /// <param name="sinceUtc">Start of the window.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        /// <exception cref="ArgumentNullException">The recipient is null or empty.</exception>
        Task<long> CountPushedSinceAsync(string recipientUserId, DateTime sinceUtc, CancellationToken token = default);

        /// <summary>
        /// Whether any notification of a recipient with a group key was pushed at or after a time.
        /// </summary>
        /// <param name="recipientUserId">Recipient.</param>
        /// <param name="groupKey">Group key.</param>
        /// <param name="sinceUtc">Start of the window.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when one was.</returns>
        /// <exception cref="ArgumentNullException">An argument is null or empty.</exception>
        Task<bool> GroupPushedSinceAsync(string recipientUserId, string groupKey, DateTime sinceUtc, CancellationToken token = default);

        /// <summary>
        /// Read the fan-out relevant columns of a feed post (reads the feedposts table).
        /// </summary>
        /// <param name="feedPostId">Feed post identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Post, or null.</returns>
        /// <exception cref="ArgumentNullException">The identifier is null or empty.</exception>
        Task<NotificationSourcePost?> ReadSourcePostAsync(string feedPostId, CancellationToken token = default);

        /// <summary>
        /// The next keyset batch of active members for a fan-out, ordered by user id: not deleted, not banned, age
        /// attested, with an active entitlement or SystemAdmin, excluding the author and anyone on either side of a
        /// block with the author, with the category's in-app and email preferences.
        /// </summary>
        /// <param name="authorUserId">Author (excluded, and the block subject).</param>
        /// <param name="category">Category whose preferences are returned.</param>
        /// <param name="defaultEmail">Email preference for members without a row for the category.</param>
        /// <param name="afterUserId">Keyset position, or null for the first batch.</param>
        /// <param name="limit">Maximum rows.</param>
        /// <param name="nowUtc">Current time (entitlement check).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Recipients.</returns>
        /// <exception cref="ArgumentNullException">The author is null or empty.</exception>
        Task<List<FanoutRecipient>> EnumerateFanoutRecipientsAsync(string authorUserId, NotificationCategoryEnum category, bool defaultEmail, string? afterUserId, int limit, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// A keyset batch of fan-out recipients, as <see cref="EnumerateFanoutRecipientsAsync(string, NotificationCategoryEnum, bool, string?, int, DateTime, CancellationToken)"/>,
        /// with the membership requirement chosen by the property's access model (docs/ACCESS_MODELS_CONTRACT.md
        /// section 7): when <paramref name="requireMembership"/> is false, every active, age-attested member qualifies.
        /// </summary>
        /// <param name="authorUserId">Author (excluded, and the block subject).</param>
        /// <param name="category">Category whose preferences are returned.</param>
        /// <param name="defaultEmail">Email preference for members without a row for the category.</param>
        /// <param name="afterUserId">Keyset position, or null for the first batch.</param>
        /// <param name="limit">Maximum rows.</param>
        /// <param name="requireMembership">Whether recipients need an active entitlement (or SystemAdmin).</param>
        /// <param name="nowUtc">Current time (entitlement check).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Recipients.</returns>
        /// <exception cref="ArgumentNullException">The author is null or empty.</exception>
        Task<List<FanoutRecipient>> EnumerateFanoutRecipientsAsync(string authorUserId, NotificationCategoryEnum category, bool defaultEmail, string? afterUserId, int limit, bool requireMembership, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Members holding a role that can moderate: SystemAdmin, Moderator, or FeedModerator, not deleted or banned.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Member identifiers.</returns>
        Task<List<string>> EnumerateModeratorIdsAsync(CancellationToken token = default);

        /// <summary>
        /// Account deletion: delete every notification the member received (with their actor and delivery rows), remove
        /// the member from other members' notifications: notifications the member was the only actor of are deleted
        /// (their titles and bodies carry the member's name and erased text), aggregated ones are re-rendered from the
        /// remaining actors ("Alex Chen and 2 others reacted to your post"), and moderation notifications keep their
        /// text but lose the actor reference.
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Notifications deleted.</returns>
        /// <exception cref="ArgumentNullException">The member is null or empty.</exception>
        Task<int> DeleteForUserAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// Retention: delete up to a batch of read notifications last updated before one cutoff and unread ones last
        /// updated before another (actors and deliveries first).
        /// </summary>
        /// <param name="readBeforeUtc">Cutoff for read notifications.</param>
        /// <param name="unreadBeforeUtc">Cutoff for unread notifications.</param>
        /// <param name="limit">Maximum notifications per call.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Notifications deleted.</returns>
        Task<int> PruneAsync(DateTime readBeforeUtc, DateTime unreadBeforeUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// A member's unread notifications of one category updated after a time, newest first (email digests).
        /// </summary>
        /// <param name="userId">Recipient.</param>
        /// <param name="category">Category.</param>
        /// <param name="sinceUtc">Exclusive lower bound on the last update.</param>
        /// <param name="limit">Most notifications (1 to 100).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Notifications.</returns>
        Task<List<Notification>> ListUnreadSinceAsync(string userId, NotificationCategoryEnum category, DateTime sinceUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// Count a member's unread notifications of one category updated after a time (email digests).
        /// </summary>
        /// <param name="userId">Recipient.</param>
        /// <param name="category">Category.</param>
        /// <param name="sinceUtc">Exclusive lower bound on the last update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountUnreadSinceAsync(string userId, NotificationCategoryEnum category, DateTime sinceUtc, CancellationToken token = default);
    }
}
