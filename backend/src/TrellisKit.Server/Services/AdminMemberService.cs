namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core.Database;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Admin member management: list and detail, flags, roles, comps, bans, mutes, and session revocation. Every change
    /// writes an audit record.
    /// Thread safety: safe to call concurrently.
    /// </summary>
    public class AdminMemberService
    {
        #region Public-Members

        /// <summary>
        /// Administrative email (comp, ban, and mute notices). Set by the composition root; null sends none.
        /// </summary>
        public EmailService? Email { get; set; } = null;

        /// <summary>
        /// Profile values (profile filters and card maps). Set by the composition root.
        /// </summary>
        public ProfileValueService? ProfileValues { get; set; } = null;

        /// <summary>
        /// Called after a member is banned (feature modules chain their effects, for example removing RSVPs on upcoming
        /// events and disabling calendar feeds). Arguments: member, cancellation token.
        /// </summary>
        public Func<string, CancellationToken, Task>? MemberBanned { get; set; } = null;

        /// <summary>
        /// Profile field definitions (the legacy rank filter). Set by the composition root.
        /// </summary>
        public ProfileFieldService? ProfileFields { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly MembershipService _Membership;
        private readonly MemberViewBuilder _Views;
        private readonly SessionService _Sessions;
        private readonly TokenService _Tokens;
        private readonly SignupCodeService _Codes;
        private readonly AuditService _Audit;
        private readonly PropertyContext _Property;
        private readonly EnumerationService _Enumeration;
        private readonly MetadataService _Metadata;
        private const int _MaxRoleAssignments = 20;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the service.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="membership">Membership service.</param>
        /// <param name="views">View builder.</param>
        /// <param name="sessions">Session service.</param>
        /// <param name="tokens">Token service.</param>
        /// <param name="codes">Sign-up code service.</param>
        /// <param name="audit">Audit service.</param>
        /// <param name="property">Property context (rank filter values).</param>
        /// <param name="enumeration">Enumeration service.</param>
        /// <param name="metadata">Labels and tags.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public AdminMemberService(DatabaseDriverBase database, MembershipService membership, MemberViewBuilder views, SessionService sessions, TokenService tokens,
            SignupCodeService codes, AuditService audit, PropertyContext property, EnumerationService enumeration, MetadataService metadata)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Membership = membership ?? throw new ArgumentNullException(nameof(membership));
            _Views = views ?? throw new ArgumentNullException(nameof(views));
            _Sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            _Tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            _Codes = codes ?? throw new ArgumentNullException(nameof(codes));
            _Audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _Property = property ?? throw new ArgumentNullException(nameof(property));
            _Enumeration = enumeration ?? throw new ArgumentNullException(nameof(enumeration));
            _Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// GET /api/v1/admin/members (collection <see cref="PhaseOneCollections.AdminMembers"/>).
        /// </summary>
        /// <param name="query">Enumeration query; filters membership, rank, role, banned.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of members with labels and tags (staff view).</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken.</exception>
        public async Task<EnumerationResult<AdminMemberListItem>> ListAsync(EnumerationQuery query, CancellationToken token = default)
        {
            EnumerationRequest request = _Enumeration.Prepare(query, PhaseOneCollections.AdminMembers, true);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            UserFilter filter = new UserFilter
            {
                Membership = request.Query.FilterEnum<MembershipFilterEnum>("membership", errors),
                Role = request.Query.FilterEnum<RoleEnum>("role", errors),
                Banned = request.Query.FilterBool("banned", errors)
            };
            string? legacyRank = request.Query.FilterString("rank");
            if (ProfileValues != null)
            {
                JsonElement? profile = request.Query.Filters.TryGetValue("profile", out JsonElement element) ? element : null;
                filter.Profile = await ProfileValues.CompileFiltersAsync(profile, ProfileViewerEnum.Staff, errors, token).ConfigureAwait(false);
                ProfileField? rankField = ProfileFields?.LegacyField("rank", ProfileViewerEnum.Staff);
                if (legacyRank != null && rankField != null)
                {
                    // The legacy admin filter rank=<key> maps to filters.profile.rank.in = [<key>] (section 9.12).
                    ProfileFieldOption? option = rankField.Options.FirstOrDefault(o => String.Equals(o.Key, legacyRank.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (option == null) errors["filters.rank"] = "Use one of " + String.Join(", ", rankField.Options.Select(o => o.Key)) + ".";
                    else filter.Profile.Add(new ProfileFilterClause { FieldId = rankField.Id, FieldType = ProfileFieldTypeEnum.Rank, Operator = ProfileFilterOperatorEnum.In, Values = new List<string> { option.Key } });
                }
                else if (legacyRank != null) filter.Rank = Rank(legacyRank, errors);
            }
            else filter.Rank = Rank(legacyRank, errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors, "One or more filters are invalid.");
            DateTime now = DateTime.UtcNow;
            EnumerationResult<User> page = await _Database.Users.EnumerateAsync(request, filter, now, token).ConfigureAwait(false);
            List<string> ids = page.Objects.Select(u => u.Id).ToList();
            List<Entitlement> entitlements = await _Database.Entitlements.EnumerateByUsersAsync(ids, token).ConfigureAwait(false);
            List<RoleAssignment> roles = await _Database.RoleAssignments.EnumerateByUsersAsync(ids, token).ConfigureAwait(false);

            List<AdminMemberListItem> items = new List<AdminMemberListItem>();
            foreach (User user in page.Objects)
            {
                List<Entitlement> mine = entitlements.Where(e => e.UserId == user.Id).ToList();
                List<Subscription> subscriptions = mine.Any(e => e.Source == MembershipSourceEnum.Subscription)
                    ? await _Database.Subscriptions.EnumerateByUserAsync(user.Id, token).ConfigureAwait(false)
                    : new List<Subscription>();
                items.Add(new AdminMemberListItem
                {
                    Id = user.Id,
                    DisplayName = user.DisplayName,
                    Handle = user.Handle,
                    Email = user.Email,
                    AvatarUrl = MemberViewBuilder.AvatarUrl(user),
                    Rank = user.Rank,
                    RankLevel = user.RankLevel,
                    Membership = MembershipService.Compute(mine, subscriptions, now),
                    Roles = roles.Where(r => r.UserId == user.Id).Select(MemberViewBuilder.ToView).ToList(),
                    IsBanned = user.IsBanned,
                    IsMuted = user.IsMutedAt(now),
                    CreatedUtc = user.CreatedUtc,
                    LastSeenUtc = user.LastSeenUtc
                });
            }
            if (ProfileValues != null)
            {
                Dictionary<string, Dictionary<string, object?>> cards = await ProfileValues.CardsAsync(ids, ProfileViewerEnum.Staff, token).ConfigureAwait(false);
                foreach (AdminMemberListItem item in items) item.Profile = cards.TryGetValue(item.Id, out Dictionary<string, object?>? card) ? card : item.Profile;
                if (ProfileFields?.LegacyField("rank", ProfileViewerEnum.Staff) == null) foreach (AdminMemberListItem item in items) { item.Rank = null; item.RankLevel = 0; }
            }
            await _Metadata.AttachAsync(TargetTypeEnum.User, items, m => m.Id, true, token).ConfigureAwait(false);
            return page.With(items);
        }

        /// <summary>
        /// GET /api/v1/admin/members/{id}: Me fields (with staff labels and tags), admin fields, and bounded previews of
        /// subscriptions, redemptions, comps, sessions, and membership history (at most 5 each, with totals and the
        /// enumeration paths).
        /// </summary>
        /// <param name="id">Member identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Detail.</returns>
        /// <exception cref="ApiErrorException">404.</exception>
        public async Task<AdminMemberDetail> DetailAsync(string id, CancellationToken token = default)
        {
            User user = await RequireAsync(id, token).ConfigureAwait(false);
            MembershipState state = await _Membership.GetStateAsync(user.Id, token).ConfigureAwait(false);
            AdminMemberDetail detail = new AdminMemberDetail();
            await _Views.FillMeAsync(detail, user, state, token).ConfigureAwait(false);
            await _Metadata.AttachOneAsync(TargetTypeEnum.User, detail, user.Id, true, token).ConfigureAwait(false);
            detail.IsBanned = user.IsBanned;
            detail.BannedReason = user.BannedReason;
            detail.IsDeleted = user.IsDeleted;
            detail.LastSeenUtc = user.LastSeenUtc;
            string path = PathFor(user.Id);
            detail.Subscriptions = BoundedPreview<AdminSubscriptionItem>.From(await SubscriptionsPageAsync(user, Preview(PhaseOneCollections.Subscriptions, "subscriptions", user.Id), token).ConfigureAwait(false), path + "/subscriptions/enumerate");
            detail.Redemptions = BoundedPreview<RedemptionView>.From(await RedemptionsPageAsync(user, Preview(PhaseOneCollections.Redemptions, "redemptions", user.Id), token).ConfigureAwait(false), path + "/redemptions/enumerate");
            detail.Comps = BoundedPreview<CompView>.From(await CompsPageAsync(user, Preview(PhaseOneCollections.AdminEntitlements, "comps", user.Id), token).ConfigureAwait(false), path + "/comps/enumerate");
            detail.Sessions = BoundedPreview<SessionView>.From(await SessionsPageAsync(user, Preview(PhaseOneCollections.Sessions, "sessions", user.Id), token).ConfigureAwait(false), path + "/sessions/enumerate");
            detail.MembershipHistory = BoundedPreview<MembershipHistoryItem>.From(await HistoryPageAsync(user, Preview(PhaseOneCollections.AdminEntitlements, "membership-history", user.Id), token).ConfigureAwait(false), path + "/membership-history/enumerate");
            return detail;
        }

        /// <summary>
        /// POST /api/v1/admin/members/{id}/subscriptions/enumerate (collection <see cref="PhaseOneCollections.Subscriptions"/>
        /// scoped to the member; filters store, status).
        /// </summary>
        /// <param name="id">Member identifier.</param>
        /// <param name="query">Enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 404 NotFound.</exception>
        public async Task<EnumerationResult<AdminSubscriptionItem>> EnumerateSubscriptionsAsync(string id, EnumerationQuery query, CancellationToken token = default)
        {
            User user = await RequireAsync(id, token).ConfigureAwait(false);
            return await SubscriptionsPageAsync(user, _Enumeration.Prepare(query, PhaseOneCollections.Subscriptions, true, "subscriptions|" + user.Id), token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/members/{id}/redemptions/enumerate (collection <see cref="PhaseOneCollections.Redemptions"/>
        /// scoped to the member; filter status).
        /// </summary>
        /// <param name="id">Member identifier.</param>
        /// <param name="query">Enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 404 NotFound.</exception>
        public async Task<EnumerationResult<RedemptionView>> EnumerateRedemptionsAsync(string id, EnumerationQuery query, CancellationToken token = default)
        {
            User user = await RequireAsync(id, token).ConfigureAwait(false);
            return await RedemptionsPageAsync(user, _Enumeration.Prepare(query, PhaseOneCollections.Redemptions, true, "redemptions|" + user.Id), token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/members/{id}/comps/enumerate (collection <see cref="PhaseOneCollections.AdminEntitlements"/>
        /// restricted to AdminComp entitlements; filter status).
        /// </summary>
        /// <param name="id">Member identifier.</param>
        /// <param name="query">Enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 404 NotFound.</exception>
        public async Task<EnumerationResult<CompView>> EnumerateCompsAsync(string id, EnumerationQuery query, CancellationToken token = default)
        {
            User user = await RequireAsync(id, token).ConfigureAwait(false);
            return await CompsPageAsync(user, _Enumeration.Prepare(query, PhaseOneCollections.AdminEntitlements, true, "comps|" + user.Id), token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/members/{id}/sessions/enumerate (collection <see cref="PhaseOneCollections.Sessions"/>;
        /// active sessions unless includeInactive is true).
        /// </summary>
        /// <param name="id">Member identifier.</param>
        /// <param name="query">Enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 404 NotFound.</exception>
        public async Task<EnumerationResult<SessionView>> EnumerateSessionsAsync(string id, EnumerationQuery query, CancellationToken token = default)
        {
            User user = await RequireAsync(id, token).ConfigureAwait(false);
            return await SessionsPageAsync(user, _Enumeration.Prepare(query, PhaseOneCollections.Sessions, true, "sessions|" + user.Id), token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/members/{id}/membership-history/enumerate (collection
        /// <see cref="PhaseOneCollections.AdminEntitlements"/>; filters source, status).
        /// </summary>
        /// <param name="id">Member identifier.</param>
        /// <param name="query">Enumeration query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or InvalidContinuationToken, 404 NotFound.</exception>
        public async Task<EnumerationResult<MembershipHistoryItem>> EnumerateMembershipHistoryAsync(string id, EnumerationQuery query, CancellationToken token = default)
        {
            User user = await RequireAsync(id, token).ConfigureAwait(false);
            return await HistoryPageAsync(user, _Enumeration.Prepare(query, PhaseOneCollections.AdminEntitlements, true, "membership-history|" + user.Id), token).ConfigureAwait(false);
        }

        /// <summary>
        /// Change permission flags. Only present values change.
        /// </summary>
        /// <param name="id">Member identifier.</param>
        /// <param name="request">Body.</param>
        /// <param name="info">Request info.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Detail.</returns>
        public async Task<AdminMemberDetail> UpdateFlagsAsync(string id, MemberFlagsRequest request, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            User user = await RequireAsync(id, token).ConfigureAwait(false);
            List<string> changes = new List<string>();
            if (request.CanPostToFeed != null) { user.CanPostToFeed = request.CanPostToFeed.Value; changes.Add("canPostToFeed=" + Lower(user.CanPostToFeed)); }
            if (request.CanPromoteToFeed != null) { user.CanPromoteToFeed = request.CanPromoteToFeed.Value; changes.Add("canPromoteToFeed=" + Lower(user.CanPromoteToFeed)); }
            if (request.CanUploadVideo != null) { user.CanUploadVideo = request.CanUploadVideo.Value; changes.Add("canUploadVideo=" + Lower(user.CanUploadVideo)); }
            if (request.CanSendDirectMessages != null) { user.CanSendDirectMessages = request.CanSendDirectMessages.Value; changes.Add("canSendDirectMessages=" + Lower(user.CanSendDirectMessages)); }
            bool userChanged = changes.Count > 0;
            UserPermission permission = await _Database.UserPermissions.ReadAsync(user.Id, token).ConfigureAwait(false);
            bool permissionChanged = false;
            if (request.CanPost != null) { permission.CanPost = request.CanPost.Value; permissionChanged = true; changes.Add("canPost=" + Lower(permission.CanPost)); }
            if (request.CanPostMedia != null) { permission.CanPostMedia = request.CanPostMedia.Value; permissionChanged = true; changes.Add("canPostMedia=" + Lower(permission.CanPostMedia)); }
            if (userChanged)
            {
                user.UpdatedUtc = DateTime.UtcNow;
                await _Database.Users.UpdateAsync(user, token).ConfigureAwait(false);
            }
            if (permissionChanged)
            {
                permission.UpdatedUtc = DateTime.UtcNow;
                permission.UpdatedByUserId = info.Context?.UserId;
                await _Database.UserPermissions.UpsertAsync(permission, token).ConfigureAwait(false);
            }
            if (changes.Count > 0) await _Audit.WriteAsync(info, "member.flags", "User", user.Id, String.Join(", ", changes), token).ConfigureAwait(false);
            // canSendDirectMessages changes the member's messaging composer: live sockets re-evaluate (access.changed).
            if (request.CanSendDirectMessages != null) _Tokens.InvalidateUser(user.Id);
            return await DetailAsync(user.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Replace a member's explicit roles.
        /// </summary>
        /// <param name="id">Member identifier.</param>
        /// <param name="request">Body.</param>
        /// <param name="info">Request info.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Detail.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or 409 CannotRemoveOwnAdmin.</exception>
        public async Task<AdminMemberDetail> ReplaceRolesAsync(string id, MemberRolesRequest request, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            User user = await RequireAsync(id, token).ConfigureAwait(false);
            if (request.Roles == null) throw ApiErrorException.Field("roles", "A roles list is required (use [] to remove every role).");
            if (request.Roles.Count > _MaxRoleAssignments) throw ApiErrorException.Field("roles", "At most " + _MaxRoleAssignments + " role assignments per member.");
            Dictionary<string, string> errors = new Dictionary<string, string>();
            List<RoleAssignment> assignments = new List<RoleAssignment>();
            DateTime now = DateTime.UtcNow;
            for (int i = 0; i < request.Roles.Count; i++)
            {
                RoleAssignmentRequest r = request.Roles[i] ?? new RoleAssignmentRequest();
                string prefix = "roles[" + i + "].";
                if (!TryParse(r.Role, out RoleEnum role) || role == RoleEnum.User) { errors[prefix + "role"] = "Use SystemAdmin, Moderator, FeedModerator, DirectoryModerator, EventManager, LibraryEditor, SupportAgent, Communicator, ReleaseManager, or RankAwarder."; continue; }
                ResourceTypeEnum resourceType = DefaultResourceType(role);
                if (!String.IsNullOrWhiteSpace(r.ResourceType) && !TryParse(r.ResourceType, out resourceType)) { errors[prefix + "resourceType"] = "Use Global, Forum, Feed, Directory, Events, Library, Messaging, or Ranks."; continue; }
                string resourceId = String.IsNullOrWhiteSpace(r.ResourceId) ? "*" : r.ResourceId.Trim();
                if (resourceId.Length > 100) { errors[prefix + "resourceId"] = "Use at most 100 characters."; continue; }
                assignments.Add(new RoleAssignment
                {
                    Id = IdGenerator.GenerateRoleAssignmentId(),
                    UserId = user.Id,
                    Role = role,
                    ResourceType = resourceType,
                    ResourceId = resourceId,
                    GrantedByUserId = info.Context?.UserId,
                    CreatedUtc = now
                });
            }
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);

            if (info.Context?.UserId == user.Id && !assignments.Any(a => a.Role == RoleEnum.SystemAdmin))
            {
                List<RoleAssignment> current = await _Database.RoleAssignments.EnumerateByUserAsync(user.Id, token).ConfigureAwait(false);
                if (current.Any(a => a.Role == RoleEnum.SystemAdmin)) throw new ApiErrorException(409, "CannotRemoveOwnAdmin", "You cannot remove your own SystemAdmin role.");
            }

            await _Database.RoleAssignments.ReplaceForUserAsync(user.Id, assignments, token).ConfigureAwait(false);
            _Tokens.InvalidateUser(user.Id);
            string summary = assignments.Count == 0 ? "No roles" : String.Join(", ", assignments.Select(a => a.Role + "(" + a.ResourceType + ":" + a.ResourceId + ")"));
            await _Audit.WriteAsync(info, "member.roles", "User", user.Id, summary, token).ConfigureAwait(false);
            return await DetailAsync(user.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Grant a free membership.
        /// </summary>
        /// <param name="id">Member identifier.</param>
        /// <param name="request">Body.</param>
        /// <param name="info">Request info.</param>
        /// <param name="actor">Caller (for the comp's optional labels and tags).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Detail.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or 404.</exception>
        public async Task<AdminMemberDetail> GrantCompAsync(string id, CompGrantRequest request, RequestInfo info, ContentActor actor, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            User user = await RequireAsync(id, token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            Dictionary<string, string> errors = new Dictionary<string, string>();
            DateTime? ends = null;
            if (!TryParse(request.GrantType, out GrantTypeEnum grantType) || grantType == GrantTypeEnum.Discount) errors["grantType"] = "Use DurationMonths, FixedEndDate, or Lifetime.";
            else if (grantType == GrantTypeEnum.DurationMonths)
            {
                if (request.Months == null || request.Months.Value < 1 || request.Months.Value > 120) errors["months"] = "Use 1 to 120.";
                else ends = now.AddMonths(request.Months.Value);
            }
            else if (grantType == GrantTypeEnum.FixedEndDate)
            {
                if (request.EndUtc == null || request.EndUtc.Value.ToUniversalTime() <= now) errors["endUtc"] = "Choose a date in the future.";
                else ends = request.EndUtc.Value.ToUniversalTime();
            }
            string? reason = Reason(request.Reason, errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            ObjectMetadata metadata = await _Metadata.PrepareNewAsync(actor, TargetTypeEnum.Entitlement, request.Labels, request.Tags, token).ConfigureAwait(false);

            string entitlementId = IdGenerator.GenerateEntitlementId();
            await _Database.Entitlements.CreateAsync(new Entitlement
            {
                Id = entitlementId,
                UserId = user.Id,
                Source = MembershipSourceEnum.AdminComp,
                SourceId = entitlementId,
                StartsUtc = now,
                EndsUtc = ends,
                Reason = reason,
                GrantType = grantType,
                GrantedByUserId = info.Context?.UserId,
                CreatedUtc = now,
                UpdatedUtc = now
            }, token).ConfigureAwait(false);
            if (metadata.Labels.Count > 0 || metadata.Tags.Count > 0) await _Database.Metadata.WriteAsync(TargetTypeEnum.Entitlement, entitlementId, metadata, token).ConfigureAwait(false);
            _Tokens.InvalidateUser(user.Id);
            await _Audit.WriteAsync(info, "member.comp", "User", user.Id, "Granted " + SignupCodeService.GrantSummary(grantType, request.Months, ends).ToLowerInvariant() + ": " + reason, token).ConfigureAwait(false);
            if (Email != null) await Email.SendToMemberAsync("free_membership_granted", user, null, token, ("grantSummary", SignupCodeService.GrantSummary(grantType, request.Months, ends)), ("link", Email.Link("/app"))).ConfigureAwait(false);
            return await DetailAsync(user.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// End a comp early.
        /// </summary>
        /// <param name="id">Member identifier.</param>
        /// <param name="compId">Comp (entitlement) identifier.</param>
        /// <param name="reason">Reason (required).</param>
        /// <param name="info">Request info.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Detail.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or 404.</exception>
        public async Task<AdminMemberDetail> EndCompAsync(string id, string compId, string? reason, RequestInfo info, CancellationToken token = default)
        {
            User user = await RequireAsync(id, token).ConfigureAwait(false);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string? why = Reason(reason, errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            Entitlement? comp = String.IsNullOrWhiteSpace(compId) ? null : await _Database.Entitlements.ReadAsync(compId, token).ConfigureAwait(false);
            if (comp == null || comp.UserId != user.Id || comp.Source != MembershipSourceEnum.AdminComp) throw ApiErrorException.NotFound("Comp");
            if (comp.RevokedUtc == null)
            {
                DateTime now = DateTime.UtcNow;
                comp.RevokedUtc = now;
                comp.RevokeReason = why;
                comp.UpdatedUtc = now;
                await _Database.Entitlements.UpdateAsync(comp, token).ConfigureAwait(false);
                _Tokens.InvalidateUser(user.Id);
                await _Audit.WriteAsync(info, "member.comp", "User", user.Id, "Ended comp " + comp.Id + ": " + why, token).ConfigureAwait(false);
            }
            return await DetailAsync(user.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Ban a member and revoke their sessions.
        /// </summary>
        /// <param name="id">Member identifier.</param>
        /// <param name="reason">Reason (required).</param>
        /// <param name="info">Request info.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Detail.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404, or 409 CannotBanSelf.</exception>
        public async Task<AdminMemberDetail> BanAsync(string id, string? reason, RequestInfo info, CancellationToken token = default)
        {
            User user = await RequireAsync(id, token).ConfigureAwait(false);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string? why = Reason(reason, errors);
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            if (info.Context?.UserId == user.Id) throw new ApiErrorException(409, "CannotBanSelf", "You cannot ban your own account.");
            DateTime now = DateTime.UtcNow;
            user.IsBanned = true;
            user.BannedReason = why;
            user.BannedUtc = now;
            user.UpdatedUtc = now;
            await _Database.Users.UpdateAsync(user, token).ConfigureAwait(false);
            int revoked = await _Sessions.RevokeAllAsync(user.Id, null, "banned", token).ConfigureAwait(false);
            await _Audit.WriteAsync(info, "member.ban", "User", user.Id, "Banned (" + revoked + " sessions revoked): " + why, token).ConfigureAwait(false);
            if (MemberBanned != null) await MemberBanned(user.Id, token).ConfigureAwait(false);
            if (Email != null) await Email.SendToMemberAsync("account_restricted", user, null, token, ("restriction", "suspended"), ("until", String.Empty), ("reason", why ?? String.Empty)).ConfigureAwait(false);
            return await DetailAsync(user.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Lift a ban.
        /// </summary>
        /// <param name="id">Member identifier.</param>
        /// <param name="info">Request info.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Detail.</returns>
        public async Task<AdminMemberDetail> UnbanAsync(string id, RequestInfo info, CancellationToken token = default)
        {
            User user = await RequireAsync(id, token).ConfigureAwait(false);
            if (user.IsBanned)
            {
                user.IsBanned = false;
                user.BannedReason = null;
                user.BannedUtc = null;
                user.UpdatedUtc = DateTime.UtcNow;
                await _Database.Users.UpdateAsync(user, token).ConfigureAwait(false);
                await _Audit.WriteAsync(info, "member.ban", "User", user.Id, "Unbanned", token).ConfigureAwait(false);
                if (Email != null) await Email.SendToMemberAsync("restriction_lifted", user, null, token, ("link", Email.Link("/app"))).ConfigureAwait(false);
            }
            return await DetailAsync(user.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Mute a member until a time (or indefinitely).
        /// </summary>
        /// <param name="id">Member identifier.</param>
        /// <param name="request">Body.</param>
        /// <param name="info">Request info.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Detail.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed or 404.</exception>
        public async Task<AdminMemberDetail> MuteAsync(string id, MuteRequest request, RequestInfo info, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            User user = await RequireAsync(id, token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            Dictionary<string, string> errors = new Dictionary<string, string>();
            string? why = Reason(request.Reason, errors);
            if (request.UntilUtc != null && request.UntilUtc.Value.ToUniversalTime() <= now) errors["untilUtc"] = "Choose a time in the future, or null for indefinite.";
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            user.IsMuted = true;
            user.MutedUntilUtc = request.UntilUtc?.ToUniversalTime();
            user.MutedReason = why;
            user.UpdatedUtc = now;
            await _Database.Users.UpdateAsync(user, token).ConfigureAwait(false);
            await _Audit.WriteAsync(info, "member.mute", "User", user.Id, "Muted until " + (user.MutedUntilUtc?.ToString("o", System.Globalization.CultureInfo.InvariantCulture) ?? "further notice") + ": " + why, token).ConfigureAwait(false);
            if (Email != null) await Email.SendToMemberAsync("account_restricted", user, null, token, ("restriction", "muted"), ("until", user.MutedUntilUtc == null ? String.Empty : " until " + EmailService.FormatDate(user.MutedUntilUtc.Value)), ("reason", why ?? String.Empty)).ConfigureAwait(false);
            return await DetailAsync(user.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Lift a mute.
        /// </summary>
        /// <param name="id">Member identifier.</param>
        /// <param name="info">Request info.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Detail.</returns>
        public async Task<AdminMemberDetail> UnmuteAsync(string id, RequestInfo info, CancellationToken token = default)
        {
            User user = await RequireAsync(id, token).ConfigureAwait(false);
            if (user.IsMuted)
            {
                user.IsMuted = false;
                user.MutedUntilUtc = null;
                user.MutedReason = null;
                user.UpdatedUtc = DateTime.UtcNow;
                await _Database.Users.UpdateAsync(user, token).ConfigureAwait(false);
                await _Audit.WriteAsync(info, "member.mute", "User", user.Id, "Unmuted", token).ConfigureAwait(false);
                if (Email != null) await Email.SendToMemberAsync("restriction_lifted", user, null, token, ("link", Email.Link("/app"))).ConfigureAwait(false);
            }
            return await DetailAsync(user.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Revoke every session of a member.
        /// </summary>
        /// <param name="id">Member identifier.</param>
        /// <param name="info">Request info.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Detail.</returns>
        public async Task<AdminMemberDetail> RevokeSessionsAsync(string id, RequestInfo info, CancellationToken token = default)
        {
            User user = await RequireAsync(id, token).ConfigureAwait(false);
            int revoked = await _Sessions.RevokeAllAsync(user.Id, null, "revoked by administrator", token).ConfigureAwait(false);
            await _Audit.WriteAsync(info, "member.sessions.revoke", "User", user.Id, "Revoked " + revoked + " sessions", token).ConfigureAwait(false);
            return await DetailAsync(user.Id, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static string PathFor(string userId)
        {
            return TrellisKit.Core.Constants.ApiPrefix + "/admin/members/" + userId;
        }

        private EnumerationRequest Preview(EnumerationCollection collection, string list, string userId)
        {
            return _Enumeration.Prepare(new EnumerationQuery { MaxResults = BoundedPreview<object>.MaxObjects }, collection, true, list + "|" + userId);
        }

        private string? Rank(string? raw, Dictionary<string, string> errors)
        {
            if (String.IsNullOrWhiteSpace(raw)) return null;
            TrellisKit.Core.PropertyConfig.PropertyRankValue? rank = _Property.Property.FindRank(raw.Trim());
            if (rank != null) return rank.Key;
            errors["filters.rank"] = "Use one of " + String.Join(", ", _Property.Property.Profile.Rank.Values.Select(v => v.Key)) + ".";
            return null;
        }

        private async Task<EnumerationResult<AdminSubscriptionItem>> SubscriptionsPageAsync(User user, EnumerationRequest request, CancellationToken token)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            SubscriptionFilter filter = new SubscriptionFilter
            {
                UserId = user.Id,
                Store = request.Query.FilterEnum<StoreEnum>("store", errors),
                Status = request.Query.FilterEnum<SubscriptionStatusEnum>("status", errors)
            };
            if (errors.Count > 0) throw ApiErrorException.Validation(errors, "One or more filters are invalid.");
            EnumerationResult<Subscription> page = await _Database.Subscriptions.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            List<AdminSubscriptionItem> items = page.Objects.Select(s => MemberViewBuilder.ToItem(s, user)).ToList();
            await _Metadata.AttachAsync(TargetTypeEnum.Subscription, items, i => i.Id, true, token).ConfigureAwait(false);
            return page.With(items);
        }

        private async Task<EnumerationResult<RedemptionView>> RedemptionsPageAsync(User user, EnumerationRequest request, CancellationToken token)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            RedemptionFilter filter = new RedemptionFilter { UserId = user.Id, Status = request.Query.FilterEnum<RedemptionStatusEnum>("status", errors) };
            if (errors.Count > 0) throw ApiErrorException.Validation(errors, "One or more filters are invalid.");
            DateTime now = DateTime.UtcNow;
            EnumerationResult<SignupCodeRedemption> page = await _Database.SignupCodeRedemptions.EnumerateAsync(request, filter, now, token).ConfigureAwait(false);
            return page.With(await _Codes.ViewsAsync(page.Objects, now, true, token).ConfigureAwait(false));
        }

        private async Task<EnumerationResult<CompView>> CompsPageAsync(User user, EnumerationRequest request, CancellationToken token)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            if (request.Query.HasFilter("source")) errors["filters.source"] = "Comps are always AdminComp; use membership-history for other sources.";
            EntitlementFilter filter = new EntitlementFilter { UserId = user.Id, Source = MembershipSourceEnum.AdminComp, Status = request.Query.FilterEnum<EntitlementStatusEnum>("status", errors) };
            if (errors.Count > 0) throw ApiErrorException.Validation(errors, "One or more filters are invalid.");
            DateTime now = DateTime.UtcNow;
            EnumerationResult<Entitlement> page = await _Database.Entitlements.EnumerateAsync(request, filter, now, token).ConfigureAwait(false);
            List<CompView> items = page.Objects.Select(e => MemberViewBuilder.ToComp(e, now)).ToList();
            await _Metadata.AttachAsync(TargetTypeEnum.Entitlement, items, i => i.Id, true, token).ConfigureAwait(false);
            return page.With(items);
        }

        private async Task<EnumerationResult<SessionView>> SessionsPageAsync(User user, EnumerationRequest request, CancellationToken token)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            bool includeInactive = request.Query.FilterBool("includeInactive", errors) ?? false;
            if (errors.Count > 0) throw ApiErrorException.Validation(errors, "One or more filters are invalid.");
            EnumerationResult<AuthSession> page = await _Database.Sessions.EnumerateByUserAsync(user.Id, request, includeInactive, DateTime.UtcNow, token).ConfigureAwait(false);
            List<SessionView> items = page.Objects.Select(MemberViewBuilder.ToView).ToList();
            await _Metadata.AttachAsync(TargetTypeEnum.Session, items, i => i.Id, true, token).ConfigureAwait(false);
            return page.With(items);
        }

        private async Task<EnumerationResult<MembershipHistoryItem>> HistoryPageAsync(User user, EnumerationRequest request, CancellationToken token)
        {
            EnumerationResult<MembershipHistoryItem> page = await _Membership.EnumerateHistoryAsync(user.Id, request, token).ConfigureAwait(false);
            await _Metadata.AttachAsync(TargetTypeEnum.Entitlement, page.Objects, i => i.Id, true, token).ConfigureAwait(false);
            return page;
        }

        private async Task<User> RequireAsync(string id, CancellationToken token)
        {
            User? user = String.IsNullOrWhiteSpace(id) ? null : await _Database.Users.ReadAsync(id, token).ConfigureAwait(false);
            return user ?? throw ApiErrorException.NotFound("Member");
        }

        private static string? Reason(string? reason, Dictionary<string, string> errors)
        {
            if (String.IsNullOrWhiteSpace(reason))
            {
                errors["reason"] = "A reason is required.";
                return null;
            }
            string trimmed = reason.Trim();
            if (trimmed.Length > 500)
            {
                errors["reason"] = "Use at most 500 characters.";
                return null;
            }
            return trimmed;
        }

        private static ResourceTypeEnum DefaultResourceType(RoleEnum role)
        {
            switch (role)
            {
                case RoleEnum.Moderator:
                    return ResourceTypeEnum.Forum;
                case RoleEnum.FeedModerator:
                    return ResourceTypeEnum.Feed;
                case RoleEnum.DirectoryModerator:
                    return ResourceTypeEnum.Directory;
                case RoleEnum.EventManager:
                    return ResourceTypeEnum.Events;
                case RoleEnum.RankAwarder:
                    return ResourceTypeEnum.Ranks;
                default:
                    return ResourceTypeEnum.Global;
            }
        }

        private static string Lower(bool value)
        {
            return value ? "true" : "false";
        }

        private static bool TryParse<T>(string? value, out T result) where T : struct, Enum
        {
            result = default;
            if (String.IsNullOrWhiteSpace(value) || Int32.TryParse(value, out int _)) return false;
            return Enum.TryParse(value.Trim(), true, out result) && Enum.IsDefined(result);
        }

        #endregion
    }
}
