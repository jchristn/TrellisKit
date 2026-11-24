namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Moderators admin (contract section 10.4, Admin group): the moderators enumeration with 30-day statistics, and
    /// grant and revoke of FeedModerator (scope Feed). Both changes are audited as member.roles and invalidate the
    /// member's cached sessions, so they take effect on the member's next request. Shortcuts over
    /// PUT /api/v1/admin/members/{id}/roles, which keeps working.
    /// Thread safety: safe to call concurrently; grant is idempotent (unique role assignment).
    /// </summary>
    public class ModeratorService
    {
        #region Public-Members

        /// <summary>
        /// Days of moderator statistics.
        /// </summary>
        public const int StatsDays = 30;

        /// <summary>
        /// Most recent resolutions per moderator used for the median response time. Default 1000, minimum 1, maximum
        /// 10000.
        /// </summary>
        public int MedianSamples
        {
            get { return _MedianSamples; }
            set { _MedianSamples = Math.Clamp(value, 1, 10000); }
        }

        #endregion

        #region Private-Members

        private readonly ApplicationServices _Services;
        private int _MedianSamples = 1000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="services">Application services (read on each call).</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public ModeratorService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// GET /api/v1/admin/moderators (ModerationCollections.Moderators).
        /// </summary>
        /// <param name="actor">Caller (SystemAdmin).</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page with exact totals.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 403 Forbidden.</exception>
        public async Task<EnumerationResult<ModeratorListItem>> EnumerateAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            RequireAdmin(actor);
            EnumerationQuery q = query ?? new EnumerationQuery();
            Dictionary<string, string> errors = new Dictionary<string, string>();
            ModeratorFilter filter = new ModeratorFilter { ResourceType = q.FilterEnum<ResourceTypeEnum>("resourceType", errors) };
            string? role = q.FilterString("role");
            if (role != null)
            {
                if (ModerationService.TryParseEnum(role, out RoleEnum parsed) && IsStaffGrant(parsed)) filter.Role = parsed;
                else errors["filters.role"] = "Use Moderator, FeedModerator, DirectoryModerator, EventManager, or LibraryEditor.";
            }
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, ModerationCollections.Moderators, true, "moderators");
            EnumerationResult<string> page = await _Services.Database.ModerationActions.EnumerateModeratorsAsync(request, filter, token).ConfigureAwait(false);
            return page.With(await ItemsAsync(page.Objects, token).ConfigureAwait(false));
        }

        /// <summary>
        /// POST /api/v1/admin/moderators: grant FeedModerator for the Feed scope (resourceId *). Idempotent.
        /// </summary>
        /// <param name="actor">Caller (SystemAdmin).</param>
        /// <param name="info">Request information (audit).</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The moderator row.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound.</exception>
        public async Task<ModeratorListItem> GrantAsync(ContentActor actor, RequestInfo info, ModeratorGrantRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            RequireAdmin(actor);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string userId = (request.UserId ?? String.Empty).Trim();
            if (userId.Length == 0) errors["userId"] = "Required.";
            ResourceTypeEnum scope = ParseScope(request.ResourceType, errors, "resourceType");
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            User user = await RequireMemberAsync(userId, token).ConfigureAwait(false);
            if (scope == ResourceTypeEnum.Forum) return await GrantForumAsync(actor, info, user, request.ResourceId, token).ConfigureAwait(false);
            using (Activity? activity = TrellisKitTelemetry.StartSpan("moderators.grant"))
            {
                bool added = await _Services.Database.RoleAssignments.AddAsync(new RoleAssignment
                {
                    Id = IdGenerator.GenerateRoleAssignmentId(),
                    UserId = user.Id,
                    Role = RoleForScope(scope),
                    ResourceType = scope,
                    ResourceId = "*",
                    GrantedByUserId = actor.UserId,
                    CreatedUtc = DateTime.UtcNow
                }, token).ConfigureAwait(false);
                if (added)
                {
                    _Services.Tokens.InvalidateUser(user.Id);
                    await _Services.Audit.WriteAsync(info, "member.roles", "User", user.Id, "Granted " + RoleForScope(scope) + " (" + scope + ":*)", token).ConfigureAwait(false);
                }
                activity?.SetStatus(ActivityStatusCode.Ok);
            }
            return (await ItemsAsync(new List<string> { user.Id }, token).ConfigureAwait(false))[0];
        }

        /// <summary>
        /// DELETE /api/v1/admin/moderators/{userId}?resourceType=Feed: revoke FeedModerator for the scope. Idempotent;
        /// the member's past actions stay attributed to them.
        /// </summary>
        /// <param name="actor">Caller (SystemAdmin).</param>
        /// <param name="info">Request information (audit).</param>
        /// <param name="userId">Member.</param>
        /// <param name="resourceType">Scope (Feed, the default).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound.</exception>
        public async Task RevokeAsync(ContentActor actor, RequestInfo info, string userId, string? resourceType, CancellationToken token = default)
        {
            await RevokeAsync(actor, info, userId, resourceType, null, token).ConfigureAwait(false);
        }

        /// <summary>
        /// DELETE /api/v1/admin/moderators/{userId}?resourceType=Feed|Forum&amp;resourceId=: revoke FeedModerator for the
        /// Feed scope, or the forum Moderator grant for resourceId (a forum identifier, or * for the all-forums grant).
        /// Idempotent; past actions stay attributed.
        /// </summary>
        /// <param name="actor">Caller (SystemAdmin).</param>
        /// <param name="info">Request information (audit).</param>
        /// <param name="userId">Member.</param>
        /// <param name="resourceType">Scope (Feed, the default, or Forum).</param>
        /// <param name="resourceId">Forum identifier or * (Forum scope only; default *).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound.</exception>
        public async Task RevokeAsync(ContentActor actor, RequestInfo info, string userId, string? resourceType, string? resourceId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            RequireAdmin(actor);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            ResourceTypeEnum scope = ParseScope(resourceType, errors, "resourceType");
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            User user = await RequireMemberAsync((userId ?? String.Empty).Trim(), token).ConfigureAwait(false);
            if (scope == ResourceTypeEnum.Forum)
            {
                string forumId = String.IsNullOrWhiteSpace(resourceId) ? "*" : resourceId.Trim();
                List<RoleAssignment> held = await _Services.Database.RoleAssignments.EnumerateByUserAsync(user.Id, token).ConfigureAwait(false);
                List<RoleAssignment> remaining = held.Where(r => !(r.Role == RoleEnum.Moderator && r.ResourceType == ResourceTypeEnum.Forum && String.Equals(r.ResourceId, forumId, StringComparison.Ordinal))).ToList();
                if (remaining.Count != held.Count)
                {
                    await _Services.Database.RoleAssignments.ReplaceForUserAsync(user.Id, remaining, token).ConfigureAwait(false);
                    _Services.Tokens.InvalidateUser(user.Id);
                    await _Services.Audit.WriteAsync(info, "member.roles", "User", user.Id, "Revoked Moderator (Forum:" + forumId + ")", token).ConfigureAwait(false);
                }
                return;
            }
            using (Activity? activity = TrellisKitTelemetry.StartSpan("moderators.revoke"))
            {
                List<RoleAssignment> current = await _Services.Database.RoleAssignments.EnumerateByUserAsync(user.Id, token).ConfigureAwait(false);
                RoleEnum revoked = RoleForScope(scope);
                List<RoleAssignment> keep = current.Where(r => !(r.Role == revoked && r.ResourceType == scope)).ToList();
                if (keep.Count != current.Count)
                {
                    await _Services.Database.RoleAssignments.ReplaceForUserAsync(user.Id, keep, token).ConfigureAwait(false);
                    _Services.Tokens.InvalidateUser(user.Id);
                    await _Services.Audit.WriteAsync(info, "member.roles", "User", user.Id, "Revoked " + revoked + " (" + scope + ")", token).ConfigureAwait(false);
                }
                activity?.SetStatus(ActivityStatusCode.Ok);
            }
        }

        #endregion

        #region Private-Methods

        private static void RequireAdmin(ContentActor actor)
        {
            if (!actor.IsSystemAdmin) throw ApiErrorException.Forbidden("Admin access is required.");
        }

        private static ResourceTypeEnum ParseScope(string? raw, Dictionary<string, string> errors, string field)
        {
            if (String.IsNullOrWhiteSpace(raw)) return ResourceTypeEnum.Feed;
            if (ModerationService.TryParseEnum(raw, out ResourceTypeEnum scope) && scope != ResourceTypeEnum.Global) return scope;
            errors[field] = "Use Feed, Forum, Directory, Events, or Library.";
            return ResourceTypeEnum.Feed;
        }

        /// <summary>
        /// The role a grant on a scope gives: FeedModerator (Feed), DirectoryModerator (Directory), EventManager
        /// (Events), LibraryEditor (Library), and Moderator (Forum).
        /// </summary>
        /// <param name="scope">Scope.</param>
        /// <returns>Role.</returns>
        public static RoleEnum RoleForScope(ResourceTypeEnum scope)
        {
            switch (scope)
            {
                case ResourceTypeEnum.Directory:
                    return RoleEnum.DirectoryModerator;
                case ResourceTypeEnum.Events:
                    return RoleEnum.EventManager;
                case ResourceTypeEnum.Library:
                    return RoleEnum.LibraryEditor;
                case ResourceTypeEnum.Forum:
                    return RoleEnum.Moderator;
                default:
                    return RoleEnum.FeedModerator;
            }
        }

        /// <summary>
        /// Whether a role is granted through the moderators routes.
        /// </summary>
        /// <param name="role">Role.</param>
        /// <returns>True for Moderator, FeedModerator, DirectoryModerator, EventManager, and LibraryEditor.</returns>
        public static bool IsStaffGrant(RoleEnum role)
        {
            return role == RoleEnum.Moderator || role == RoleEnum.FeedModerator || role == RoleEnum.DirectoryModerator || role == RoleEnum.EventManager || role == RoleEnum.LibraryEditor;
        }

        private async Task<ForumModeratorView> GrantForumAsync(ContentActor actor, RequestInfo info, User user, string? resourceId, CancellationToken token)
        {
            string forumId = String.IsNullOrWhiteSpace(resourceId) ? "*" : resourceId.Trim();
            if (forumId != "*")
            {
                Forum? forum = await _Services.Database.Forums.ReadAsync(forumId, token).ConfigureAwait(false);
                if (forum == null) throw ApiErrorException.NotFound("Forum");
            }
            using (Activity? activity = TrellisKitTelemetry.StartSpan("moderators.grant"))
            {
                bool added = await _Services.Database.RoleAssignments.AddAsync(new RoleAssignment
                {
                    Id = IdGenerator.GenerateRoleAssignmentId(),
                    UserId = user.Id,
                    Role = RoleEnum.Moderator,
                    ResourceType = ResourceTypeEnum.Forum,
                    ResourceId = forumId,
                    GrantedByUserId = actor.UserId,
                    CreatedUtc = DateTime.UtcNow
                }, token).ConfigureAwait(false);
                if (added)
                {
                    _Services.Tokens.InvalidateUser(user.Id);
                    await _Services.Audit.WriteAsync(info, "member.roles", "User", user.Id, "Granted Moderator (Forum:" + forumId + ")", token).ConfigureAwait(false);
                }
                activity?.SetStatus(ActivityStatusCode.Ok);
            }
            ModeratorListItem item = (await ItemsAsync(new List<string> { user.Id }, token).ConfigureAwait(false))[0];
            return new ForumModeratorView
            {
                User = item.User,
                Roles = item.Roles,
                GrantedUtc = item.GrantedUtc,
                GrantedByName = item.GrantedByName,
                OpenInScope = item.OpenInScope,
                Actions30d = item.Actions30d,
                MedianResponseMinutes30d = item.MedianResponseMinutes30d,
                OverdueHandled30d = item.OverdueHandled30d,
                Labels = item.Labels,
                Tags = item.Tags,
                Scope = forumId == "*" ? "AllForums" : "Forum",
                ForumId = forumId == "*" ? null : forumId
            };
        }

        private async Task<User> RequireMemberAsync(string userId, CancellationToken token)
        {
            User? user = String.IsNullOrEmpty(userId) ? null : await _Services.Database.Users.ReadAsync(userId, token).ConfigureAwait(false);
            if (user == null || user.IsDeleted) throw ApiErrorException.NotFound("Member");
            return user;
        }

        private async Task<List<ModeratorListItem>> ItemsAsync(List<string> userIds, CancellationToken token)
        {
            if (userIds.Count == 0) return new List<ModeratorListItem>();
            DateTime now = DateTime.UtcNow;
            Dictionary<string, UserSummary> users = await _Services.Summaries.BuildAsync(userIds, token).ConfigureAwait(false);
            List<RoleAssignment> roles = await _Services.Database.RoleAssignments.EnumerateByUsersAsync(userIds, token).ConfigureAwait(false);
            Dictionary<string, ModeratorStats> stats = await _Services.Database.ModerationActions.ModeratorStatsAsync(userIds, now.AddDays(-StatsDays), _MedianSamples, token).ConfigureAwait(false);
            Dictionary<string, UserSummary> granters = await _Services.Summaries.BuildAsync(roles.Select(r => r.GrantedByUserId), token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.User, userIds, true, token).ConfigureAwait(false);
            long feedOpen = await _Services.Database.Reports.CountOpenAsync(new List<string> { ModerationService.FeedScope }, null, token).ConfigureAwait(false);
            List<ModeratorListItem> items = new List<ModeratorListItem>();
            foreach (string id in userIds)
            {
                List<RoleAssignment> mine = roles.Where(r => r.UserId == id && IsStaffGrant(r.Role)).OrderBy(r => r.CreatedUtc).ToList();
                RoleAssignment? first = mine.FirstOrDefault();
                bool feedScope = roles.Any(r => r.UserId == id && (r.Role == RoleEnum.SystemAdmin || ActorService.IsFeedModeratorRole(r)));
                ModeratorStats stat = stats.TryGetValue(id, out ModeratorStats? s) ? s : new ModeratorStats { UserId = id };
                items.Add(new ModeratorListItem
                {
                    User = users.TryGetValue(id, out UserSummary? u) ? u : UserSummary.Deleted(),
                    Roles = mine.Select(r => new RoleView { Role = r.Role, ResourceType = r.ResourceType, ResourceId = r.ResourceId }).ToList(),
                    GrantedUtc = first?.CreatedUtc,
                    GrantedByName = first == null ? null : (first.GrantedByUserId == null ? "admin token" : (granters.TryGetValue(first.GrantedByUserId, out UserSummary? g) ? g.DisplayName : UserSummary.Deleted().DisplayName)),
                    OpenInScope = feedScope ? feedOpen : 0,
                    Actions30d = stat.Actions,
                    MedianResponseMinutes30d = stat.MedianResponseMinutes,
                    OverdueHandled30d = stat.OverdueHandled,
                    Labels = metadata.TryGetValue(id, out ObjectMetadata? m) ? m.Labels : new List<string>(),
                    Tags = m != null ? m.Tags : new Dictionary<string, string>()
                });
            }
            return items;
        }

        #endregion
    }
}
