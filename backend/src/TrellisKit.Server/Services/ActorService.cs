namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Security;
    using WatsonWebserver.Core;

    /// <summary>
    /// Resolves the <see cref="ContentActor"/> for a request: the member, roles, Phase 1 flags, Phase 2 posting
    /// permissions, mute, membership, and age attestation. Reads the database on every call so permission and role changes apply immediately.
    /// Thread safety: stateless apart from the shared driver; safe to call concurrently.
    /// </summary>
    public class ActorService
    {
        #region Public-Members

        /// <summary>
        /// Scheme of the request context the authentication hook builds for a signed-out caller on a route that
        /// anonymous browsing opens (docs/ACCESS_MODELS_CONTRACT.md section 3).
        /// </summary>
        public const string AnonymousScheme = "Anonymous";

        /// <summary>
        /// Supplies the property's current access configuration, stamped on every actor. Default FullyGated.
        /// </summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public Func<AccessPolicy> Access
        {
            get { return _Access; }
            set { _Access = value ?? throw new ArgumentNullException(nameof(Access)); }
        }

        /// <summary>
        /// Applied to every member and break-glass actor this service resolves (ApplicationServices sets it to
        /// <see cref="ContentTargetRegistry.Decorate"/> so provider moderation scopes are known), or null.
        /// </summary>
        public Action<ContentActor>? Decorator { get; set; } = null;

        #endregion

        #region Private-Members

        private Func<AccessPolicy> _Access = () => AccessPolicy.FullyGatedDefault;

        private readonly DatabaseDriverBase _Database;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <exception cref="ArgumentNullException">The database is null.</exception>
        public ActorService(DatabaseDriverBase database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolve the actor of an authenticated request.
        /// </summary>
        /// <param name="ctx">HTTP context whose metadata is the <see cref="RequestContext"/>.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Actor.</returns>
        /// <exception cref="ApiErrorException">401 Unauthorized when the request is not authenticated, 404 when the member no longer exists.</exception>
        public Task<ContentActor> ResolveAsync(HttpContextBase ctx, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            RequestContext? context = ctx.Metadata as RequestContext;
            if (IsAnonymous(context)) return Task.FromResult(Anonymous(ctx.Request.Source?.IpAddress));
            return ResolveAsync(context, token);
        }

        /// <summary>
        /// Whether a request context is the anonymous-browsing context built by the authentication hook.
        /// </summary>
        /// <param name="context">Request context, or null.</param>
        /// <returns>True when anonymous.</returns>
        public static bool IsAnonymous(RequestContext? context)
        {
            return context != null && !context.IsAuthenticated && String.Equals(context.Scheme, AnonymousScheme, StringComparison.Ordinal);
        }

        /// <summary>
        /// The actor of a signed-out caller: no member, no rights to write, react, or report.
        /// </summary>
        /// <param name="sourceIp">Source IP (rate limits), or null.</param>
        /// <returns>Actor.</returns>
        public ContentActor Anonymous(string? sourceIp)
        {
            return new ContentActor
            {
                IsAnonymous = true,
                SourceIp = sourceIp,
                CanPost = false,
                CanPostMedia = false,
                CanUploadVideo = false,
                Access = _Access()
            };
        }

        /// <summary>
        /// Resolve the actor of a request context.
        /// </summary>
        /// <param name="context">Request context, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Actor.</returns>
        /// <exception cref="ApiErrorException">401 Unauthorized when the context is not authenticated, 404 when the member no longer exists.</exception>
        public async Task<ContentActor> ResolveAsync(RequestContext? context, CancellationToken token = default)
        {
            if (IsAnonymous(context)) return Anonymous(null);
            if (context == null || !context.IsAuthenticated) throw new ApiErrorException(401, "Unauthorized", "A valid bearer token is required.");
            if (context.IsBreakGlass || String.IsNullOrEmpty(context.UserId))
            {
                ContentActor glass = new ContentActor { IsBreakGlass = true, IsSystemAdmin = true, IsStaff = true, CanPostToFeed = true, CanPromoteToFeed = true, AllForumsModerator = true, IsDirectoryModerator = true, IsEventManager = true, IsLibraryEditor = true, IsRankAwarder = true, HasMembership = true, AgeAttested = true, Access = _Access() };
                Decorator?.Invoke(glass);
                return glass;
            }
            return await ForUserAsync(context.UserId, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Resolve the actor for a member identifier (workers and services acting for a member).
        /// </summary>
        /// <param name="userId">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Actor.</returns>
        /// <exception cref="ArgumentNullException">The identifier is null or empty.</exception>
        /// <exception cref="ApiErrorException">404 NotFound when the member does not exist or is deleted.</exception>
        public async Task<ContentActor> ForUserAsync(string userId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            User? user = await _Database.Users.ReadAsync(userId, token).ConfigureAwait(false);
            if (user == null || user.IsDeleted) throw ApiErrorException.NotFound("Member");
            List<RoleAssignment> roles = await _Database.RoleAssignments.EnumerateByUserAsync(userId, token).ConfigureAwait(false);
            UserPermission permission = await _Database.UserPermissions.ReadAsync(userId, token).ConfigureAwait(false);
            List<Entitlement> entitlements = await _Database.Entitlements.EnumerateByUserAsync(userId, token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            bool isAdmin = roles.Any(r => r.Role == RoleEnum.SystemAdmin);
            bool muted = !isAdmin && user.IsMutedAt(now);
            ContentActor actor = new ContentActor
            {
                UserId = user.Id,
                User = user,
                IsSystemAdmin = isAdmin,
                IsFeedModerator = roles.Any(IsFeedModeratorRole),
                IsDirectoryModerator = roles.Any(r => r.Role == RoleEnum.DirectoryModerator),
                IsEventManager = roles.Any(r => r.Role == RoleEnum.EventManager),
                IsLibraryEditor = roles.Any(r => r.Role == RoleEnum.LibraryEditor),
                IsRankAwarder = roles.Any(r => r.Role == RoleEnum.RankAwarder),
                IsStaff = roles.Any(r => UserSummaryService.IsStaffRole(r.Role)),
                IsSupportAgent = roles.Any(r => r.Role == RoleEnum.SupportAgent),
                IsCommunicator = roles.Any(r => r.Role == RoleEnum.Communicator),
                IsReleaseManager = roles.Any(r => r.Role == RoleEnum.ReleaseManager),
                IsMessagingModerator = roles.Any(r => r.Role == RoleEnum.Moderator && (r.ResourceType == ResourceTypeEnum.Messaging || r.ResourceType == ResourceTypeEnum.Global)),
                CanPostToFeed = user.CanPostToFeed,
                CanPromoteToFeed = user.CanPromoteToFeed,
                AllForumsModerator = roles.Any(IsAllForumsModeratorRole),
                ModeratedForumIds = new HashSet<string>(roles.Where(r => r.Role == RoleEnum.Moderator && r.ResourceType == ResourceTypeEnum.Forum && r.ResourceId != "*").Select(r => r.ResourceId), StringComparer.Ordinal),
                CanPost = permission.CanPost,
                CanPostMedia = permission.CanPostMedia,
                CanUploadVideo = user.CanUploadVideo,
                IsMuted = muted,
                MutedUntilUtc = muted ? user.MutedUntilUtc : null,
                HasMembership = entitlements.Any(e => e.IsActiveAt(now)),
                AgeAttested = user.AgeAttestedUtc != null,
                Access = _Access()
            };
            Decorator?.Invoke(actor);
            return actor;
        }

        /// <summary>
        /// Whether a role assignment covers every forum (Moderator with resource type Global, or Forum with *).
        /// </summary>
        /// <param name="role">Assignment.</param>
        /// <returns>True for an all-forums moderator assignment.</returns>
        public static bool IsAllForumsModeratorRole(RoleAssignment role)
        {
            if (role == null) return false;
            return role.Role == RoleEnum.Moderator && (role.ResourceType == ResourceTypeEnum.Global || (role.ResourceType == ResourceTypeEnum.Forum && role.ResourceId == "*"));
        }

        /// <summary>
        /// Whether a role assignment lets its holder moderate the Feed (FeedModerator, or Moderator scoped Global or Feed).
        /// </summary>
        /// <param name="role">Role assignment.</param>
        /// <returns>True when it does.</returns>
        public static bool IsFeedModeratorRole(RoleAssignment role)
        {
            if (role == null) return false;
            if (role.Role == RoleEnum.FeedModerator) return true;
            return role.Role == RoleEnum.Moderator && (role.ResourceType == ResourceTypeEnum.Global || role.ResourceType == ResourceTypeEnum.Feed);
        }

        #endregion
    }
}
