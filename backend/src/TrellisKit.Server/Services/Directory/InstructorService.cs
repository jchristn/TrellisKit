namespace TrellisKit.Server.Services
{
    using System;
    using System.Collections.Generic;
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
    /// Instructors (docs/PHASE4_CONTRACT.md sections 2.3, 3.1, 3.8, and 3.9): member reads and lists, staff create, edit,
    /// archive, soft delete, links to member accounts (the member is notified and may unlink), and the instructor rank from
    /// the configured Rank profile field. Thread safe.
    /// </summary>
    public class InstructorService
    {
        #region Private-Members

        private readonly ApplicationServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Property services.</param>
        /// <exception cref="ArgumentNullException">The services are null.</exception>
        public InstructorService(ApplicationServices services)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// POST /api/v1/instructors/enumerate. Filters venueId, rank (keys), claimed, linkedUserId, audience.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        public async Task<EnumerationResult<InstructorSummaryView>> ListAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Services.Limits.EnforceMember(actor, MediaService.ReadBucket, _Services.Settings.RateLimits.MemberReadsPerMinute, RateLimitService.Minute);
            EnumerationRequest request = _Services.Enumeration.Prepare(query, DirectoryCollections.Instructors, actor.IsDirectoryStaff);
            InstructorFilter filter = Filter(actor, request.Query, false);
            EnumerationResult<Instructor> page = await _Services.Database.DirectoryListings.Instructors.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            List<InstructorSummaryView> views = await _Services.Directory.Views.InstructorSummariesAsync(actor, page.Objects, new DirectoryViewOptions(), token).ConfigureAwait(false);
            return page.With(views);
        }

        /// <summary>
        /// GET /api/v1/instructors/{idOrSlug}.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="idOrSlug">Identifier or slug.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Instructor (a locked stub when the caller needs a membership).</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<InstructorView> GetAsync(ContentActor actor, string idOrSlug, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            _Services.Limits.EnforceMember(actor, MediaService.ReadBucket, _Services.Settings.RateLimits.MemberReadsPerMinute, RateLimitService.Minute);
            Instructor instructor = await FindAsync(idOrSlug, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Instructor");
            if (DirectoryViewBuilder.Level(actor, instructor, new DirectoryViewOptions()) == 0) throw ApiErrorException.NotFound("Instructor");
            return await _Services.Directory.Views.InstructorAsync(actor, instructor, new DirectoryViewOptions { Detail = true }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/instructors/{id}/venues/enumerate.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="instructorId">Instructor.</param>
        /// <param name="query">Query (filter includeClosed).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">404 NotFound.</exception>
        public async Task<EnumerationResult<VenueSummaryView>> VenuesAsync(ContentActor actor, string instructorId, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            Instructor instructor = await FindAsync(instructorId, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Instructor");
            if (DirectoryViewBuilder.Level(actor, instructor, new DirectoryViewOptions()) != 2) throw ApiErrorException.NotFound("Instructor");
            EnumerationRequest request = _Services.Enumeration.Prepare(query, DirectoryCollections.InstructorVenues, actor.IsDirectoryStaff, instructor.Id);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            VenueFilter filter = new VenueFilter
            {
                InstructorId = instructor.Id,
                IncludeClosed = request.Query.FilterBool("includeClosed", errors) ?? false,
                Audiences = DirectoryAccess.ListedAudiences(actor),
                LiveOnly = !actor.IsDirectoryStaff
            };
            DirectoryFilters.ThrowIfAny(errors);
            EnumerationResult<Venue> page = await _Services.Database.DirectoryListings.Venues.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            List<VenueSummaryView> views = await _Services.Directory.Views.VenueSummariesAsync(actor, page.Objects, new DirectoryViewOptions(), token).ConfigureAwait(false);
            return page.With(views);
        }

        /// <summary>
        /// POST /api/v1/admin/instructors/enumerate (directory staff). Filters as members plus status.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="query">Query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden.</exception>
        public async Task<EnumerationResult<InstructorSummaryView>> AdminListAsync(ContentActor actor, EnumerationQuery? query, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            DirectoryAccess.RequireStaff(actor);
            EnumerationRequest request = _Services.Enumeration.Prepare(query, DirectoryCollections.AdminInstructors, true);
            InstructorFilter filter = Filter(actor, request.Query, true);
            EnumerationResult<Instructor> page = await _Services.Database.DirectoryListings.Instructors.EnumerateAsync(request, filter, token).ConfigureAwait(false);
            List<InstructorSummaryView> views = await _Services.Directory.Views.InstructorSummariesAsync(actor, page.Objects, new DirectoryViewOptions { Admin = true }, token).ConfigureAwait(false);
            return page.With(views);
        }

        /// <summary>
        /// GET /api/v1/admin/instructors/{id} (directory staff).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="id">Instructor identifier or slug.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Admin instructor.</returns>
        /// <exception cref="ArgumentNullException">The actor is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task<InstructorView> AdminGetAsync(ContentActor actor, string id, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            DirectoryAccess.RequireStaff(actor);
            Instructor instructor = await FindAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Instructor");
            return await _Services.Directory.Views.InstructorAsync(actor, instructor, new DirectoryViewOptions { Admin = true, Detail = true }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/instructors (directory staff). Audited as instructor.create.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="request">Body (venueIds links the new entry with the Regular role).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Admin instructor.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, UnknownOption, or ContentFiltered; 403 Forbidden; 404 NotFound (a venue); 409 SlugInUse.</exception>
        public async Task<InstructorView> CreateAsync(ContentActor actor, RequestInfo info, InstructorRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            DirectoryAccess.RequireStaff(actor);
            Instructor instructor = await BuildAsync(actor, request, null, null, actor.UserId, token).ConfigureAwait(false);
            List<string> venueIds = await VenueIdsAsync(request.VenueIds, token).ConfigureAwait(false);
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, true, token, TargetTypeEnum.Instructor).ConfigureAwait(false);
            try
            {
                instructor = await _Services.Database.DirectoryListings.Instructors.CreateAsync(instructor, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw SlugInUse();
            }
            foreach (string venueId in venueIds) await _Services.Database.DirectoryListings.Venues.LinkAsync(venueId, instructor.Id, VenueInstructorRoleEnum.Regular, DateTime.UtcNow, token).ConfigureAwait(false);
            await AfterSaveAsync(actor, instructor, request.Labels, request.Tags, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "instructor.create", TargetTypeEnum.Instructor.ToString(), instructor.Id, instructor.DisplayName, token).ConfigureAwait(false);
            return await AdminGetAsync(actor, instructor.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// PUT /api/v1/admin/instructors/{id} (directory staff): omitted fields stay. Audited as instructor.update.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="id">Instructor.</param>
        /// <param name="request">Body.</param>
        /// <param name="present">Fields present in the body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Admin instructor.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400, 403, 404 NotFound, 409 SlugInUse.</exception>
        public async Task<InstructorView> UpdateAsync(ContentActor actor, RequestInfo info, string id, InstructorRequest request, HashSet<string> present, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(present);
            DirectoryAccess.RequireStaff(actor);
            Instructor existing = await _Services.Database.DirectoryListings.Instructors.ReadAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Instructor");
            Instructor instructor = await BuildAsync(actor, request, existing, present, actor.UserId, token).ConfigureAwait(false);
            await _Services.Metadata.ValidateAsync(actor, request.Labels, request.Tags, true, token, TargetTypeEnum.Instructor).ConfigureAwait(false);
            Instructor? saved;
            try
            {
                saved = await _Services.Database.DirectoryListings.Instructors.UpdateAsync(instructor, existing.Slug, token).ConfigureAwait(false);
            }
            catch (DuplicateRecordException)
            {
                throw SlugInUse();
            }
            if (saved == null) throw ApiErrorException.NotFound("Instructor");
            await AfterSaveAsync(actor, saved, present.Contains("labels") ? request.Labels : null, present.Contains("tags") ? request.Tags : null, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "instructor.update", TargetTypeEnum.Instructor.ToString(), saved.Id, saved.DisplayName, token).ConfigureAwait(false);
            return await AdminGetAsync(actor, saved.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// DELETE /api/v1/admin/instructors/{id} (directory staff): soft delete; links removed. Audited as instructor.delete.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="id">Instructor.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task DeleteAsync(ContentActor actor, RequestInfo info, string id, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            DirectoryAccess.RequireStaff(actor);
            Instructor instructor = await _Services.Database.DirectoryListings.Instructors.ReadAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Instructor");
            if (!await _Services.Database.DirectoryListings.Instructors.SoftDeleteAsync(instructor.Id, DateTime.UtcNow, token).ConfigureAwait(false)) throw ApiErrorException.NotFound("Instructor");
            await _Services.Media.UnpublishTargetAsync(TargetTypeEnum.Instructor, instructor.Id, token).ConfigureAwait(false);
            await _Services.Directory.ChangedAsync(TargetTypeEnum.Instructor, instructor.Id, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "instructor.delete", TargetTypeEnum.Instructor.ToString(), instructor.Id, instructor.DisplayName, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/instructors/{id}/archive or unarchive (directory staff). Audited as instructor.update.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="id">Instructor.</param>
        /// <param name="archived">Archive or unarchive.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Admin instructor.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task<InstructorView> SetArchivedAsync(ContentActor actor, RequestInfo info, string id, bool archived, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            DirectoryAccess.RequireStaff(actor);
            Instructor instructor = await _Services.Database.DirectoryListings.Instructors.ReadAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Instructor");
            await _Services.Database.DirectoryListings.Instructors.SetStatusAsync(instructor.Id, archived ? InstructorStatusEnum.Archived : InstructorStatusEnum.Active, DateTime.UtcNow, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "instructor.update", TargetTypeEnum.Instructor.ToString(), instructor.Id, archived ? "Archived" : "Unarchived", token).ConfigureAwait(false);
            await _Services.Directory.ChangedAsync(TargetTypeEnum.Instructor, instructor.Id, token).ConfigureAwait(false);
            return await AdminGetAsync(actor, instructor.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/instructors/{id}/link (directory staff): link the entry to a member, who is notified
        /// (InstructorLinked) and may unlink. Audited as instructor.link.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="id">Instructor.</param>
        /// <param name="userId">Member.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Admin instructor.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, 403 Forbidden, 404 NotFound, 409 AlreadyClaimed.</exception>
        public async Task<InstructorView> LinkAsync(ContentActor actor, RequestInfo info, string id, string? userId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            DirectoryAccess.RequireStaff(actor);
            if (String.IsNullOrWhiteSpace(userId)) throw ApiErrorException.Field("userId", "Required.");
            Instructor instructor = await _Services.Database.DirectoryListings.Instructors.ReadAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Instructor");
            User? user = await _Services.Database.Users.ReadAsync(userId.Trim(), token).ConfigureAwait(false);
            if (user == null || user.IsDeleted) throw ApiErrorException.NotFound("Member");
            if (instructor.LinkedUserId != null && instructor.LinkedUserId != user.Id) throw AlreadyClaimed("This entry is linked to another account; unlink it first.");
            if (instructor.LinkedUserId != user.Id)
            {
                Instructor? other = await _Services.Database.DirectoryListings.Instructors.ReadByLinkedUserAsync(user.Id, token).ConfigureAwait(false);
                if (other != null) throw AlreadyClaimed("This member is linked to another entry.");
                try
                {
                    await _Services.Database.DirectoryListings.Instructors.SetLinkedUserAsync(instructor.Id, user.Id, DateTime.UtcNow, token).ConfigureAwait(false);
                }
                catch (DuplicateRecordException)
                {
                    throw AlreadyClaimed("This member is linked to another entry.");
                }
                await NotifyLinkedAsync(instructor, user.Id, actor.UserId, token).ConfigureAwait(false);
            }
            await _Services.Audit.WriteAsync(info, "instructor.link", TargetTypeEnum.Instructor.ToString(), instructor.Id, "Linked " + user.Id, token).ConfigureAwait(false);
            await _Services.Directory.ChangedAsync(TargetTypeEnum.Instructor, instructor.Id, token).ConfigureAwait(false);
            return await AdminGetAsync(actor, instructor.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/admin/instructors/{id}/unlink (directory staff). Audited as instructor.unlink.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="id">Instructor.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Admin instructor.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">403 Forbidden, 404 NotFound.</exception>
        public async Task<InstructorView> UnlinkAsync(ContentActor actor, RequestInfo info, string id, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            DirectoryAccess.RequireStaff(actor);
            Instructor instructor = await _Services.Database.DirectoryListings.Instructors.ReadAsync(id, token).ConfigureAwait(false) ?? throw ApiErrorException.NotFound("Instructor");
            if (instructor.LinkedUserId != null) await _Services.Database.DirectoryListings.Instructors.SetLinkedUserAsync(instructor.Id, null, DateTime.UtcNow, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "instructor.unlink", TargetTypeEnum.Instructor.ToString(), instructor.Id, "Unlinked by staff", token).ConfigureAwait(false);
            await _Services.Directory.ChangedAsync(TargetTypeEnum.Instructor, instructor.Id, token).ConfigureAwait(false);
            return await AdminGetAsync(actor, instructor.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// POST /api/v1/me/instructor-links/{instructorId}/unlink: the linked member removes the link (always allowed,
        /// audited as instructor.unlink).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="info">Request (audit).</param>
        /// <param name="instructorId">Instructor.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">404 NotFound when the caller is not linked to it.</exception>
        public async Task SelfUnlinkAsync(ContentActor actor, RequestInfo info, string instructorId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(info);
            string userId = actor.RequireUserId();
            Instructor? instructor = await _Services.Database.DirectoryListings.Instructors.ReadAsync(instructorId, token).ConfigureAwait(false);
            if (instructor == null || instructor.LinkedUserId != userId) throw ApiErrorException.NotFound("Instructor link");
            await _Services.Database.DirectoryListings.Instructors.SetLinkedUserAsync(instructor.Id, null, DateTime.UtcNow, token).ConfigureAwait(false);
            await _Services.Audit.WriteAsync(info, "instructor.unlink", TargetTypeEnum.Instructor.ToString(), instructor.Id, "Unlinked by the member", token).ConfigureAwait(false);
            await _Services.Directory.ChangedAsync(TargetTypeEnum.Instructor, instructor.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Find a live instructor by identifier, slug, or previous slug.
        /// </summary>
        /// <param name="idOrSlug">Identifier or slug.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Instructor, or null.</returns>
        public async Task<Instructor?> FindAsync(string? idOrSlug, CancellationToken token = default)
        {
            string key = (idOrSlug ?? String.Empty).Trim();
            if (key.Length == 0) return null;
            if (key.StartsWith(DirectoryIdPrefixes.Instructor, StringComparison.Ordinal))
            {
                Instructor? byId = await _Services.Database.DirectoryListings.Instructors.ReadAsync(key, token).ConfigureAwait(false);
                if (byId != null) return byId;
            }
            return await _Services.Database.DirectoryListings.Instructors.ReadBySlugAsync(key, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Validate a request and produce the instructor to store (not saved).
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="request">Request.</param>
        /// <param name="existing">Current instructor, or null for a create.</param>
        /// <param name="present">Fields present in an edit body, or null.</param>
        /// <param name="photoOwnerUserId">Member whose unattached photo may be attached, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The instructor to store.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ApiErrorException">400 ValidationFailed, UnknownOption, ContentFiltered.</exception>
        public async Task<Instructor> BuildAsync(ContentActor actor, InstructorRequest request, Instructor? existing, HashSet<string>? present, string? photoOwnerUserId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(request);
            Dictionary<string, string> errors = new Dictionary<string, string>();
            bool create = existing == null;
            DateTime now = DateTime.UtcNow;
            Instructor i = existing == null ? new Instructor { Id = IdGenerator.Generate(DirectoryIdPrefixes.Instructor), CreatedUtc = now, CreatedByUserId = actor.UserId } : existing.Clone();
            i.UpdatedUtc = now;
            bool Has(string name) => present == null || present.Contains(name);
            bool Set(string name, object? value) => Has(name) && (value != null || present != null);

            if (create || Has("displayName"))
            {
                string? name = DirectoryText.Text(request.DisplayName, 120, "displayName", errors);
                if (name == null || name.Length < 2) errors["displayName"] = "2 to 120 characters.";
                else i.DisplayName = name;
            }
            if (request.Slug != null && Has("slug"))
            {
                string slug = request.Slug.Trim().ToLowerInvariant();
                if (!DirectoryText.IsSlug(slug)) errors["slug"] = "2 to 80 lowercase letters, digits, and hyphens.";
                else i.Slug = slug;
            }
            else if (create) i.Slug = await FreeSlugAsync(DirectoryText.DeriveSlug(i.DisplayName, "instructor"), token).ConfigureAwait(false);
            if (Set("bio", request.Bio)) i.Bio = DirectoryText.Text(request.Bio, _Services.Settings.Directory.MaxDescriptionLength, "bio", errors);
            if (Set("affiliation", request.Affiliation)) i.Affiliation = DirectoryText.Text(request.Affiliation, 120, "affiliation", errors);
            if (Set("websiteUrl", request.WebsiteUrl)) i.WebsiteUrl = DirectoryText.HttpsUrl(request.WebsiteUrl, "websiteUrl", errors);
            if (Set("socialLinks", request.SocialLinks)) i.SocialLinks = DirectoryText.SocialLinks(request.SocialLinks, _Services.Settings.Directory.MaxSocialLinks, errors);
            if (Has("audience") && request.Audience != null)
            {
                if (request.Audience == ContentAudienceEnum.Public && !actor.IsSystemAdmin) errors["audience"] = "Only a SystemAdmin may set Public.";
                else i.Audience = request.Audience.Value;
            }
            else if (create) i.Audience = actor.Access.DefaultAudienceFor(FeatureKeyEnum.Directory);
            if (Set("photoMediaId", request.PhotoMediaId))
            {
                string? photo = String.IsNullOrWhiteSpace(request.PhotoMediaId) ? null : request.PhotoMediaId.Trim();
                if (photo != null) await _Services.Directory.Venues.ValidatePhotosAsync(actor, new List<string> { photo }, TargetTypeEnum.Instructor, existing?.Id, photoOwnerUserId, "photoMediaId", errors, token).ConfigureAwait(false);
                i.PhotoMediaId = photo;
            }
            if (Set("rank", request.Rank) || Set("rankLevel", request.RankLevel))
            {
                ProfileField? field = await _Services.Directory.Config.RankFieldAsync(token).ConfigureAwait(false);
                string? rank = Has("rank") && (request.Rank != null || present != null) ? (String.IsNullOrWhiteSpace(request.Rank) ? null : request.Rank.Trim()) : i.RankKey;
                int? level = Has("rankLevel") && (request.RankLevel != null || present != null) ? request.RankLevel : i.RankLevel;
                if (rank != null)
                {
                    if (field == null) errors["rank"] = "No Rank profile field is configured for instructors (directory.instructorRankFieldKey).";
                    else
                    {
                        ProfileFieldOption? option = field.Options.FirstOrDefault(o => !o.Retired && String.Equals(o.Key, rank, StringComparison.OrdinalIgnoreCase));
                        if (option == null)
                            throw new ApiErrorException(400, "UnknownOption", "Unknown rank " + rank + ".", new Dictionary<string, string> { { "rank", "Unknown option of the " + field.Key + " field." } });
                        rank = option.Key;
                        int max = option.MaxLevel ?? field.Rank?.MaxLevel ?? 0;
                        if (level != null && (level < 0 || level > max)) errors["rankLevel"] = "0 to " + max + ".";
                    }
                }
                else level = null;
                i.RankKey = rank;
                i.RankLevel = level;
            }
            if (errors.Count > 0) throw ApiErrorException.Validation(errors);
            await _Services.WordFilter.CheckAsync(actor, TargetTypeEnum.Instructor, new Dictionary<string, string?> { { "displayName", i.DisplayName }, { "bio", i.Bio }, { "affiliation", i.Affiliation } }, token).ConfigureAwait(false);
            return i;
        }

        /// <summary>
        /// Attach the photo, publish public media copies, apply labels and tags, and announce the change.
        /// </summary>
        /// <param name="actor">Caller.</param>
        /// <param name="instructor">Stored instructor.</param>
        /// <param name="labels">Labels, or null to keep.</param>
        /// <param name="tags">Tags, or null to keep.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task AfterSaveAsync(ContentActor actor, Instructor instructor, List<string>? labels, Dictionary<string, string>? tags, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(instructor);
            await _Services.Media.AttachAsync(TargetTypeEnum.Instructor, instructor.Id, instructor.PhotoMediaId == null ? new List<string>() : new List<string> { instructor.PhotoMediaId }, token).ConfigureAwait(false);
            if (labels != null || tags != null) await _Services.Metadata.ApplyAsync(actor, TargetTypeEnum.Instructor, instructor.Id, labels, tags, token).ConfigureAwait(false);
            await _Services.Directory.ChangedAsync(TargetTypeEnum.Instructor, instructor.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Notify a member that an instructor entry is linked to their account (InstructorLinked).
        /// </summary>
        /// <param name="instructor">Instructor.</param>
        /// <param name="userId">Member.</param>
        /// <param name="actorUserId">Who linked it, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task NotifyLinkedAsync(Instructor instructor, string userId, string? actorUserId, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(instructor);
            if (String.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            DirectoryConfig config = await _Services.Directory.Config.CurrentAsync(token).ConfigureAwait(false);
            List<string> venueIds = await _Services.Database.DirectoryListings.Instructors.VenueIdsAsync(instructor.Id, 1, token).ConfigureAwait(false);
            Venue? venue = venueIds.Count == 0 ? null : await _Services.Database.DirectoryListings.Venues.ReadAsync(venueIds[0], token).ConfigureAwait(false);
            string label = config.InstructorLabel.ToLowerInvariant();
            string title = venue == null ? "You are now listed as a " + label + " in the directory" : "You are now listed as a " + label + " at " + venue.Name;
            await _Services.Notifications.CreateAsync(new NotificationDraft
            {
                RecipientUserId = userId,
                Type = NotificationTypeEnum.InstructorLinked,
                ActorUserId = actorUserId == userId ? null : actorUserId,
                TargetType = TargetTypeEnum.Instructor,
                TargetId = instructor.Id,
                TitleText = title,
                Path = "/instructors/" + instructor.Id
            }, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static InstructorFilter Filter(ContentActor actor, EnumerationQuery q, bool admin)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            InstructorFilter filter = new InstructorFilter
            {
                VenueId = q.FilterString("venueId"),
                RankKeys = DirectoryFilters.Strings(q, "rank"),
                Claimed = q.FilterBool("claimed", errors),
                LinkedUserId = q.FilterString("linkedUserId"),
                AudienceFilter = DirectoryFilters.Enums<ContentAudienceEnum>(q, "audience", errors),
                Audiences = DirectoryAccess.ListedAudiences(actor),
                LiveOnly = !actor.IsDirectoryStaff
            };
            if (admin)
            {
                List<InstructorStatusEnum> statuses = DirectoryFilters.Enums<InstructorStatusEnum>(q, "status", errors);
                filter.Statuses = statuses.Count > 0 ? statuses : new List<InstructorStatusEnum> { InstructorStatusEnum.Active, InstructorStatusEnum.Archived };
                filter.LiveOnly = false;
            }
            DirectoryFilters.ThrowIfAny(errors);
            return filter;
        }

        private async Task<List<string>> VenueIdsAsync(List<string>? ids, CancellationToken token)
        {
            List<string> list = (ids ?? new List<string>()).Where(i => !String.IsNullOrWhiteSpace(i)).Select(i => i.Trim()).Distinct(StringComparer.Ordinal).Take(50).ToList();
            if (list.Count == 0) return list;
            List<Venue> venues = await _Services.Database.DirectoryListings.Venues.ReadManyAsync(list, token).ConfigureAwait(false);
            HashSet<string> found = new HashSet<string>(venues.Select(v => v.Id), StringComparer.Ordinal);
            string? missing = list.FirstOrDefault(i => !found.Contains(i));
            if (missing != null) throw ApiErrorException.NotFound("Venue");
            return list;
        }

        private async Task<string> FreeSlugAsync(string slug, CancellationToken token)
        {
            string candidate = slug;
            for (int n = 2; n < 50; n++)
            {
                Instructor? taken = await _Services.Database.DirectoryListings.Instructors.ReadBySlugAsync(candidate, token).ConfigureAwait(false);
                if (taken == null || !String.Equals(taken.Slug, candidate, StringComparison.OrdinalIgnoreCase)) return candidate;
                string suffix = "-" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
                candidate = (slug.Length + suffix.Length > 80 ? slug.Substring(0, 80 - suffix.Length) : slug) + suffix;
            }
            return slug + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        private static ApiErrorException SlugInUse()
        {
            return new ApiErrorException(409, "SlugInUse", "Another instructor uses this slug.", new Dictionary<string, string> { { "slug", "In use." } });
        }

        private static ApiErrorException AlreadyClaimed(string message)
        {
            return new ApiErrorException(409, "AlreadyClaimed", message);
        }

        #endregion
    }
}
