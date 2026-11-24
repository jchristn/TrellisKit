namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;
    using TrellisKit.Core.Settings;

    /// <summary>
    /// Member blocks (contract section 9.2): block, unblock, the caller's block list, and the block queries other
    /// services use to hide content and suppress interactions. Blocks work for muted members and members without
    /// canPost. Staff (holders of any staff role in UserSummaryService.IsStaffRole: SystemAdmin, Moderator, FeedModerator,
    /// DirectoryModerator, EventManager, LibraryEditor, SupportAgent, Communicator) cannot be blocked, and a blocked member who later
    /// becomes staff is no longer hidden (<see cref="HiddenAuthorsForAsync"/>).
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class BlockService
    {
        #region Public-Members

        /// <summary>
        /// Rate limit bucket shared by block and unblock.
        /// </summary>
        public const string RateBucket = "blocks";

        /// <summary>
        /// Most accounts a member may block. Default 1000, minimum 1, maximum 100000. Exceeding it returns
        /// 409 BlockLimitReached.
        /// </summary>
        public int MaxBlocksPerMember
        {
            get { return _MaxBlocksPerMember; }
            set { _MaxBlocksPerMember = Math.Clamp(value, 1, 100000); }
        }

        /// <summary>
        /// Called after a block or unblock with the blocker and the blocked member (for example so direct conversations
        /// leave or return to the blocker's lists), or null. Set during module attachment only.
        /// </summary>
        public Func<string, string, CancellationToken, Task>? Changed { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly AppSettings _Settings;
        private readonly RateLimitService _Limits;
        private readonly UserSummaryService _Summaries;
        private readonly EnumerationService _Enumeration;
        private readonly MetadataService _Metadata;
        private int _MaxBlocksPerMember = 1000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Running settings (read live).</param>
        /// <param name="limits">Rate limits.</param>
        /// <param name="summaries">UserSummary builder.</param>
        /// <param name="enumeration">Enumeration preparation.</param>
        /// <param name="metadata">Labels and tags.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public BlockService(DatabaseDriverBase database, AppSettings settings, RateLimitService limits, UserSummaryService summaries, EnumerationService enumeration, MetadataService metadata)
        {
            _Enumeration = enumeration ?? throw new ArgumentNullException(nameof(enumeration));
            _Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Limits = limits ?? throw new ArgumentNullException(nameof(limits));
            _Summaries = summaries ?? throw new ArgumentNullException(nameof(summaries));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Block a member. Idempotent.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="targetUserId">Member to block.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>{ blocked: true }.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden (admin token), 400 CannotBlockSelf, 404 NotFound (unknown or deleted),
        /// 400 CannotBlockStaff, 429 RateLimited, 409 BlockLimitReached.</exception>
        public async Task<BlockStateResponse> BlockAsync(ContentActor actor, string targetUserId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            string userId = actor.RequireUserId();
            using (Activity? activity = TrellisKitTelemetry.StartSpan("blocks.block"))
            {
                if (String.Equals(userId, targetUserId, StringComparison.Ordinal)) throw new ApiErrorException(400, "CannotBlockSelf", "You cannot block yourself.");
                User? target = String.IsNullOrWhiteSpace(targetUserId) ? null : await _Database.Users.ReadAsync(targetUserId, token).ConfigureAwait(false);
                if (target == null || target.IsDeleted) throw ApiErrorException.NotFound("Member");
                HashSet<string> staff = await _Summaries.StaffAsync(new[] { target.Id }, token).ConfigureAwait(false);
                if (staff.Contains(target.Id)) throw new ApiErrorException(400, "CannotBlockStaff", "Staff members cannot be blocked. Report content instead.");
                _Limits.EnforceMember(actor, RateBucket, _Settings.Moderation.BlocksPerHour, RateLimitService.Hour);
                bool exists = await _Database.Blocks.CreateAsync(userId, target.Id, DateTime.UtcNow, MaxBlocksPerMember, token).ConfigureAwait(false);
                if (!exists) throw new ApiErrorException(409, "BlockLimitReached", "You can block up to " + MaxBlocksPerMember + " accounts. Unblock someone first.");
                TrellisKitTelemetry.Add(TrellisKitTelemetry.Blocks, 1, "action", "block");
                if (Changed != null) await Changed(userId, target.Id, token).ConfigureAwait(false);
                activity?.SetStatus(ActivityStatusCode.Ok);
                return new BlockStateResponse { Blocked = true };
            }
        }

        /// <summary>
        /// Unblock a member. Idempotent (unknown members return { blocked: false } too).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="targetUserId">Member to unblock.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>{ blocked: false }.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden (admin token), 429 RateLimited.</exception>
        public async Task<BlockStateResponse> UnblockAsync(ContentActor actor, string targetUserId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            string userId = actor.RequireUserId();
            using (Activity? activity = TrellisKitTelemetry.StartSpan("blocks.unblock"))
            {
                _Limits.EnforceMember(actor, RateBucket, _Settings.Moderation.BlocksPerHour, RateLimitService.Hour);
                if (!String.IsNullOrWhiteSpace(targetUserId))
                {
                    await _Database.Blocks.DeleteAsync(userId, targetUserId, token).ConfigureAwait(false);
                    if (Changed != null) await Changed(userId, targetUserId, token).ConfigureAwait(false);
                }
                TrellisKitTelemetry.Add(TrellisKitTelemetry.Blocks, 1, "action", "unblock");
                activity?.SetStatus(ActivityStatusCode.Ok);
                return new BlockStateResponse { Blocked = false };
            }
        }

        /// <summary>
        /// One enumeration page of the caller's blocks (GET /api/v1/me/blocks, collection SafetyCollections.Blocks:
        /// CreatedDescending by default, search over the blocked member's display name and handle, label and tag
        /// filters on the blocked member's User metadata). Each item carries the blocked member's public labels and tags.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Enumeration query, or null for defaults.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden (admin token), 400 ValidationFailed, 400 InvalidContinuationToken.</exception>
        public async Task<EnumerationResult<BlockListItem>> ListAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            string userId = actor.RequireUserId();
            bool staff = actor.IsStaffFor(TargetTypeEnum.User);
            EnumerationRequest request = _Enumeration.Prepare(query, SafetyCollections.Blocks, staff, "blocks|" + userId);
            EnumerationResult<Block> page = await _Database.Blocks.EnumerateByBlockerAsync(userId, request, token).ConfigureAwait(false);
            List<string> ids = page.Objects.Select(b => b.BlockedUserId).ToList();
            Dictionary<string, UserSummary> summaries = await _Summaries.BuildAsync(ids.Select(i => (string?)i), token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Metadata.LoadAsync(TargetTypeEnum.User, ids, staff, token).ConfigureAwait(false);
            return page.With(page.Objects.Select(b => new BlockListItem
            {
                User = summaries.TryGetValue(b.BlockedUserId, out UserSummary? summary) ? summary : UserSummary.Deleted(),
                CreatedUtc = b.CreatedUtc,
                Labels = metadata.TryGetValue(b.BlockedUserId, out ObjectMetadata? m) ? m.Labels : new List<string>(),
                Tags = m != null ? m.Tags : new Dictionary<string, string>()
            }).ToList());
        }

        /// <summary>
        /// Whether the blocker blocked the other member. False when either id is null or empty.
        /// </summary>
        /// <param name="blockerUserId">Member who may have blocked.</param>
        /// <param name="blockedUserId">Member who may be blocked.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when blocked.</returns>
        public Task<bool> HasBlockedAsync(string? blockerUserId, string? blockedUserId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(blockerUserId) || String.IsNullOrEmpty(blockedUserId)) return Task.FromResult(false);
            return _Database.Blocks.ExistsAsync(blockerUserId, blockedUserId, token);
        }

        /// <summary>
        /// Whether either member blocked the other. False when either id is null or empty.
        /// </summary>
        /// <param name="firstUserId">First member.</param>
        /// <param name="secondUserId">Second member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a block exists in either direction.</returns>
        public Task<bool> IsEitherBlockedAsync(string? firstUserId, string? secondUserId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(firstUserId) || String.IsNullOrEmpty(secondUserId)) return Task.FromResult(false);
            return _Database.Blocks.EitherAsync(firstUserId, secondUserId, token);
        }

        /// <summary>
        /// Members the viewer blocked (empty for a null viewer).
        /// </summary>
        /// <param name="viewerUserId">Viewer.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Member identifiers (at most <see cref="MaxBlocksPerMember"/>).</returns>
        public Task<HashSet<string>> BlockedByAsync(string? viewerUserId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(viewerUserId)) return Task.FromResult(new HashSet<string>(StringComparer.Ordinal));
            return _Database.Blocks.BlockedByAsync(viewerUserId, token);
        }

        /// <summary>
        /// Members who blocked the user (empty for a null user).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Member identifiers.</returns>
        public Task<HashSet<string>> BlockersOfAsync(string? userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) return Task.FromResult(new HashSet<string>(StringComparer.Ordinal));
            return _Database.Blocks.BlockersOfAsync(userId, token);
        }

        /// <summary>
        /// Members related to the user by a block in either direction (empty for a null user).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Member identifiers.</returns>
        public Task<HashSet<string>> RelatedAsync(string? userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) return Task.FromResult(new HashSet<string>(StringComparer.Ordinal));
            return _Database.Blocks.RelatedAsync(userId, token);
        }

        /// <summary>
        /// Of the candidates, those related to the user by a block in either direction (for fan-out filtering).
        /// </summary>
        /// <param name="userId">Member, or null (empty result).</param>
        /// <param name="candidateUserIds">Candidates.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Related candidates.</returns>
        /// <exception cref="ArgumentNullException">The candidates are null.</exception>
        public Task<HashSet<string>> RelatedAmongAsync(string? userId, IEnumerable<string> candidateUserIds, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(candidateUserIds);
            if (String.IsNullOrEmpty(userId)) return Task.FromResult(new HashSet<string>(StringComparer.Ordinal));
            return _Database.Blocks.RelatedAmongAsync(userId, candidateUserIds, token);
        }

        /// <summary>
        /// Authors whose content is hidden from the viewer: members the viewer blocked minus current staff (a blocked
        /// member who later became staff is shown again, contract section 9.2). Empty for a null viewer.
        /// </summary>
        /// <param name="viewerUserId">Viewer.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Author identifiers.</returns>
        public Task<HashSet<string>> HiddenAuthorsForAsync(string? viewerUserId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(viewerUserId)) return Task.FromResult(new HashSet<string>(StringComparer.Ordinal));
            return _Database.Blocks.HiddenAuthorsAsync(viewerUserId, token);
        }

        /// <summary>
        /// Remove every block a member made or received (account deletion, contract section 11).
        /// </summary>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows removed.</returns>
        /// <exception cref="ArgumentNullException">The user id is null or empty.</exception>
        public Task<int> DeleteAllForUserAsync(string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            return _Database.Blocks.DeleteByUserAsync(userId, token);
        }

        #endregion
    }
}
