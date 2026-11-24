namespace TrellisKit.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Moderation snapshots of assistant conversations (docs/AI_CONTRACT.md section 12.3): tables
    /// aimoderationsnapshots, aimoderationsnapshotmessages, and aimoderationsnapshotcitations.
    /// </summary>
    public interface IAiModerationMethods
    {
        /// <summary>
        /// Store a snapshot with its messages and citations in one transaction. At most one snapshot exists per message;
        /// a second call for the same message stores nothing.
        /// </summary>
        /// <param name="snapshot">Snapshot.</param>
        /// <param name="messages">Messages in conversation order.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the snapshot was stored, false when one already existed for the message.</returns>
        Task<bool> CreateSnapshotAsync(AiModerationSnapshot snapshot, IReadOnlyList<AiModerationSnapshotMessage> messages, CancellationToken token = default);

        /// <summary>
        /// The snapshot of a message (with its citations), or null.
        /// </summary>
        /// <param name="messageId">Message identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Snapshot, or null.</returns>
        Task<AiModerationSnapshot?> ReadByMessageAsync(string messageId, CancellationToken token = default);

        /// <summary>
        /// The snapshots of several messages, keyed by message identifier (without citations).
        /// </summary>
        /// <param name="messageIds">Message identifiers (bounded by the caller's page).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Snapshots.</returns>
        Task<Dictionary<string, AiModerationSnapshot>> ReadManyByMessageAsync(IEnumerable<string> messageIds, CancellationToken token = default);

        /// <summary>
        /// The last messages of a snapshot, oldest first.
        /// </summary>
        /// <param name="snapshotId">Snapshot identifier.</param>
        /// <param name="limit">Most messages (1 to 100).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Messages.</returns>
        Task<List<AiModerationSnapshotMessage>> LastMessagesAsync(string snapshotId, int limit, CancellationToken token = default);

        /// <summary>
        /// Enumerate a snapshot's messages (AiCollections.SnapshotMessages).
        /// </summary>
        /// <param name="request">Prepared request.</param>
        /// <param name="snapshotId">Snapshot identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        Task<EnumerationResult<AiModerationSnapshotMessage>> EnumerateMessagesAsync(EnumerationRequest request, string snapshotId, CancellationToken token = default);

        /// <summary>
        /// Delete snapshots that expired before a time, with their messages and citations.
        /// </summary>
        /// <param name="beforeUtc">Expiry cutoff.</param>
        /// <param name="limit">Most snapshots per call (1 to 1000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Snapshots deleted.</returns>
        Task<int> PruneAsync(DateTime beforeUtc, int limit, CancellationToken token = default);
    }
}
