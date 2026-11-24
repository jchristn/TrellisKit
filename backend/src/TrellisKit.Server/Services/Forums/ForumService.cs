namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using TrellisKit.Core;
    using TrellisKit.Core.Enumeration;
    using TrellisKit.Core.Enums;
    using TrellisKit.Core.Exceptions;
    using TrellisKit.Core.Helpers;
    using TrellisKit.Core.Models;
    using TrellisKit.Core.Observability;
    using TrellisKit.Core.PropertyConfig;
    using TrellisKit.Core.Requests;
    using TrellisKit.Core.Responses;

    /// <summary>
    /// Forums: member and admin routes, ordering, archive, follows, read state, seeding, moderators, and metrics
    /// (docs/PHASE3_CONTRACT.md sections 3, 9.2, 9.4, 12, and 15).
    /// </summary>
    public class ForumService
    {
        #region Public-Members

        /// <summary>
        /// Slug of the default forum created for a property without forums (resolved decision 12).
        /// </summary>
        public const string DefaultForumSlug = "general-discussion";

        /// <summary>
        /// Name of the default forum.
        /// </summary>
        public const string DefaultForumName = "General discussion";

        /// <summary>
        /// Longest forum description.
        /// </summary>
        public const int MaxDescriptionLength = 500;

        /// <summary>
        /// Longest forum guidelines.
        /// </summary>
        public const int MaxGuidelinesLength = 5000;

        /// <summary>
        /// Most moderators in a forum detail preview.
        /// </summary>
        public const int ModeratorPreviewMax = 10;

        /// <summary>
        /// Most forums in the metrics byForum preview.
        /// </summary>
        public const int MetricsForumMax = 20;

        #endregion

        #region Private-Members

        private static readonly Regex _Slug = new Regex("^[a-z0-9-]{2,60}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _Color = new Regex("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private readonly ApplicationServices _Services;
        private int _Seeded = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public ForumService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create the default forum and the property.json seed forums when the property has no forums yet. Idempotent
        /// and safe under concurrency (slugs are unique).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Forums created.</returns>
        public async Task<int> EnsureSeededAsync(CancellationToken token = default)
        {
            if (Volatile.Read(ref _Seeded) != 0) return 0;
            if (await _Services.Database.Forums.CountAsync(token).ConfigureAwait(false) > 0)
            {
                Interlocked.Exchange(ref _Seeded, 1);
                return 0;
            }
            List<PropertyForumSeed> seeds = new List<PropertyForumSeed>();
            if (_Services.Settings.Forums.SeedDefaultForum) seeds.Add(new PropertyForumSeed { Slug = DefaultForumSlug, Name = DefaultForumName, Description = "Anything about the community." });
            seeds.AddRange(_Services.Property.Property.Forums ?? new List<PropertyForumSeed>());
            int created = 0;
            int order = 10;
            HashSet<string> slugs = new HashSet<string>(StringComparer.Ordinal);
            foreach (PropertyForumSeed seed in seeds)
            {
                token.ThrowIfCancellationRequested();
                string name = MarkdownNormalizer.NormalizeSingleLine(seed.Name ?? String.Empty);
                if (name.Length < 2) continue;
                string slug = String.IsNullOrWhiteSpace(seed.Slug) ? DeriveSlug(name) : seed.Slug.Trim().ToLowerInvariant();
                if (!_Slug.IsMatch(slug) || !slugs.Add(slug)) continue;
                List<ThreadTypeEnum> types = new List<ThreadTypeEnum>();
                foreach (string raw in seed.AllowedThreadTypes ?? new List<string>())
                    if (ModerationService.TryParseEnum(raw, out ThreadTypeEnum type) && !types.Contains(type)) types.Add(type);
                if (types.Count == 0) types = Enum.GetValues<ThreadTypeEnum>().ToList();
                DateTime now = DateTime.UtcNow;
                try
                {
                    await _Services.Database.Forums.CreateAsync(new Forum
                    {
                        Id = IdGenerator.Generate(ForumIdPrefixes.Forum),
                        Slug = slug,
                        Name = Cut(name, 60),
                        Description = String.IsNullOrWhiteSpace(seed.Description) ? null : Cut(seed.Description.Trim(), MaxDescriptionLength),
                        Section = String.IsNullOrWhiteSpace(seed.Section) ? null : Cut(seed.Section.Trim(), 60),
                        SortOrder = order,
                        Audience = ForumAccessService.DefaultForumAudience(_Services.Access.Current),
                        AllowedThreadTypes = types,
                        CreatedUtc = now,
                        UpdatedUtc = now
                    }, token).ConfigureAwait(false);
                    created++;
                    order += 10;
                }
                catch (DuplicateRecordException)
                {
                    // Another instance seeded the same forum concurrently.
                }
            }
            Interlocked.Exchange(ref _Seeded, 1);
            return created;
        }

        /// <summary>
        /// POST /api/v1/forums/enumerate: forums the caller may read.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of forums.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotAvailable.</exception>
        public async Task<EnumerationResult<ForumView>> ListAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Services.Forums.Access.RequireEnabled();
            _Services.Media.EnforceRead(actor);
            await EnsureSeededAsync(token).ConfigureAwait(false);
            EnumerationQuery q = query ?? new EnumerationQuery();
            if (q.MaxResults == null) q.MaxResults = 100;
            ForumFilter filter = ParseFilter(q, actor, false);
            if (!actor.IsSystemAdmin && !actor.AllForumsModerator) filter.Audiences = ContentAudienceRule.VisibleAudiences(actor);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, ForumCollections.Forums, actor.IsStaffFor(TargetTypeEnum.Forum), "forums");
            EnumerationResult<Forum> page = await _Services.Database.Forums.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            return page.With(await _Services.Forums.Views.ForumsAsync<ForumView>(page.Objects, actor, token).ConfigureAwait(false));
        }

        /// <summary>
        /// GET /api/v1/forums/{idOrSlug}.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="idOrSlug">Identifier or slug.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Forum.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<ForumView> GetAsync(ContentActor actor, string idOrSlug, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Services.Media.EnforceRead(actor);
            await EnsureSeededAsync(token).ConfigureAwait(false);
            Forum forum = await _Services.Forums.Access.RequireForumAsync(actor, idOrSlug, token).ConfigureAwait(false);
            return await ViewAsync(forum, actor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// PUT /api/v1/forums/{id}/follow.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="forumId">Forum.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Forum.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 404 NotFound, 429 RateLimited.</exception>
        public async Task<ForumView> FollowAsync(ContentActor actor, string forumId, ForumFollowRequest? request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            string userId = actor.RequireUserId();
            Forum forum = await _Services.Forums.Access.RequireForumAsync(actor, forumId, token).ConfigureAwait(false);
            string level = String.IsNullOrWhiteSpace(request?.Level) ? "NewThreads" : request!.Level!.Trim();
            if (!String.Equals(level, "NewThreads", StringComparison.OrdinalIgnoreCase)) throw ApiErrorException.Field("level", "Use NewThreads.");
            _Services.Limits.EnforceMember(actor, "forums.follows", _Services.Settings.Forums.FollowsPerMinute, RateLimitService.Minute);
            await _Services.Database.Forums.FollowAsync(new ForumFollow { Id = IdGenerator.Generate(ForumIdPrefixes.ForumFollow), ForumId = forum.Id, UserId = userId, Level = "NewThreads", CreatedUtc = DateTime.UtcNow }, token).ConfigureAwait(false);
            ForumTelemetry.Count(ForumTelemetry.Follows, "target_type", "forum", "action", "follow");
            return await ViewAsync(forum, actor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// DELETE /api/v1/forums/{id}/follow.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="forumId">Forum.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Forum.</returns>
        /// <exception cref="ApiErrorException">404 NotFound, 429 RateLimited.</exception>
        public async Task<ForumView> UnfollowAsync(ContentActor actor, string forumId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            string userId = actor.RequireUserId();
            Forum forum = await _Services.Forums.Access.RequireForumAsync(actor, forumId, token).ConfigureAwait(false);
            _Services.Limits.EnforceMember(actor, "forums.follows", _Services.Settings.Forums.FollowsPerMinute, RateLimitService.Minute);
            await _Services.Database.Forums.UnfollowAsync(forum.Id, userId, token).ConfigureAwait(false);
            ForumTelemetry.Count(ForumTelemetry.Follows, "target_type", "forum", "action", "unfollow");
            return await ViewAsync(forum, actor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/forums/{id}/read: mark every thread in the forum read.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="forumId">Forum.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task MarkReadAsync(ContentActor actor, string forumId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            string userId = actor.RequireUserId();
            Forum forum = await _Services.Forums.Access.RequireForumAsync(actor, forumId, token).ConfigureAwait(false);
            await _Services.Database.Forums.MarkReadAsync(userId, forum.Id, DateTime.UtcNow, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Build one forum payload.
        /// </summary>
        /// <param name="forum">Forum.</param>
        /// <param name="actor">Caller.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>View.</returns>
        public async Task<ForumView> ViewAsync(Forum forum, ContentActor actor, CancellationToken token = default)
        {
            return (await _Services.Forums.Views.ForumsAsync<ForumView>(new[] { forum }, actor, token).ConfigureAwait(false))[0];
        }

        #region Admin

        /// <summary>
        /// POST /api/v1/admin/forums/enumerate.
        /// </summary>
        /// <param name="actor">Caller (SystemAdmin).</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of admin forums.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden.</exception>
        public async Task<EnumerationResult<AdminForumView>> ListAdminAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            RequireAdmin(actor);
            _Services.Forums.Access.RequireEnabled();
            await EnsureSeededAsync(token).ConfigureAwait(false);
            EnumerationQuery q = query ?? new EnumerationQuery();
            ForumFilter filter = ParseFilter(q, actor, true);
            EnumerationRequest request = _Services.Enumeration.Prepare(q, ForumCollections.AdminForums, true, "admin-forums");
            EnumerationResult<Forum> page = await _Services.Database.Forums.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            return page.With(await AdminViewsAsync<AdminForumView>(page.Objects, actor, token).ConfigureAwait(false));
        }

        /// <summary>
        /// GET /api/v1/admin/forums/{id}.
        /// </summary>
        /// <param name="actor">Caller (SystemAdmin).</param>
        /// <param name="forumId">Forum.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Admin forum with a moderators preview.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task<AdminForumDetail> GetAdminAsync(ContentActor actor, string forumId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            RequireAdmin(actor);
            Forum forum = await RequireForumAsync(forumId, token).ConfigureAwait(false);
            AdminForumDetail detail = (await AdminViewsAsync<AdminForumDetail>(new[] { forum }, actor, token).ConfigureAwait(false))[0];
            EnumerationResult<ForumModeratorView> moderators = await ModeratorsAsync(actor, forum.Id, new EnumerationQuery { MaxResults = ModeratorPreviewMax }, token).ConfigureAwait(false);
            detail.Moderators = new BoundedPreview<ForumModeratorView>
            {
                Objects = moderators.Objects.Take(ModeratorPreviewMax).ToList(),
                TotalRecords = moderators.TotalRecords ?? moderators.Objects.Count,
                Enumerate = Constants.ApiPrefix + "/admin/forums/" + forum.Id + "/moderators/enumerate"
            };
            return detail;
        }

        /// <summary>
        /// POST /api/v1/admin/forums.
        /// </summary>
        /// <param name="actor">Caller (SystemAdmin).</param>
        /// <param name="info">Request information.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Forum.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 409 SlugInUse or ForumLimitReached.</exception>
        public async Task<ForumView> CreateAsync(ContentActor actor, RequestInfo info, ForumRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            RequireAdmin(actor);
            _Services.Forums.Access.RequireEnabled();
            await EnsureSeededAsync(token).ConfigureAwait(false);
            if (await _Services.Database.Forums.CountAsync(token).ConfigureAwait(false) >= _Services.Settings.Forums.MaxForums)
                throw new ApiErrorException(409, "ForumLimitReached", "This community already has " + _Services.Settings.Forums.MaxForums + " forums (forums.maxForums).");
            DateTime now = DateTime.UtcNow;
            Forum forum = new Forum { Id = IdGenerator.Generate(ForumIdPrefixes.Forum), Audience = ForumAccessService.DefaultForumAudience(_Services.Access.Current), AllowedThreadTypes = Enum.GetValues<ThreadTypeEnum>().ToList(), CreatedByUserId = actor.UserId, CreatedUtc = now, UpdatedUtc = now };
            HashSet<string> present = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "name", "slug", "description", "guidelines", "icon", "color", "section", "audience", "threadCreation",
                "allowedThreadTypes", "defaultOrdering", "allowMedia", "allowVideo", "maxOpenFeedbackPerMember" };
            await ApplyAsync(actor, forum, request, present, true, token).ConfigureAwait(false);
            forum.SortOrder = await _Services.Database.Forums.NextSortOrderAsync(token).ConfigureAwait(false);
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, true, token, TargetTypeEnum.Forum).ConfigureAwait(false);
            try
            {
                forum = await _Services.Database.Forums.CreateAsync(forum, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw SlugInUse();
            }
            await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.Forum, forum.Id, request.Labels, request.Tags, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "forum.create", TargetTypeEnum.Forum.ToString(), forum.Id, forum.Name, token).ConfigureAwait(false);
            return await ViewAsync(forum, actor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// PUT /api/v1/admin/forums/{id}: any create field; narrowing the audience narrows every thread in the same batch.
        /// </summary>
        /// <param name="actor">Caller (SystemAdmin).</param>
        /// <param name="info">Request information.</param>
        /// <param name="forumId">Forum.</param>
        /// <param name="request">Body.</param>
        /// <param name="present">Fields present in the body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Forum.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound, 409 SlugInUse.</exception>
        public async Task<ForumView> UpdateAsync(ContentActor actor, RequestInfo info, string forumId, ForumRequest request, HashSet<string> present, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(present);
            RequireAdmin(actor);
            Forum forum = await RequireForumAsync(forumId, token).ConfigureAwait(false);
            ContentAudienceEnum before = forum.Audience;
            await ApplyAsync(actor, forum, request, present, false, token).ConfigureAwait(false);
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, true, token, TargetTypeEnum.Forum).ConfigureAwait(false);
            forum.UpdatedUtc = DateTime.UtcNow;
            bool narrow = ForumAccessService.Rank(forum.Audience) > ForumAccessService.Rank(before);
            try
            {
                forum = await _Services.Database.Forums.UpdateAsync(forum, narrow, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw SlugInUse();
            }
            await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.Forum, forum.Id, present.Contains("labels") ? request.Labels : null, present.Contains("tags") ? request.Tags : null, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "forum.update", TargetTypeEnum.Forum.ToString(), forum.Id, forum.Name + (narrow ? " (audience narrowed to " + forum.Audience + ")" : String.Empty), token).ConfigureAwait(false);
            return await ViewAsync(forum, actor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// PUT /api/v1/admin/forums/order: every non-archived forum exactly once.
        /// </summary>
        /// <param name="actor">Caller (SystemAdmin).</param>
        /// <param name="info">Request information.</param>
        /// <param name="request">Body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count reordered.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden.</exception>
        public async Task<ForumOrderResponse> OrderAsync(ContentActor actor, RequestInfo info, ForumOrderRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            RequireAdmin(actor);
            List<string> ids = (request.ForumIds ?? new List<string>()).Select(i => (i ?? String.Empty).Trim()).ToList();
            if (ids.Count > _Services.Settings.Forums.MaxForums) throw ApiErrorException.Field("forumIds", "At most " + _Services.Settings.Forums.MaxForums + " forums.");
            List<string> active = await _Services.Database.Forums.ActiveIdsAsync(_Services.Settings.Forums.MaxForums, token).ConfigureAwait(false);
            HashSet<string> activeSet = new HashSet<string>(active, StringComparer.Ordinal);
            if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Count) throw ApiErrorException.Field("forumIds", "Each forum may appear only once.");
            List<string> unknown = ids.Where(i => !activeSet.Contains(i)).ToList();
            if (unknown.Count > 0) throw ApiErrorException.Field("forumIds", "Unknown or archived forum: " + String.Join(", ", unknown.Take(5)) + ".");
            if (ids.Count != active.Count) throw ApiErrorException.Field("forumIds", "List every non-archived forum exactly once (" + active.Count + " forums).");
            int updated = await _Services.Database.Forums.ReorderAsync(ids, DateTime.UtcNow, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "forum.order", TargetTypeEnum.Forum.ToString(), null, updated + " forums", token).ConfigureAwait(false);
            return new ForumOrderResponse { Updated = updated };
        }

        /// <summary>
        /// POST /api/v1/admin/forums/{id}/archive and .../unarchive.
        /// </summary>
        /// <param name="actor">Caller (SystemAdmin).</param>
        /// <param name="info">Request information.</param>
        /// <param name="forumId">Forum.</param>
        /// <param name="archived">Archive (true) or unarchive (false).</param>
        /// <param name="reason">Optional reason.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Forum.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task<ForumView> SetArchivedAsync(ContentActor actor, RequestInfo info, string forumId, bool archived, string? reason, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            RequireAdmin(actor);
            Forum forum = await RequireForumAsync(forumId, token).ConfigureAwait(false);
            await _Services.Database.Forums.SetArchivedAsync(forum.Id, archived, DateTime.UtcNow, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, archived ? "forum.archive" : "forum.unarchive", TargetTypeEnum.Forum.ToString(), forum.Id,
                forum.Name + (String.IsNullOrWhiteSpace(reason) ? String.Empty : ": " + Cut(reason.Trim(), 200)), token).ConfigureAwait(false);
            return await ViewAsync(await RequireForumAsync(forum.Id, token).ConfigureAwait(false), actor, token).ConfigureAwait(false);
        }

        /// <summary>
        /// DELETE /api/v1/admin/forums/{id}: only when the forum has no threads that are not deleted (Published, Processing, Hidden, or Removed threads block it; deleted threads do not).
        /// </summary>
        /// <param name="actor">Caller (SystemAdmin).</param>
        /// <param name="info">Request information.</param>
        /// <param name="forumId">Forum.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound, 409 ForumNotEmpty.</exception>
        public async Task DeleteAsync(ContentActor actor, RequestInfo info, string forumId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            RequireAdmin(actor);
            Forum forum = await RequireForumAsync(forumId, token).ConfigureAwait(false);
            if (!await _Services.Database.Forums.DeleteAsync(forum.Id, DateTime.UtcNow, token).ConfigureAwait(false))
                throw new ApiErrorException(409, "ForumNotEmpty", "This forum still has threads; archive it instead.");
            await _Services.Audit.WriteAsync(info, "forum.delete", TargetTypeEnum.Forum.ToString(), forum.Id, forum.Name, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/forums/{id}/moderators/enumerate: moderators whose assignments cover the forum.
        /// </summary>
        /// <param name="actor">Caller (SystemAdmin).</param>
        /// <param name="forumId">Forum.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of moderator rows.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound.</exception>
        public async Task<EnumerationResult<ForumModeratorView>> ModeratorsAsync(ContentActor actor, string forumId, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            RequireAdmin(actor);
            Forum forum = await RequireForumAsync(forumId, token).ConfigureAwait(false);
            EnumerationRequest request = _Services.Enumeration.Prepare(query, ForumCollections.ForumModerators, true, "forum-moderators|" + forum.Id);
            EnumerationResult<string> page = await _Services.Database.Forums.EnumerateModeratorsAsync(forum.Id, request, token).ConfigureAwait(false);
            Dictionary<string, UserSummary> users = await _Services.Summaries.BuildAsync(page.Objects.Select(i => (string?)i), token).ConfigureAwait(false);
            List<RoleAssignment> roles = await _Services.Database.RoleAssignments.EnumerateByUsersAsync(page.Objects, token).ConfigureAwait(false);
            Dictionary<string, ObjectMetadata> metadata = await _Services.Metadata.LoadAsync(TargetTypeEnum.User, page.Objects, true, token).ConfigureAwait(false);
            Dictionary<string, UserSummary> granters = await _Services.Summaries.BuildAsync(roles.Select(r => r.GrantedByUserId), token).ConfigureAwait(false);
            List<ForumModeratorView> rows = new List<ForumModeratorView>();
            foreach (string id in page.Objects)
            {
                List<RoleAssignment> covering = roles.Where(r => r.UserId == id && r.Role == RoleEnum.Moderator &&
                    (r.ResourceType == ResourceTypeEnum.Global || (r.ResourceType == ResourceTypeEnum.Forum && (r.ResourceId == "*" || r.ResourceId == forum.Id)))).OrderBy(r => r.CreatedUtc).ToList();
                RoleAssignment? first = covering.FirstOrDefault();
                bool all = covering.Any(ActorService.IsAllForumsModeratorRole);
                rows.Add(new ForumModeratorView
                {
                    User = users.TryGetValue(id, out UserSummary? u) ? u : UserSummary.Deleted(),
                    Roles = covering.Select(r => new RoleView { Role = r.Role, ResourceType = r.ResourceType, ResourceId = r.ResourceId }).ToList(),
                    GrantedUtc = first?.CreatedUtc,
                    GrantedByName = first == null ? null : (first.GrantedByUserId == null ? "admin token" : (granters.TryGetValue(first.GrantedByUserId, out UserSummary? g) ? g.DisplayName : UserSummary.Deleted().DisplayName)),
                    Scope = all ? "AllForums" : "Forum",
                    ForumId = all ? null : forum.Id,
                    Labels = metadata.TryGetValue(id, out ObjectMetadata? m) ? m.Labels : new List<string>(),
                    Tags = m != null ? m.Tags : new Dictionary<string, string>()
                });
            }
            return page.With(rows);
        }

        /// <summary>
        /// GET /api/v1/admin/metrics/forums?days= (7 to 365).
        /// </summary>
        /// <param name="actor">Caller (SystemAdmin).</param>
        /// <param name="days">Window in days (default 30).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Metrics.</returns>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden.</exception>
        public async Task<ForumMetrics> MetricsAsync(ContentActor actor, int? days, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            RequireAdmin(actor);
            int window = days ?? 30;
            if (window < 7 || window > 365) throw ApiErrorException.Field("days", "Must be 7 to 365.");
            await EnsureSeededAsync(token).ConfigureAwait(false);
            DateTime now = DateTime.UtcNow;
            DateTime from = DateTime.SpecifyKind(now.Date.AddDays(-(window - 1)), DateTimeKind.Utc);
            ThreadActivityCounts counts = await _Services.Database.Threads.ActivityCountsAsync(from, token).ConfigureAwait(false);
            ForumTelemetry.SetOpenReviewRequests(counts.OpenReviewRequests);
            Dictionary<string, long> byForum = await _Services.Database.Threads.ThreadsInWindowByForumAsync(from, token).ConfigureAwait(false);
            List<string> top = byForum.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).Take(MetricsForumMax).Select(p => p.Key).ToList();
            List<Forum> forums = (await _Services.Database.Forums.ReadManyAsync(top, token).ConfigureAwait(false)).Where(f => f.DeletedUtc == null).ToList();
            List<Forum> ordered = top.Select(id => forums.FirstOrDefault(f => f.Id == id)).Where(f => f != null).Select(f => f!).ToList();
            return new ForumMetrics
            {
                Days = window,
                ThreadsByDay = await _Services.Database.Threads.DailyAsync("threads", from, window, token).ConfigureAwait(false),
                CommentsByDay = await _Services.Database.Threads.DailyAsync("comments", from, window, token).ConfigureAwait(false),
                VotesByDay = await _Services.Database.Threads.DailyAsync("votes", from, window, token).ConfigureAwait(false),
                ActiveThreads = counts.ActiveThreads,
                OpenReviewRequests = counts.OpenReviewRequests,
                UnansweredQuestions = counts.UnansweredQuestions,
                ByForum = new BoundedPreview<AdminForumView>
                {
                    Objects = await AdminViewsAsync<AdminForumView>(ordered, actor, token).ConfigureAwait(false),
                    TotalRecords = await _Services.Database.Forums.CountAsync(token).ConfigureAwait(false),
                    Enumerate = Constants.ApiPrefix + "/admin/forums/enumerate"
                }
            };
        }

        #endregion

        /// <summary>
        /// Derive a slug from a name: lowercase, letters and digits kept, other runs become one hyphen.
        /// </summary>
        /// <param name="name">Name.</param>
        /// <returns>Slug (2 to 60 characters).</returns>
        public static string DeriveSlug(string name)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char ch in (name ?? String.Empty).Normalize(NormalizationForm.FormD).ToLowerInvariant())
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
                if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9')) sb.Append(ch);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
            }
            string slug = sb.ToString().Trim('-');
            if (slug.Length > 60) slug = slug.Substring(0, 60).Trim('-');
            if (slug.Length < 2) slug = "forum-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            return slug;
        }

        #endregion

        #region Private-Methods

        private static void RequireAdmin(ContentActor actor)
        {
            if (!actor.IsSystemAdmin) throw ApiErrorException.Forbidden("Only a SystemAdmin can manage forums.");
        }

        private static ApiErrorException SlugInUse()
        {
            return new ApiErrorException(409, "SlugInUse", "Another forum already uses this slug.", new Dictionary<string, string> { { "slug", "Choose another slug." } });
        }

        private async Task<Forum> RequireForumAsync(string forumId, CancellationToken token)
        {
            _Services.Forums.Access.RequireEnabled();
            Forum? forum = String.IsNullOrWhiteSpace(forumId) ? null : await _Services.Database.Forums.ReadAsync(forumId.Trim(), token).ConfigureAwait(false);
            if (forum == null) throw ApiErrorException.NotFound("Forum");
            return forum;
        }

        private ForumFilter ParseFilter(EnumerationQuery q, ContentActor actor, bool admin)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            ForumFilter filter = new ForumFilter
            {
                Section = q.FilterString("section"),
                IncludeArchived = q.FilterBool("includeArchived", errors) ?? false,
                Audience = q.FilterEnum<ContentAudienceEnum>("audience", errors)
            };
            if (admin) filter.IsArchived = q.FilterBool("isArchived", errors);
            if (q.FilterBool("followedOnly", errors) == true)
            {
                if (String.IsNullOrEmpty(actor.UserId)) errors["filters.followedOnly"] = "Needs a member session.";
                else filter.FollowedByUserId = actor.UserId;
            }
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            return filter;
        }

        private async Task<List<T>> AdminViewsAsync<T>(IReadOnlyList<Forum> forums, ContentActor actor, CancellationToken token) where T : AdminForumView, new()
        {
            List<T> views = await _Services.Forums.Views.ForumsAsync<T>(forums, actor, token).ConfigureAwait(false);
            if (views.Count == 0) return views;
            Dictionary<string, long> reports = await _Services.Database.Forums.OpenReportCountsAsync(forums.Select(f => f.Id), token).ConfigureAwait(false);
            Dictionary<string, long> recent = await _Services.Database.Threads.ThreadsInWindowByForumAsync(DateTime.UtcNow.AddDays(-30), token).ConfigureAwait(false);
            for (int i = 0; i < views.Count; i++)
            {
                T view = views[i];
                Forum forum = forums[i];
                view.OpenReportCount = reports.TryGetValue(forum.Id, out long r) ? r : 0;
                view.ModeratorCount = await _Services.Database.Forums.CountModeratorsAsync(forum.Id, token).ConfigureAwait(false);
                view.PinnedCount = await _Services.Database.Threads.CountPinnedAsync(forum.Id, token).ConfigureAwait(false);
                view.ThreadsLast30d = recent.TryGetValue(forum.Id, out long n) ? n : 0;
                view.ArchivedUtc = forum.ArchivedUtc;
            }
            return views;
        }

        private async Task ApplyAsync(ContentActor actor, Forum forum, ForumRequest request, HashSet<string> present, bool creating, CancellationToken token)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            if (present.Contains("name"))
            {
                string name = MarkdownNormalizer.IsWellFormed(request.Name) ? MarkdownNormalizer.NormalizeSingleLine(request.Name) : String.Empty;
                int length = MarkdownNormalizer.Length(name);
                if (length < 2 || length > 60) errors["name"] = "Use 2 to 60 characters.";
                else forum.Name = name;
            }
            if (present.Contains("slug") && !(creating && String.IsNullOrWhiteSpace(request.Slug)))
            {
                string slug = (request.Slug ?? String.Empty).Trim().ToLowerInvariant();
                if (!_Slug.IsMatch(slug)) errors["slug"] = "Use 2 to 60 of a-z, 0-9, and -.";
                else forum.Slug = slug;
            }
            if (creating && String.IsNullOrWhiteSpace(forum.Slug) && !errors.ContainsKey("name")) forum.Slug = DeriveSlug(forum.Name);
            if (present.Contains("description")) forum.Description = Optional(request.Description, MaxDescriptionLength, "description", errors, false);
            if (present.Contains("guidelines")) forum.Guidelines = Optional(request.Guidelines, MaxGuidelinesLength, "guidelines", errors, true);
            if (present.Contains("section")) forum.Section = Optional(request.Section, 60, "section", errors, false);
            if (present.Contains("color"))
            {
                string? color = String.IsNullOrWhiteSpace(request.Color) ? null : request.Color.Trim();
                if (color != null && !_Color.IsMatch(color)) errors["color"] = "Use #RRGGBB or null.";
                else forum.Color = color?.ToUpperInvariant();
            }
            if (present.Contains("icon"))
            {
                string? emoji = String.IsNullOrWhiteSpace(request.Icon?.Emoji) ? null : request.Icon!.Emoji!.Trim();
                string? mediaId = String.IsNullOrWhiteSpace(request.Icon?.MediaId) ? null : request.Icon!.MediaId!.Trim();
                if (emoji != null && mediaId != null) errors["icon"] = "Use an emoji or an image, not both.";
                else if (emoji != null && new StringInfo(emoji).LengthInTextElements != 1) errors["icon.emoji"] = "Use one emoji.";
                else if (mediaId != null)
                {
                    MediaAttachment? media = await _Services.Database.Media.ReadAsync(mediaId, token).ConfigureAwait(false);
                    if (media == null || media.DeletedUtc != null || media.Kind != MediaKindEnum.Image || media.Status != MediaStatusEnum.Ready) errors["icon.mediaId"] = "Use a Ready image.";
                    else if (!await _Services.Forums.Access.IsSystemAdminAsync(media.OwnerUserId, token).ConfigureAwait(false) && !String.Equals(media.OwnerUserId, actor.UserId, StringComparison.Ordinal))
                        errors["icon.mediaId"] = "The image must be uploaded by an administrator.";
                }
                forum.IconEmoji = emoji;
                forum.IconMediaId = mediaId;
            }
            if (present.Contains("audience") && request.Audience != null) forum.Audience = request.Audience.Value;
            if (present.Contains("threadCreation") && request.ThreadCreation != null) forum.ThreadCreation = request.ThreadCreation.Value;
            if (present.Contains("defaultOrdering") && request.DefaultOrdering != null)
            {
                if (request.DefaultOrdering.Value == ThreadOrderingEnum.Relevance) errors["defaultOrdering"] = "Relevance needs a search; use Active, Hot, CreatedDescending, CreatedAscending, or Top.";
                else forum.DefaultOrdering = request.DefaultOrdering.Value;
            }
            if (present.Contains("allowedThreadTypes") && request.AllowedThreadTypes != null)
            {
                List<ThreadTypeEnum> types = request.AllowedThreadTypes.Distinct().ToList();
                if (types.Count == 0) errors["allowedThreadTypes"] = "Allow at least one thread type.";
                else forum.AllowedThreadTypes = Enum.GetValues<ThreadTypeEnum>().Where(types.Contains).ToList();
            }
            if (present.Contains("allowMedia") && request.AllowMedia != null) forum.AllowMedia = request.AllowMedia.Value;
            if (present.Contains("allowVideo") && request.AllowVideo != null) forum.AllowVideo = request.AllowVideo.Value;
            if (present.Contains("maxOpenFeedbackPerMember"))
            {
                if (request.MaxOpenFeedbackPerMember != null && (request.MaxOpenFeedbackPerMember.Value < 0 || request.MaxOpenFeedbackPerMember.Value > 100)) errors["maxOpenFeedbackPerMember"] = "Use 0 to 100, or null for the property setting.";
                else forum.MaxOpenFeedbackPerMember = request.MaxOpenFeedbackPerMember;
            }
            if (creating && String.IsNullOrWhiteSpace(forum.Name)) errors.TryAdd("name", "Required (2 to 60 characters).");
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
        }

        private static string? Optional(string? value, int max, string field, Dictionary<string, string> errors, bool markdown)
        {
            if (value == null) return null;
            if (!MarkdownNormalizer.IsWellFormed(value))
            {
                errors[field] = "Must be valid UTF-8 text.";
                return null;
            }
            string normalized = markdown ? MarkdownNormalizer.Normalize(value) : MarkdownNormalizer.NormalizeSingleLine(value);
            if (MarkdownNormalizer.Length(normalized) > max) errors[field] = "At most " + max + " characters.";
            return normalized.Length == 0 ? null : normalized;
        }

        private static string Cut(string value, int max)
        {
            return value.Length > max ? value.Substring(0, max) : value;
        }

        #endregion
    }
}
