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
    /// Broadcast, segment, audience, and delivery methods (docs/PHASE5_CONTRACT.md sections 8 and 16).
    /// </summary>
    public interface IBroadcastMethods
    {
        /// <summary>
        /// Create a saved segment with its filter rows.
        /// </summary>
        /// <param name="segment">Segment.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The segment.</returns>
        Task<BroadcastSegment> CreateSegmentAsync(BroadcastSegment segment, CancellationToken token = default);

        /// <summary>
        /// Read a saved segment with its filters, or null.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Segment, or null.</returns>
        Task<BroadcastSegment?> ReadSegmentAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Replace a saved segment's name, description, and filters.
        /// </summary>
        /// <param name="segment">Segment.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the segment exists.</returns>
        Task<bool> UpdateSegmentAsync(BroadcastSegment segment, CancellationToken token = default);

        /// <summary>
        /// Delete a saved segment and its filters (broadcasts keep their own copies).
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when deleted.</returns>
        Task<bool> DeleteSegmentAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate saved segments (filters included).
        /// </summary>
        /// <param name="request">Normalized request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<BroadcastSegment>> EnumerateSegmentsAsync(EnumerationRequest request, CancellationToken token = default);

        /// <summary>
        /// Create a broadcast with its filter rows.
        /// </summary>
        /// <param name="broadcast">Broadcast.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The broadcast.</returns>
        Task<Broadcast> CreateAsync(Broadcast broadcast, CancellationToken token = default);

        /// <summary>
        /// Read a broadcast with its filters, or null.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Broadcast, or null.</returns>
        Task<Broadcast?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Replace a broadcast's content, channels, category, audience, and schedule, only while it is Draft or Scheduled.
        /// </summary>
        /// <param name="broadcast">Broadcast.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when written; false when it is no longer editable.</returns>
        Task<bool> UpdateAsync(Broadcast broadcast, CancellationToken token = default);

        /// <summary>
        /// Delete a Draft broadcast and its filters.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when deleted; false when it is not a Draft.</returns>
        Task<bool> DeleteDraftAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Enumerate broadcasts (never test copies), optionally by status and channel.
        /// </summary>
        /// <param name="request">Normalized request.</param>
        /// <param name="statuses">Statuses, or empty for every status.</param>
        /// <param name="channel">Channel, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<Broadcast>> EnumerateAsync(EnumerationRequest request, List<BroadcastStatusEnum> statuses, BroadcastChannelEnum? channel, CancellationToken token = default);

        /// <summary>
        /// Move a broadcast from one of the given statuses to another (a conditional update).
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="from">Allowed current statuses.</param>
        /// <param name="to">New status.</param>
        /// <param name="sendAtUtc">New schedule (Scheduled), or null to keep it.</param>
        /// <param name="nowUtc">Now (startedutc for Sending, canceledutc for Canceled, sentutc for Sent).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when moved.</returns>
        Task<bool> TransitionAsync(string id, IReadOnlyCollection<BroadcastStatusEnum> from, BroadcastStatusEnum to, DateTime? sendAtUtc, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Broadcasts (not test copies) that started in a time window or are scheduled to send in it, except one.
        /// </summary>
        /// <param name="startUtc">Window start (inclusive).</param>
        /// <param name="endUtc">Window end (exclusive).</param>
        /// <param name="exceptId">Broadcast to leave out, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountUsedInWindowAsync(DateTime startUtc, DateTime endUtc, string? exceptId, CancellationToken token = default);

        /// <summary>
        /// Move due Scheduled broadcasts to Sending.
        /// </summary>
        /// <param name="nowUtc">Now.</param>
        /// <param name="limit">Most broadcasts moved.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Identifiers moved.</returns>
        Task<List<string>> StartDueAsync(DateTime nowUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// Broadcasts in Sending, oldest first.
        /// </summary>
        /// <param name="limit">Most returned.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Identifiers.</returns>
        Task<List<string>> ListSendingAsync(int limit, CancellationToken token = default);

        /// <summary>
        /// Broadcasts (not test copies) that finished or were canceled after a time, for the delivery statistics refresh.
        /// </summary>
        /// <param name="sinceUtc">Earliest finish.</param>
        /// <param name="limit">Most returned.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Identifiers.</returns>
        Task<List<string>> ListRecentlyFinishedAsync(DateTime sinceUtc, int limit, CancellationToken token = default);

        /// <summary>
        /// Record keyset progress of a Sending broadcast.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="cursorUserId">Last user id expanded.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the broadcast is still Sending.</returns>
        Task<bool> RecordProgressAsync(string id, string cursorUserId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Synchronize the delivery statuses of a broadcast with the push states and receipts of its notifications and
        /// with its email message statuses, then recompute and store its counters.
        /// </summary>
        /// <param name="id">Identifier.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The counters.</returns>
        Task<BroadcastStats> SyncStatsAsync(string id, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Count a segment's audience and the recipients each channel can reach.
        /// </summary>
        /// <param name="audience">Compiled segment.</param>
        /// <param name="essential">Whether the Announcements preferences are ignored (never suppressions).</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Counts.</returns>
        Task<BroadcastAudienceCounts> CountAudienceAsync(BroadcastAudience audience, bool essential, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// The first recipients of a segment by user id.
        /// </summary>
        /// <param name="audience">Compiled segment.</param>
        /// <param name="limit">Most returned (at most 10 in previews).</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>User identifiers.</returns>
        Task<List<string>> SampleAudienceAsync(BroadcastAudience audience, int limit, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// One keyset batch of a segment's recipients ordered by user id.
        /// </summary>
        /// <param name="audience">Compiled segment.</param>
        /// <param name="afterUserId">Keyset position, or null.</param>
        /// <param name="limit">Batch size.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Recipients.</returns>
        Task<List<BroadcastRecipient>> EnumerateRecipientsAsync(BroadcastAudience audience, string? afterUserId, int limit, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Insert delivery rows; existing rows (same broadcast and member) are left alone.
        /// </summary>
        /// <param name="deliveries">Deliveries.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows inserted.</returns>
        Task<int> CreateDeliveriesAsync(List<BroadcastDelivery> deliveries, CancellationToken token = default);

        /// <summary>
        /// Deliveries of a broadcast to some members that still have a Pending channel.
        /// </summary>
        /// <param name="broadcastId">Broadcast.</param>
        /// <param name="userIds">Members.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Deliveries.</returns>
        Task<List<BroadcastDelivery>> ListPendingAsync(string broadcastId, IEnumerable<string> userIds, CancellationToken token = default);

        /// <summary>
        /// Write the in-app and push statuses and the notification of deliveries.
        /// </summary>
        /// <param name="deliveries">Deliveries.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task UpdateNotificationStatusesAsync(List<BroadcastDelivery> deliveries, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Settle a Pending email channel without sending (NoAddress, SkippedPreference, Suppressed).
        /// </summary>
        /// <param name="deliveryId">Delivery.</param>
        /// <param name="status">Final status.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the channel was Pending.</returns>
        Task<bool> SettleEmailAsync(string deliveryId, BroadcastDeliveryStatusEnum status, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Claim a Pending email channel before sending (at most once): status Queued and the queued time.
        /// </summary>
        /// <param name="deliveryId">Delivery.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when this call claimed it.</returns>
        Task<bool> ClaimEmailAsync(string deliveryId, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Record the queued email message of a delivery.
        /// </summary>
        /// <param name="deliveryId">Delivery.</param>
        /// <param name="emailMessageId">Email message, or null when none was queued.</param>
        /// <param name="status">Status after queueing.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        Task SetEmailMessageAsync(string deliveryId, string? emailMessageId, BroadcastDeliveryStatusEnum status, DateTime nowUtc, CancellationToken token = default);

        /// <summary>
        /// Broadcast emails of the property handed to the queue since a time (the per-minute throttle).
        /// </summary>
        /// <param name="sinceUtc">Start.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountEmailsQueuedSinceAsync(DateTime sinceUtc, CancellationToken token = default);

        /// <summary>
        /// Enumerate a broadcast's deliveries.
        /// </summary>
        /// <param name="request">Normalized request.</param>
        /// <param name="broadcastId">Broadcast.</param>
        /// <param name="channel">Channel, or null.</param>
        /// <param name="status">Status, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<BroadcastDelivery>> EnumerateDeliveriesAsync(EnumerationRequest request, string broadcastId, BroadcastChannelEnum? channel, BroadcastDeliveryStatusEnum? status, CancellationToken token = default);

        /// <summary>
        /// The delivery of a broadcast to a member, or null.
        /// </summary>
        /// <param name="broadcastId">Broadcast.</param>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Delivery, or null.</returns>
        Task<BroadcastDelivery?> ReadDeliveryAsync(string broadcastId, string userId, CancellationToken token = default);

        /// <summary>
        /// Enumerate the broadcasts a member received (a delivery exists and the broadcast started), newest first.
        /// </summary>
        /// <param name="request">Normalized request.</param>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<Broadcast>> EnumerateReceivedAsync(EnumerationRequest request, string userId, CancellationToken token = default);

        /// <summary>
        /// Delete every delivery of a member (account deletion).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        Task<int> DeleteDeliveriesForUserAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// The Broadcast notification of a broadcast per recipient.
        /// </summary>
        /// <param name="broadcastId">Broadcast.</param>
        /// <param name="userIds">Members.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Notification identifier by member.</returns>
        Task<Dictionary<string, string>> NotificationIdsAsync(string broadcastId, IEnumerable<string> userIds, CancellationToken token = default);

        /// <summary>
        /// Of some broadcasts, those that ignore the Announcements push preference (Essential broadcasts and test copies).
        /// </summary>
        /// <param name="ids">Broadcasts.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Identifiers.</returns>
        Task<HashSet<string>> PreferenceExemptAsync(IEnumerable<string> ids, CancellationToken token = default);
    }
}
